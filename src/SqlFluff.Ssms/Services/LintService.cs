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

        public async Task LintAsync(ITextBuffer buffer, string path, bool userInitiated)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            BufferState state = _state.GetOrCreateValue(buffer);
            state.Debounce?.Cancel();
            state.Lint?.Cancel();
            var cts = new CancellationTokenSource();
            state.Lint = cts;

            SqlFluffSettings settings = _package.GetSettings();
            ITextSnapshot snapshot = buffer.CurrentSnapshot;
            string text = snapshot.GetText();

            if (string.IsNullOrWhiteSpace(text))
            {
                ClearDiagnostics(buffer, path);
                return;
            }

            if (userInitiated)
            {
                OutputLog.SetStatus("SQLFluff: linting...");
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

        public Task FixAsync(IWpfTextView view, ITextBuffer buffer, string path)
        {
            return RewriteAsync(view, buffer, path, RewriteMode.Fix);
        }

        public Task FormatAsync(IWpfTextView view, ITextBuffer buffer, string path)
        {
            return RewriteAsync(view, buffer, path, RewriteMode.Format);
        }

        private enum RewriteMode
        {
            Fix,
            Format,
        }

        private async Task RewriteAsync(IWpfTextView view, ITextBuffer buffer, string path, RewriteMode mode)
        {
            string verb = mode == RewriteMode.Format ? "format" : "fix";
            string verbCapitalized = mode == RewriteMode.Format ? "Format" : "Fix";

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (!buffer.CheckEditAccess())
            {
                ReportFailure("The document is read-only.", userInitiated: true);
                return;
            }

            SqlFluffSettings settings = _package.GetSettings();
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
