using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using SqlFluff.Ssms.Core;
using SqlFluff.Ssms.Editor;

namespace SqlFluff.Ssms.Services
{
    internal sealed class LintService
    {
        private sealed class BufferState
        {
            public CancellationTokenSource Lint;
            public CancellationTokenSource Debounce;
        }

        private readonly SqlFluffPackage _package;
        private readonly EditorServices _editor;
        private readonly ErrorListService _errors;
        private readonly ConditionalWeakTable<ITextBuffer, BufferState> _state = new ConditionalWeakTable<ITextBuffer, BufferState>();
        private string _lastAutomaticFailure;

        public LintService(SqlFluffPackage package, EditorServices editor, ErrorListService errors)
        {
            _package = package;
            _editor = editor;
            _errors = errors;
        }

        public void Schedule(ITextBuffer buffer, string path, int delayMs)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            BufferState state = _state.GetOrCreateValue(buffer);
            state.Debounce?.Cancel();
            var cts = new CancellationTokenSource();
            state.Debounce = cts;

            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                try
                {
                    await Task.Delay(delayMs, cts.Token);
                    await LintAsync(buffer, path, userInitiated: false);
                }
                catch (OperationCanceledException)
                {
                }
            }).Task.FileAndForget("sqlfluff/schedule");
        }

        // target == null lints the whole document (used by every automatic trigger: open, save,
        // while-typing). A non-null target — the current selection, from the interactive Lint
        // command — lints only that span; the reported line/col, relative to that fragment, are
        // then shifted back onto the full document before being published.
        public async Task LintAsync(ITextBuffer buffer, string path, bool userInitiated, Span? target = null)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            BufferState state = _state.GetOrCreateValue(buffer);
            state.Debounce?.Cancel();
            state.Lint?.Cancel();
            var cts = new CancellationTokenSource();
            state.Lint = cts;

            SqlFluffSettings settings = ResolveEffectiveSettings(_package.GetSettings(), path);
            ITextSnapshot snapshot = buffer.CurrentSnapshot;
            bool isSelection = target.HasValue;
            string text = isSelection ? snapshot.GetText(target.Value) : snapshot.GetText();

            if (string.IsNullOrWhiteSpace(text))
            {
                if (isSelection)
                {
                    OutputLog.SetStatus("SQLFluff: nothing to lint.");
                }
                else
                {
                    ClearDiagnostics(buffer, path);
                }
                return;
            }

            int lineOffset = 0;
            int firstLineColumnOffset = 0;
            if (isSelection)
            {
                ITextSnapshotLine startLine = snapshot.GetLineFromPosition(target.Value.Start);
                lineOffset = startLine.LineNumber;
                firstLineColumnOffset = target.Value.Start - startLine.Start.Position;
            }

            if (userInitiated)
            {
                OutputLog.SetStatus(isSelection ? "SQLFluff: linting selection..." : "SQLFluff: linting...");
            }

            try
            {
                IReadOnlyList<LintViolation> violations =
                    await Task.Run(() => SqlFluffRunner.LintAsync(text, path, settings, cts.Token), cts.Token);

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (cts.IsCancellationRequested)
                {
                    return;
                }

                if (isSelection && (lineOffset > 0 || firstLineColumnOffset > 0))
                {
                    OffsetViolations(violations, lineOffset, firstLineColumnOffset);
                }

                int count = Publish(buffer, path, snapshot, violations, settings);
                _lastAutomaticFailure = null;

                if (userInitiated)
                {
                    OutputLog.SetStatus(count == 0 ? "SQLFluff: no issues found." : "SQLFluff: " + count + " issue(s) found.");
                    if (count > 0)
                    {
                        _errors.Show();
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (SqlFluffException ex)
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                ReportFailure(ex.Message, userInitiated);
            }
            finally
            {
                if (ReferenceEquals(state.Lint, cts))
                {
                    state.Lint = null;
                }
            }
        }

        // Same "selection or whole document" targeting as Fix/Format (RewriteAsync) — used by the
        // interactive Lint command, which has a view/selection to check; automatic triggers don't
        // and call the buffer-only overload above directly.
        public Task LintAsync(IWpfTextView view, ITextBuffer buffer, string path, bool userInitiated)
        {
            Span? target = null;
            if (!view.Selection.IsEmpty && ReferenceEquals(view.TextBuffer, buffer))
            {
                SnapshotSpan selected = view.Selection.StreamSelectionSpan.SnapshotSpan;
                if (selected.Snapshot == buffer.CurrentSnapshot)
                {
                    target = selected.Span;
                }
            }

            return LintAsync(buffer, path, userInitiated, target);
        }

        private static void OffsetViolations(IReadOnlyList<LintViolation> violations, int lineOffset, int firstLineColumnOffset)
        {
            foreach (LintViolation v in violations)
            {
                if (v.StartLine == 1)
                {
                    v.StartColumn += firstLineColumnOffset;
                }
                v.StartLine += lineOffset;

                if (v.EndLine > 0)
                {
                    if (v.EndLine == 1)
                    {
                        v.EndColumn += firstLineColumnOffset;
                    }
                    v.EndLine += lineOffset;
                }
            }
        }

        public Task FixAsync(IWpfTextView view, ITextBuffer buffer, string path)
        {
            return RewriteAsync(view, buffer, path, RewriteMode.Fix, ruleFilter: null, targetLine: null);
        }

        public Task FormatAsync(IWpfTextView view, ITextBuffer buffer, string path)
        {
            return RewriteAsync(view, buffer, path, RewriteMode.Format, ruleFilter: null, targetLine: null);
        }

        // Restricts the fix to a single rule code (e.g. the one under a specific squiggle) AND, via
        // targetLine, to only the diff hunk touching that violation's line — sqlfluff has no way to
        // target a single violation instance by position, so it still fixes every occurrence of the
        // rule internally, but only the hunk overlapping targetLine is kept; the rest reverts back
        // to the original text. targetLine is 1-based, relative to the whole document.
        public Task FixRuleAsync(IWpfTextView view, ITextBuffer buffer, string path, string ruleCode, int targetLine)
        {
            return RewriteAsync(view, buffer, path, RewriteMode.Fix, ruleFilter: ruleCode, targetLine: targetLine);
        }

        // Inserts/merges a `-- noqa: <rule>` comment on the violation's line instead of rewriting
        // SQL — a thin wrapper around sqlfluff's own noqa mechanism (NoqaCommentEditor), not a
        // separate suppression system, so a pipeline running plain `sqlfluff lint` on the saved file
        // honors the same suppression. violationSnapshot/violationSpan locate the violation as of
        // the lint that found it; TranslateTo re-maps that onto whatever the buffer's current
        // snapshot is by the time the Light Bulb action is invoked.
        public async Task SuppressViolationAsync(ITextBuffer buffer, string path, ITextSnapshot violationSnapshot, Span violationSpan, string ruleCode)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (!buffer.CheckEditAccess())
            {
                ReportFailure("The document is read-only.", userInitiated: true);
                return;
            }

            ITextSnapshot current = buffer.CurrentSnapshot;
            SnapshotSpan translated = new SnapshotSpan(violationSnapshot, violationSpan).TranslateTo(current, SpanTrackingMode.EdgeInclusive);
            ITextSnapshotLine line = current.GetLineFromPosition(translated.Start.Position);

            string original = line.GetText();
            string updated = NoqaCommentEditor.AddNoqa(original, ruleCode);
            if (string.Equals(updated, original, StringComparison.Ordinal))
            {
                OutputLog.SetStatus("SQLFluff: " + ruleCode + " is already suppressed on this line.");
                return;
            }

            using (ITextEdit edit = buffer.CreateEdit())
            {
                edit.Replace(line.Extent, updated);
                edit.Apply();
            }

            OutputLog.SetStatus("SQLFluff: suppressed " + ruleCode + ". Press Ctrl+S to save.");
            await LintAsync(buffer, path, userInitiated: false);
        }

        private enum RewriteMode
        {
            Fix,
            Format,
        }

        // Runs Format synchronously from a save event (IVsRunningDocTableEvents3.OnBeforeSave),
        // so it must return before the caller proceeds with the actual save. No IWpfTextView
        // involved (a save isn't necessarily tied to a focused view) and always the whole
        // document (no selection concept applies to a save). Never throws: a formatting failure
        // must not block or corrupt the save, so callers get a bool and the buffer is left as-is
        // on failure.
        public async Task<bool> FormatForSaveAsync(ITextBuffer buffer, string path)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (!buffer.CheckEditAccess())
            {
                return false;
            }

            SqlFluffSettings settings = ResolveEffectiveSettings(_package.GetSettings(), path);
            ITextSnapshot snapshot = buffer.CurrentSnapshot;
            string original = snapshot.GetText();

            if (string.IsNullOrWhiteSpace(original))
            {
                return true;
            }

            string formatted;
            try
            {
                formatted = await Task.Run(() => SqlFluffRunner.FormatAsync(original, path, settings, CancellationToken.None));
            }
            catch (SqlFluffException ex)
            {
                OutputLog.Write("Format on save failed: " + ex.Message);
                return false;
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (buffer.CurrentSnapshot.Version.VersionNumber != snapshot.Version.VersionNumber)
            {
                OutputLog.Write("Format on save skipped: the document changed while formatting was running.");
                return false;
            }

            string newline = DominantNewline(snapshot);
            formatted = formatted.Replace("\r\n", "\n").Replace("\n", newline);

            if (string.Equals(formatted, original, StringComparison.Ordinal))
            {
                return true;
            }

            ApplyMinimalEdit(buffer, 0, original, formatted);
            return true;
        }

        public enum BatchRewriteOutcome
        {
            Unchanged,
            Rewritten,
            Failed,
        }

        public sealed class BatchRewriteResult
        {
            public BatchRewriteOutcome Outcome { get; set; }
            public string Error { get; set; }
        }

        // Fix/Format for a buffer that's open but not the focused view (the folder-wide batch
        // commands) — same shape as FormatForSaveAsync (no selection concept, whole buffer) but
        // covers both verbs and reports what happened instead of a bare bool.
        public async Task<BatchRewriteResult> RewriteBufferForBatchAsync(ITextBuffer buffer, string path, bool useFix)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (!buffer.CheckEditAccess())
            {
                return new BatchRewriteResult { Outcome = BatchRewriteOutcome.Failed, Error = "The document is read-only." };
            }

            SqlFluffSettings settings = ResolveEffectiveSettings(_package.GetSettings(), path);
            ITextSnapshot snapshot = buffer.CurrentSnapshot;
            string original = snapshot.GetText();

            if (string.IsNullOrWhiteSpace(original))
            {
                return new BatchRewriteResult { Outcome = BatchRewriteOutcome.Unchanged };
            }

            string rewritten;
            try
            {
                rewritten = useFix
                    ? await Task.Run(() => SqlFluffRunner.FixAsync(original, path, settings, CancellationToken.None))
                    : await Task.Run(() => SqlFluffRunner.FormatAsync(original, path, settings, CancellationToken.None));
            }
            catch (SqlFluffException ex)
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                return new BatchRewriteResult { Outcome = BatchRewriteOutcome.Failed, Error = ex.Message };
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (buffer.CurrentSnapshot.Version.VersionNumber != snapshot.Version.VersionNumber)
            {
                return new BatchRewriteResult { Outcome = BatchRewriteOutcome.Failed, Error = "The document changed while SQLFluff was running." };
            }

            string newline = DominantNewline(snapshot);
            rewritten = rewritten.Replace("\r\n", "\n").Replace("\n", newline);

            if (string.Equals(rewritten, original, StringComparison.Ordinal))
            {
                return new BatchRewriteResult { Outcome = BatchRewriteOutcome.Unchanged };
            }

            ApplyMinimalEdit(buffer, 0, original, rewritten);
            return new BatchRewriteResult { Outcome = BatchRewriteOutcome.Rewritten };
        }

        // Exposes the RDT-based save (see TrySaveDocumentAsync below) to the folder-wide batch
        // commands, which need to persist an open buffer they just rewrote.
        public Task<bool> SaveDocumentAsync(string path)
        {
            return TrySaveDocumentAsync(path);
        }

        private async Task RewriteAsync(IWpfTextView view, ITextBuffer buffer, string path, RewriteMode mode, string ruleFilter, int? targetLine)
        {
            string verb = mode == RewriteMode.Format ? "format" : "fix";
            string verbCapitalized = mode == RewriteMode.Format ? "Format" : "Fix";

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (!buffer.CheckEditAccess())
            {
                ReportFailure("The document is read-only.", userInitiated: true);
                return;
            }

            SqlFluffSettings settings = ResolveEffectiveSettings(_package.GetSettings(), path);
            if (!string.IsNullOrEmpty(ruleFilter))
            {
                settings = settings.Clone();
                settings.Rules = ruleFilter;
            }

            ITextSnapshot snapshot = buffer.CurrentSnapshot;

            Span target = new Span(0, snapshot.Length);
            bool isSelection = false;
            if (!view.Selection.IsEmpty && ReferenceEquals(view.TextBuffer, buffer))
            {
                SnapshotSpan selected = view.Selection.StreamSelectionSpan.SnapshotSpan;
                if (selected.Snapshot == snapshot)
                {
                    target = selected.Span;
                    isSelection = true;
                }
            }

            string original = snapshot.GetText(target);
            if (string.IsNullOrWhiteSpace(original))
            {
                OutputLog.SetStatus("SQLFluff: nothing to " + verb + ".");
                return;
            }

            OutputLog.SetStatus("SQLFluff: " + (mode == RewriteMode.Format ? "formatting..." : "fixing..."));

            string rewritten;
            try
            {
                rewritten = mode == RewriteMode.Format
                    ? await Task.Run(() => SqlFluffRunner.FormatAsync(original, path, settings, CancellationToken.None))
                    : await Task.Run(() => SqlFluffRunner.FixAsync(original, path, settings, CancellationToken.None));
            }
            catch (SqlFluffException ex)
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                ReportFailure(ex.Message, userInitiated: true);
                return;
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (buffer.CurrentSnapshot.Version.VersionNumber != snapshot.Version.VersionNumber)
            {
                ReportFailure("The document changed while SQLFluff was running. Run " + verbCapitalized + " again.", userInitiated: true);
                return;
            }

            string newline = DominantNewline(snapshot);
            rewritten = rewritten.Replace("\r\n", "\n").Replace("\n", newline);
            if (isSelection && !EndsWithNewline(original))
            {
                rewritten = rewritten.TrimEnd('\r', '\n');
            }

            if (targetLine.HasValue && !string.Equals(rewritten, original, StringComparison.Ordinal))
            {
                int targetStartLineInDoc = snapshot.GetLineFromPosition(target.Start).LineNumber; // 0-based
                int relativeLine = targetLine.Value - targetStartLineInDoc; // 1-based, relative to `original`
                rewritten = SelectHunkForLine(original, rewritten, newline, relativeLine);
            }

            if (string.Equals(rewritten, original, StringComparison.Ordinal))
            {
                OutputLog.SetStatus("SQLFluff: nothing to " + verb + ".");
                await LintAsync(buffer, path, userInitiated: false);
                return;
            }

            ApplyMinimalEdit(buffer, target.Start, original, rewritten);

            if (settings.AutoSaveAfterFix && !string.IsNullOrEmpty(path))
            {
                bool saved = await TrySaveDocumentAsync(path);
                OutputLog.SetStatus(saved
                    ? "SQLFluff: " + verb + " applied and saved."
                    : "SQLFluff: " + verb + " applied, but auto-save failed. Press Ctrl+S to save.");
            }
            else
            {
                OutputLog.SetStatus("SQLFluff: " + verb + " applied. Press Ctrl+S to save.");
            }

            await LintAsync(buffer, path, userInitiated: false);
        }

        // Saves via the Running Document Table so it goes through the same path as Ctrl+S
        // (respects read-only files, save-as-for-new-docs, etc.) instead of writing the file directly.
        private async Task<bool> TrySaveDocumentAsync(string path)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var rdt = Package.GetGlobalService(typeof(SVsRunningDocumentTable)) as IVsRunningDocumentTable;
            if (rdt == null)
            {
                return false;
            }

            IntPtr docDataPtr = IntPtr.Zero;
            try
            {
                int hr = rdt.FindAndLockDocument(
                    (uint)_VSRDTFLAGS.RDT_NoLock, path, out IVsHierarchy hierarchy, out uint itemId, out docDataPtr, out uint cookie);

                if (ErrorHandler.Failed(hr))
                {
                    OutputLog.Write("Auto-save: could not find '" + path + "' in the running document table (hr=0x" + hr.ToString("X8") + ").");
                    return false;
                }

                int saveHr = rdt.SaveDocuments((uint)__VSRDTSAVEOPTIONS.RDTSAVEOPT_SaveIfDirty, null, 0, cookie);
                if (ErrorHandler.Failed(saveHr))
                {
                    OutputLog.Write("Auto-save failed for '" + path + "' (hr=0x" + saveHr.ToString("X8") + ").");
                    return false;
                }

                return true;
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                OutputLog.Write("Auto-save failed for '" + path + "': " + ex.Message);
                return false;
            }
            finally
            {
                if (docDataPtr != IntPtr.Zero)
                {
                    Marshal.Release(docDataPtr);
                }
            }
        }

        public void Clear(ITextBuffer buffer, string path)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_state.TryGetValue(buffer, out BufferState state))
            {
                state.Debounce?.Cancel();
                state.Lint?.Cancel();
            }

            ClearDiagnostics(buffer, path);
        }

        public void DocumentClosed(string path)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _errors.Clear(path);
        }

        private int Publish(
            ITextBuffer buffer, string path, ITextSnapshot snapshot,
            IReadOnlyList<LintViolation> violations, SqlFluffSettings settings)
        {
            List<ViolationEntry> entries = violations
                .Select(v => new ViolationEntry(v, ViolationStore.ToSpan(snapshot, v)))
                .ToList();

            var set = new ViolationSet(snapshot, entries, settings.Severity);
            ViolationStore.Set(buffer, set);
            _errors.Publish(path, set, entry => NavigateTo(buffer, path, set, entry));
            return entries.Count;
        }

        private void ClearDiagnostics(ITextBuffer buffer, string path)
        {
            ViolationStore.Clear(buffer);
            _errors.Clear(path);
        }

        private void NavigateTo(ITextBuffer buffer, string path, ViolationSet set, ViolationEntry entry)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            bool active = _editor.TryGetActiveSqlView(out IWpfTextView view, out ITextBuffer activeBuffer, out _) &&
                          ReferenceEquals(activeBuffer, buffer);

            if (!active && !string.IsNullOrEmpty(path) && File.Exists(path))
            {
                try
                {
                    VsShellUtilities.OpenDocument(_package, path, Guid.Empty, out _, out _, out IVsWindowFrame frame);
                    frame?.Show();
                    active = _editor.TryGetActiveSqlView(out view, out activeBuffer, out _) &&
                             ReferenceEquals(activeBuffer, buffer);
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    OutputLog.Write("Could not open " + path + ": " + ex.Message);
                }
            }

            if (!active)
            {
                return;
            }

            SnapshotPoint point = new SnapshotSpan(set.Snapshot, entry.Span)
                .TranslateTo(view.TextSnapshot, SpanTrackingMode.EdgeInclusive).Start;
            view.Caret.MoveTo(point);
            view.ViewScroller.EnsureSpanVisible(new SnapshotSpan(point, 0), EnsureSpanVisibleOptions.AlwaysCenter);
            view.VisualElement.Focus();
        }

        private void ReportFailure(string message, bool userInitiated)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            OutputLog.Write(message);

            if (userInitiated)
            {
                OutputLog.SetStatus("SQLFluff: failed. See the SQLFluff output pane for details.");
                VsShellUtilities.ShowMessageBox(
                    _package, message, "SQLFluff",
                    OLEMSGICON.OLEMSGICON_WARNING, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            }
            else if (!string.Equals(_lastAutomaticFailure, message, StringComparison.Ordinal))
            {
                // Background runs must not pop dialogs; surface each distinct failure once in the status bar.
                _lastAutomaticFailure = message;
                OutputLog.SetStatus("SQLFluff: " + FirstLine(message));
            }
        }

        private static string FirstLine(string message)
        {
            int index = message.IndexOfAny(new[] { '\r', '\n' });
            return index < 0 ? message : message.Substring(0, index);
        }

        // Replaces ConfigFile with whatever SqlFluffConfigResolver decides should actually be
        // used: a .sqlfluff discovered near the document (if it exists on disk) or the open
        // folder (covers an unsaved new document), falling back to the Options-configured path.
        // Resolving it here, once, keeps SqlFluffRunner itself free of any VS SDK dependency.
        // Internal (not private): FolderBatchService needs the same resolution for closed files
        // it reads straight off disk, without an ITextBuffer to route through this class.
        internal SqlFluffSettings ResolveEffectiveSettings(SqlFluffSettings settings, string path)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            string openFolderPath = _editor.GetOpenFolderPath();
            string resolvedConfigFile = SqlFluffConfigResolver.Resolve(path, openFolderPath, settings.ConfigFile);

            if (string.Equals(resolvedConfigFile, settings.ConfigFile, StringComparison.Ordinal))
            {
                return settings;
            }

            SqlFluffSettings clone = settings.Clone();
            clone.ConfigFile = resolvedConfigFile;
            return clone;
        }

        private static string DominantNewline(ITextSnapshot snapshot)
        {
            foreach (ITextSnapshotLine line in snapshot.Lines)
            {
                if (line.LineBreakLength > 0)
                {
                    return line.GetLineBreakText();
                }
            }

            return "\r\n";
        }

        private static bool EndsWithNewline(string text)
        {
            return text.Length > 0 && (text[text.Length - 1] == '\n' || text[text.Length - 1] == '\r');
        }

        // Keeps only the diff hunk (see Core/LineDiff.cs) touching `targetLine1Based`, reverting
        // every other hunk back to `original` — this is what makes FixRuleAsync a best-effort
        // single-violation fix instead of "every occurrence of this rule".
        private static string SelectHunkForLine(string original, string rewritten, string newline, int targetLine1Based)
        {
            string[] originalLines = original.Split(new[] { newline }, StringSplitOptions.None);
            string[] rewrittenLines = rewritten.Split(new[] { newline }, StringSplitOptions.None);

            IReadOnlyList<LineHunk> hunks = LineDiff.ComputeHunks(originalLines, rewrittenLines);
            IReadOnlyList<string> merged = LineDiff.ApplySelectedHunks(
                originalLines, hunks, h => LineDiff.HunkTouchesLine(h, targetLine1Based));

            return string.Join(newline, merged);
        }

        // Replace only the differing middle so the caret, scroll position and undo stack stay sensible.
        private static void ApplyMinimalEdit(ITextBuffer buffer, int baseOffset, string original, string fixedText)
        {
            int prefix = 0;
            int max = Math.Min(original.Length, fixedText.Length);
            while (prefix < max && original[prefix] == fixedText[prefix])
            {
                prefix++;
            }

            int suffix = 0;
            while (suffix < original.Length - prefix &&
                   suffix < fixedText.Length - prefix &&
                   original[original.Length - 1 - suffix] == fixedText[fixedText.Length - 1 - suffix])
            {
                suffix++;
            }

            int removeLength = original.Length - prefix - suffix;
            string insert = fixedText.Substring(prefix, fixedText.Length - prefix - suffix);

            using (ITextEdit edit = buffer.CreateEdit())
            {
                if (!edit.Replace(baseOffset + prefix, removeLength, insert))
                {
                    edit.Cancel();
                    return;
                }

                edit.Apply();
            }
        }
    }
}
