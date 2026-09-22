using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using SqlFluff.Ssms.Core;
using SqlFluff.Ssms.Services;

namespace SqlFluff.Ssms.Editor
{
    [Export(typeof(ISuggestedActionsSourceProvider))]
    [Name("SqlFluff Suggested Actions")]
    [ContentType("text")]
    internal sealed class SqlFluffSuggestedActionsSourceProvider : ISuggestedActionsSourceProvider
    {
        [Import]
        private ITextDocumentFactoryService Documents { get; set; }

        public ISuggestedActionsSource CreateSuggestedActionsSource(ITextView textView, ITextBuffer textBuffer)
        {
            if (textView == null || textBuffer == null || textView.TextBuffer != textBuffer)
            {
                return null;
            }

            // "text" is the broadest content type MEF offers; without SSMS's own SQL content type
            // name to target precisely, filter here so the Light Bulb doesn't offer SQL fixes in
            // every text editor.
            if (!SqlBufferHeuristics.IsLikelySql(textBuffer, Documents))
            {
                return null;
            }

            return new SqlFluffSuggestedActionsSource(textView, textBuffer);
        }
    }

    internal sealed class SqlFluffSuggestedActionsSource : ISuggestedActionsSource
    {
        private readonly ITextView _view;
        private readonly ITextBuffer _buffer;

        public SqlFluffSuggestedActionsSource(ITextView view, ITextBuffer buffer)
        {
            _view = view;
            _buffer = buffer;
            ViolationStore.Subscribe(buffer, () => SuggestedActionsChanged?.Invoke(this, EventArgs.Empty));
        }

        public event EventHandler<EventArgs> SuggestedActionsChanged;

        public IEnumerable<SuggestedActionSet> GetSuggestedActions(
            ISuggestedActionCategorySet requestedActionCategories, SnapshotSpan range, CancellationToken cancellationToken)
        {
            if (!Matches(range))
            {
                yield break;
            }

            var actions = new List<ISuggestedAction>
            {
                new SqlFluffFixAction(_view, _buffer, isFormat: false),
                new SqlFluffFixAction(_view, _buffer, isFormat: true),
            };

            yield return new SuggestedActionSet(
                PredefinedSuggestedActionCategoryNames.CodeFix, actions, "SQLFluff");
        }

        public Task<bool> HasSuggestedActionsAsync(
            ISuggestedActionCategorySet requestedActionCategories, SnapshotSpan range, CancellationToken cancellationToken)
        {
            return Task.FromResult(Matches(range));
        }

        public bool TryGetTelemetryId(out Guid telemetryId)
        {
            telemetryId = Guid.Empty;
            return false;
        }

        public void Dispose()
        {
        }

        private bool Matches(SnapshotSpan range)
        {
            ViolationSet set = ViolationStore.Get(_buffer);
            if (set == null || set.Entries.Count == 0)
            {
                return false;
            }

            return set.Entries.Any(entry =>
            {
                SnapshotSpan translated = new SnapshotSpan(set.Snapshot, entry.Span)
                    .TranslateTo(range.Snapshot, SpanTrackingMode.EdgeInclusive);
                return translated.IntersectsWith(range) || translated.Contains(range.Start);
            });
        }
    }

    internal sealed class SqlFluffFixAction : ISuggestedAction
    {
        private readonly IWpfTextView _view;
        private readonly ITextBuffer _buffer;
        private readonly bool _isFormat;

        public SqlFluffFixAction(ITextView view, ITextBuffer buffer, bool isFormat)
        {
            _view = view as IWpfTextView;
            _buffer = buffer;
            _isFormat = isFormat;
        }

        public string DisplayText => _isFormat
            ? "Format with SQLFluff (safe rules only)"
            : "Fix with SQLFluff (all fixable issues)";

        public string IconAutomationText => null;
        public ImageMoniker IconMoniker => default;
        public string InputGestureText => null;
        public bool HasActionSets => false;
        public bool HasPreview => false;

        public Task<IEnumerable<SuggestedActionSet>> GetActionSetsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(Enumerable.Empty<SuggestedActionSet>());
        }

        public Task<object> GetPreviewAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<object>(null);
        }

        public void Invoke(CancellationToken cancellationToken)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_view == null || SqlFluffPackage.Instance == null)
            {
                return;
            }

            string path = SqlFluffPackage.Instance.EditorServices.GetPath(_buffer);
            LintService lint = SqlFluffPackage.Instance.LintService;

            ThreadHelper.JoinableTaskFactory
                .RunAsync(() => _isFormat ? lint.FormatAsync(_view, _buffer, path) : lint.FixAsync(_view, _buffer, path))
                .Task.FileAndForget(_isFormat ? "sqlfluff/lightbulb-format" : "sqlfluff/lightbulb-fix");
        }

        public void Dispose()
        {
        }

        public bool TryGetTelemetryId(out Guid telemetryId)
        {
            telemetryId = Guid.Empty;
            return false;
        }
    }
}
