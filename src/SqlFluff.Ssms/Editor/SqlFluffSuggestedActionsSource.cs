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
            ViolationEntry match = FindMatch(range, out ITextSnapshot matchSnapshot);
            if (match == null)
            {
                yield break;
            }

            var actions = new List<ISuggestedAction>();

            // Parse errors (PRS/LXR/TMP) aren't sqlfluff rule codes, so they can't be targeted via
            // --rules — only the blanket Fix/Format actions apply to those.
            if (!match.Violation.IsParseError)
            {
                // Some rules (e.g. AM04 "ambiguous column count") have no autofix at all - offering
                // "Fix this issue" for one would just be a silent no-op (sqlfluff exits reporting
                // "Unfixable violations detected" with the document unchanged). Suppressing it here
                // is still available regardless.
                if (match.Violation.IsFixable)
                {
                    actions.Add(new SqlFluffFixAction(_view, _buffer, match.Violation.Code, match.Violation.StartLine));
                }

                actions.Add(new SqlFluffNoqaAction(_view, _buffer, matchSnapshot, match.Span, match.Violation.Code));
            }

            actions.Add(new SqlFluffFixAction(_view, _buffer, isFormat: false));
            actions.Add(new SqlFluffFixAction(_view, _buffer, isFormat: true));

            yield return new SuggestedActionSet(
                PredefinedSuggestedActionCategoryNames.CodeFix, actions, "SQLFluff");
        }

        public Task<bool> HasSuggestedActionsAsync(
            ISuggestedActionCategorySet requestedActionCategories, SnapshotSpan range, CancellationToken cancellationToken)
        {
            return Task.FromResult(FindMatch(range, out _) != null);
        }

        public bool TryGetTelemetryId(out Guid telemetryId)
        {
            telemetryId = Guid.Empty;
            return false;
        }

        public void Dispose()
        {
        }

        private ViolationEntry FindMatch(SnapshotSpan range, out ITextSnapshot snapshot)
        {
            ViolationSet set = ViolationStore.Get(_buffer);
            snapshot = set?.Snapshot;
            if (set == null || set.Entries.Count == 0)
            {
                return null;
            }

            foreach (ViolationEntry entry in set.Entries)
            {
                SnapshotSpan translated = new SnapshotSpan(set.Snapshot, entry.Span)
                    .TranslateTo(range.Snapshot, SpanTrackingMode.EdgeInclusive);
                if (translated.IntersectsWith(range) || translated.Contains(range.Start))
                {
                    return entry;
                }
            }

            return null;
        }
    }

    internal sealed class SqlFluffFixAction : ISuggestedAction
    {
        private readonly IWpfTextView _view;
        private readonly ITextBuffer _buffer;
        private readonly bool _isFormat;
        private readonly string _ruleCode;
        private readonly int _ruleTargetLine;

        // Fix or Format the whole document/selection.
        public SqlFluffFixAction(ITextView view, ITextBuffer buffer, bool isFormat)
        {
            _view = view as IWpfTextView;
            _buffer = buffer;
            _isFormat = isFormat;
        }

        // Fix only the rule of the violation under the cursor, keeping only the diff hunk that
        // touches targetLine (see LintService.FixRuleAsync) — sqlfluff can't target a single
        // violation instance by position, so this is a best-effort single-violation fix.
        public SqlFluffFixAction(ITextView view, ITextBuffer buffer, string ruleCode, int targetLine)
        {
            _view = view as IWpfTextView;
            _buffer = buffer;
            _ruleCode = ruleCode;
            _ruleTargetLine = targetLine;
        }

        public string DisplayText => _ruleCode != null
            ? "Fix this issue with SQLFluff (" + _ruleCode + ")"
            : _isFormat
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

            Func<Task> operation = _ruleCode != null
                ? () => lint.FixRuleAsync(_view, _buffer, path, _ruleCode, _ruleTargetLine)
                : _isFormat
                    ? (Func<Task>)(() => lint.FormatAsync(_view, _buffer, path))
                    : () => lint.FixAsync(_view, _buffer, path);

            ThreadHelper.JoinableTaskFactory
                .RunAsync(operation)
                .Task.FileAndForget(_ruleCode != null ? "sqlfluff/lightbulb-fix-rule" : _isFormat ? "sqlfluff/lightbulb-format" : "sqlfluff/lightbulb-fix");
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

    // Inserts a `-- noqa: <rule>` comment on the violation's line instead of rewriting SQL — see
    // LintService.SuppressViolationAsync / NoqaCommentEditor for why this stays a thin wrapper
    // around sqlfluff's own noqa mechanism rather than a separate suppression system.
    internal sealed class SqlFluffNoqaAction : ISuggestedAction
    {
        private readonly IWpfTextView _view;
        private readonly ITextBuffer _buffer;
        private readonly ITextSnapshot _snapshot;
        private readonly Span _span;
        private readonly string _ruleCode;

        public SqlFluffNoqaAction(ITextView view, ITextBuffer buffer, ITextSnapshot snapshot, Span span, string ruleCode)
        {
            _view = view as IWpfTextView;
            _buffer = buffer;
            _snapshot = snapshot;
            _span = span;
            _ruleCode = ruleCode;
        }

        public string DisplayText => "Suppress this issue with SQLFluff (-- noqa: " + _ruleCode + ")";
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
                .RunAsync(() => lint.SuppressViolationAsync(_buffer, path, _snapshot, _span, _ruleCode))
                .Task.FileAndForget("sqlfluff/lightbulb-noqa");
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
