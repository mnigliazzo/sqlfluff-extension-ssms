using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;
using SqlFluff.Ssms.Core;

namespace SqlFluff.Ssms.Editor
{
    [Export(typeof(ITaggerProvider))]
    [ContentType("text")]
    [TagType(typeof(IErrorTag))]
    internal sealed class SqlFluffTaggerProvider : ITaggerProvider
    {
        [Import]
        private ITextDocumentFactoryService Documents { get; set; }

        public ITagger<T> CreateTagger<T>(ITextBuffer buffer) where T : ITag
        {
            // "text" is the broadest content type MEF offers; without SSMS's own SQL content type
            // name to target precisely, filter here so squiggles don't show up in every text editor.
            if (!SqlBufferHeuristics.IsLikelySql(buffer, Documents))
            {
                return null;
            }

            return buffer.Properties.GetOrCreateSingletonProperty(
                typeof(SqlFluffTagger), () => new SqlFluffTagger(buffer)) as ITagger<T>;
        }
    }

    internal sealed class SqlFluffTagger : ITagger<IErrorTag>
    {
        private readonly ITextBuffer _buffer;

        public SqlFluffTagger(ITextBuffer buffer)
        {
            _buffer = buffer;
            ViolationStore.Subscribe(buffer, OnStoreChanged);
        }

        public event EventHandler<SnapshotSpanEventArgs> TagsChanged;

        public IEnumerable<ITagSpan<IErrorTag>> GetTags(NormalizedSnapshotSpanCollection spans)
        {
            ViolationSet set = ViolationStore.Get(_buffer);
            if (set == null || spans.Count == 0)
            {
                yield break;
            }

            ITextSnapshot current = spans[0].Snapshot;
            foreach (ViolationEntry entry in set.Entries)
            {
                SnapshotSpan translated = new SnapshotSpan(set.Snapshot, entry.Span)
                    .TranslateTo(current, SpanTrackingMode.EdgeInclusive);

                if (!spans.IntersectsWith(translated))
                {
                    continue;
                }

                string errorType = ErrorTypeFor(entry.Violation, set.Severity);
                yield return new TagSpan<IErrorTag>(translated, new ErrorTag(errorType, entry.Violation.Message));
            }
        }

        private void OnStoreChanged()
        {
            ITextSnapshot snapshot = _buffer.CurrentSnapshot;
            TagsChanged?.Invoke(this, new SnapshotSpanEventArgs(new SnapshotSpan(snapshot, 0, snapshot.Length)));
        }

        internal static string ErrorTypeFor(LintViolation violation, DiagnosticSeverity severity)
        {
            if (violation.IsParseError)
            {
                return PredefinedErrorTypeNames.SyntaxError;
            }

            switch (severity)
            {
                case DiagnosticSeverity.Error:
                    return PredefinedErrorTypeNames.CompilerError;
                case DiagnosticSeverity.Message:
                    return PredefinedErrorTypeNames.Suggestion;
                default:
                    return PredefinedErrorTypeNames.Warning;
            }
        }
    }
}
