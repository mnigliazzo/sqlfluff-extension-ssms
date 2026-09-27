using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Shell.TableControl;
using Microsoft.VisualStudio.Shell.TableManager;
using Microsoft.VisualStudio.Text;
using SqlFluff.Ssms.Core;
using SqlFluff.Ssms.Editor;

namespace SqlFluff.Ssms.Services
{
    // Feeds the Error List through the table API (ITableDataSource), the same model VS/SSMS
    // language services use: each open buffer owns one group of entries (a snapshot factory) that
    // is replaced on every re-lint and removed when the buffer closes. Ownership is by buffer, not
    // by path, so two documents that resolve to the same path (or none, like unsaved queries)
    // can't clear or overwrite each other's entries. Folder-wide Lint results for files that
    // aren't open have no buffer, so those groups are owned by their file path instead.
    internal sealed class ErrorListService : ITableDataSource, IDisposable
    {
        public const string SourceIdentifier = "SqlFluff.Ssms.ErrorList";

        private readonly IServiceProvider _serviceProvider;
        private readonly ITableManager _tableManager;
        private readonly object _gate = new object();
        private readonly List<ITableDataSink> _sinks = new List<ITableDataSink>();
        private readonly Dictionary<ITextBuffer, EntriesFactory> _byBuffer = new Dictionary<ITextBuffer, EntriesFactory>();
        private readonly Dictionary<string, EntriesFactory> _byPath =
            new Dictionary<string, EntriesFactory>(StringComparer.OrdinalIgnoreCase);

        public ErrorListService(IServiceProvider serviceProvider, ITableManagerProvider tableManagerProvider)
        {
            _serviceProvider = serviceProvider;
            _tableManager = tableManagerProvider.GetTableManager(StandardTables.ErrorsTable);
            _tableManager.AddSource(
                this,
                StandardTableColumnDefinitions.DetailsExpander,
                StandardTableColumnDefinitions.ErrorSeverity,
                StandardTableColumnDefinitions.ErrorCode,
                StandardTableColumnDefinitions.ErrorSource,
                StandardTableColumnDefinitions.BuildTool,
                StandardTableColumnDefinitions.Text,
                StandardTableColumnDefinitions.DocumentName,
                StandardTableColumnDefinitions.Line,
                StandardTableColumnDefinitions.Column);
        }

        public string SourceTypeIdentifier => StandardTableDataSources.ErrorTableDataSource;

        public string Identifier => SourceIdentifier;

        public string DisplayName => "SQLFluff";

        public IDisposable Subscribe(ITableDataSink sink)
        {
            List<EntriesFactory> existing;
            lock (_gate)
            {
                _sinks.Add(sink);
                existing = _byBuffer.Values.Concat(_byPath.Values).ToList();
            }

            foreach (EntriesFactory factory in existing)
            {
                sink.AddFactory(factory);
            }

            return new Subscription(this, sink);
        }

        public void Publish(ITextBuffer buffer, string path, ViolationSet set, Action<ViolationEntry> navigate)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var rows = new List<Row>(set.Entries.Count);
            foreach (ViolationEntry entry in set.Entries)
            {
                ITextSnapshotLine line = set.Snapshot.GetLineFromPosition(Math.Min(entry.Span.Start, set.Snapshot.Length));
                ViolationEntry captured = entry;
                rows.Add(new Row(
                    entry.Violation,
                    CategoryFor(entry.Violation, set.Severity),
                    path,
                    line.LineNumber,
                    Math.Max(0, entry.Span.Start - line.Start.Position),
                    () => navigate(captured)));
            }

            // A live buffer's results supersede any folder-wide Lint results for the same file.
            if (!string.IsNullOrEmpty(path))
            {
                RemoveFactory(_byPath, path);
            }

            SetFactory(_byBuffer, buffer, rows);
        }

        // For violations with no live ITextSnapshot - the folder-wide Lint command, which reads
        // closed files straight off disk. Line/col come directly from sqlfluff's (1-based) report.
        public void PublishRaw(string path, IReadOnlyList<LintViolation> violations, DiagnosticSeverity severity, Action<LintViolation> navigate)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var rows = new List<Row>(violations.Count);
            foreach (LintViolation violation in violations)
            {
                LintViolation captured = violation;
                rows.Add(new Row(
                    violation,
                    CategoryFor(violation, severity),
                    path,
                    Math.Max(0, violation.StartLine - 1),
                    Math.Max(0, violation.StartColumn - 1),
                    () => navigate(captured)));
            }

            SetFactory(_byPath, path ?? string.Empty, rows);
        }

        public void Clear(ITextBuffer buffer)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            RemoveFactory(_byBuffer, buffer);
        }

        // For when a document closes and its buffer can no longer be resolved: drops every group
        // shown for that path, whichever owner it belongs to.
        public void ClearDocument(string path)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            RemoveFactory(_byPath, path);

            List<ITextBuffer> owners;
            lock (_gate)
            {
                owners = _byBuffer
                    .Where(pair => string.Equals(pair.Value.DocumentPath, path, StringComparison.OrdinalIgnoreCase))
                    .Select(pair => pair.Key)
                    .ToList();
            }

            foreach (ITextBuffer owner in owners)
            {
                RemoveFactory(_byBuffer, owner);
            }
        }

        public void Show()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            (_serviceProvider.GetService(typeof(SVsErrorList)) as IVsErrorList)?.BringToFront();
        }

        public void Dispose()
        {
            _tableManager.RemoveSource(this);
        }

        private void SetFactory<TKey>(Dictionary<TKey, EntriesFactory> owners, TKey owner, List<Row> rows)
        {
            if (rows.Count == 0)
            {
                RemoveFactory(owners, owner);
                return;
            }

            EntriesFactory factory;
            bool added;
            List<ITableDataSink> sinks;
            lock (_gate)
            {
                added = !owners.TryGetValue(owner, out factory);
                if (added)
                {
                    factory = new EntriesFactory(rows);
                    owners[owner] = factory;
                }
                else
                {
                    factory.Update(rows);
                }

                sinks = _sinks.ToList();
            }

            foreach (ITableDataSink sink in sinks)
            {
                if (added)
                {
                    sink.AddFactory(factory);
                }
                else
                {
                    sink.FactorySnapshotChanged(factory);
                }
            }
        }

        private void RemoveFactory<TKey>(Dictionary<TKey, EntriesFactory> owners, TKey owner)
        {
            EntriesFactory factory;
            List<ITableDataSink> sinks;
            lock (_gate)
            {
                if (!owners.TryGetValue(owner, out factory))
                {
                    return;
                }

                owners.Remove(owner);
                sinks = _sinks.ToList();
            }

            foreach (ITableDataSink sink in sinks)
            {
                sink.RemoveFactory(factory);
            }
        }

        private void Unsubscribe(ITableDataSink sink)
        {
            lock (_gate)
            {
                _sinks.Remove(sink);
            }
        }

        private static __VSERRORCATEGORY CategoryFor(LintViolation violation, DiagnosticSeverity severity)
        {
            if (violation.IsParseError)
            {
                return __VSERRORCATEGORY.EC_ERROR;
            }

            switch (severity)
            {
                case DiagnosticSeverity.Error:
                    return __VSERRORCATEGORY.EC_ERROR;
                case DiagnosticSeverity.Message:
                    return __VSERRORCATEGORY.EC_MESSAGE;
                default:
                    return __VSERRORCATEGORY.EC_WARNING;
            }
        }

        // The Error List's File column shows only the bare filename, so two files with the same
        // name in different folders - e.g. a migration and its rollback both named "001.sql" -
        // would be indistinguishable. Prefixing the parent folder disambiguates them.
        private static string BuildText(string path, string message)
        {
            if (string.IsNullOrEmpty(path))
            {
                return message;
            }

            string fileName;
            string parent;
            try
            {
                fileName = Path.GetFileName(path);
                parent = Path.GetFileName(Path.GetDirectoryName(path));
            }
            catch (ArgumentException)
            {
                return message;
            }

            string shortPath = string.IsNullOrEmpty(parent) ? fileName : Path.Combine(parent, fileName);
            return string.IsNullOrEmpty(shortPath) ? message : "[" + shortPath + "] " + message;
        }

        private sealed class Subscription : IDisposable
        {
            private readonly ErrorListService _owner;
            private readonly ITableDataSink _sink;

            public Subscription(ErrorListService owner, ITableDataSink sink)
            {
                _owner = owner;
                _sink = sink;
            }

            public void Dispose() => _owner.Unsubscribe(_sink);
        }

        internal sealed class Row
        {
            public Row(LintViolation violation, __VSERRORCATEGORY category, string path, int line, int column, Action navigate)
            {
                Code = violation.Code;
                Text = BuildText(path, violation.Message);
                Category = category;
                Path = path;
                Line = line;
                Column = column;
                Navigate = navigate;
            }

            public string Code { get; }
            public string Text { get; }
            public __VSERRORCATEGORY Category { get; }
            public string Path { get; }
            public int Line { get; }
            public int Column { get; }
            public Action Navigate { get; }
        }

        private sealed class EntriesFactory : TableEntriesSnapshotFactoryBase
        {
            // Read by the Error List from background threads, replaced on the UI thread.
            private volatile EntriesSnapshot _current;

            public EntriesFactory(List<Row> rows)
            {
                _current = new EntriesSnapshot(rows, versionNumber: 0);
            }

            public string DocumentPath => _current.DocumentPath;

            public override int CurrentVersionNumber => _current.VersionNumber;

            public void Update(List<Row> rows)
            {
                _current = new EntriesSnapshot(rows, _current.VersionNumber + 1);
            }

            public override ITableEntriesSnapshot GetCurrentSnapshot() => _current;

            public override ITableEntriesSnapshot GetSnapshot(int versionNumber)
            {
                EntriesSnapshot current = _current;
                return current.VersionNumber == versionNumber ? current : null;
            }
        }

        internal sealed class EntriesSnapshot : TableEntriesSnapshotBase
        {
            private readonly List<Row> _rows;

            public EntriesSnapshot(List<Row> rows, int versionNumber)
            {
                _rows = rows;
                VersionNumber = versionNumber;
            }

            public override int VersionNumber { get; }

            public override int Count => _rows.Count;

            public string DocumentPath => _rows.Count > 0 ? _rows[0].Path : null;

            public bool TryNavigate(int index)
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                if (index < 0 || index >= _rows.Count)
                {
                    return false;
                }

                _rows[index].Navigate();
                return true;
            }

            public override bool TryGetValue(int index, string keyName, out object content)
            {
                content = null;
                if (index < 0 || index >= _rows.Count)
                {
                    return false;
                }

                Row row = _rows[index];
                switch (keyName)
                {
                    case StandardTableKeyNames.ErrorSeverity:
                        content = row.Category;
                        return true;
                    case StandardTableKeyNames.Text:
                        content = row.Text;
                        return true;
                    case StandardTableKeyNames.ErrorCode:
                        content = row.Code;
                        return true;
                    case StandardTableKeyNames.DocumentName:
                        content = row.Path;
                        return true;
                    case StandardTableKeyNames.Line:
                        content = row.Line;
                        return true;
                    case StandardTableKeyNames.Column:
                        content = row.Column;
                        return true;
                    case StandardTableKeyNames.BuildTool:
                        content = "SQLFluff";
                        return true;
                    case StandardTableKeyNames.ErrorSource:
                        content = ErrorSource.Other;
                        return true;
                    default:
                        return false;
                }
            }
        }
    }
}
