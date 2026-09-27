using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using SqlFluff.Ssms.Core;

namespace SqlFluff.Ssms.Services
{
    internal sealed class DocumentEvents : IVsRunningDocTableEvents3
    {
        private readonly SqlFluffPackage _package;
        private readonly RunningDocumentTable _rdt;
        private readonly EditorServices _editor;
        private readonly LintService _lint;
        private readonly ConditionalWeakTable<ITextBuffer, object> _tracked = new ConditionalWeakTable<ITextBuffer, object>();

        // Buffer + path per docCookie, captured whenever we successfully resolve them
        // (attach/save) rather than re-resolved at close time. By OnBeforeLastDocumentUnlock, an
        // unsaved-and-discarded document's IVsTextBuffer/adapter mapping can already be torn
        // down, so TryGetBufferFromDocCookie fails there even though it worked moments earlier.
        // The cached buffer reference matters, not just the path: without it, a lint that was
        // already in flight (lint-while-typing) when the tab closed keeps running and republishes
        // the same violations into the Error List afterwards - the warning briefly disappears
        // then "comes back". Passing the same buffer instance into LintService.Clear lets it find
        // and cancel that pending lint/debounce via its own per-buffer state.
        private readonly Dictionary<uint, (ITextBuffer Buffer, string Path)> _cookieInfo = new Dictionary<uint, (ITextBuffer Buffer, string Path)>();

        public DocumentEvents(SqlFluffPackage package, RunningDocumentTable rdt, EditorServices editor, LintService lint)
        {
            _package = package;
            _rdt = rdt;
            _editor = editor;
            _lint = lint;
        }

        public void AttachToOpenDocuments()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            foreach (uint cookie in _rdt.Select(document => document.DocCookie).ToList())
            {
                Attach(cookie, isBulkAttach: true);
            }
        }

        public int OnBeforeDocumentWindowShow(uint docCookie, int fFirstShow, IVsWindowFrame pFrame)
        {
            if (fFirstShow != 0)
            {
                Attach(docCookie, isBulkAttach: false);
            }

            return VSConstants.S_OK;
        }

        public int OnAfterSave(uint docCookie)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_editor.TryGetBufferFromDocCookie(_rdt, docCookie, out ITextBuffer buffer, out string path))
            {
                // Keep the cache in sync with e.g. a Save As on a previously-unsaved document,
                // so a later close looks up the saved path rather than a stale moniker.
                _cookieInfo[docCookie] = (buffer, path);

                if (_package.GetSettings().LintOnSave)
                {
                    RunLint(buffer, path);
                }
            }

            return VSConstants.S_OK;
        }

        public int OnBeforeLastDocumentUnlock(uint docCookie, uint dwRDTLockType, uint dwReadLocksRemaining, uint dwEditLocksRemaining)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (dwReadLocksRemaining == 0 && dwEditLocksRemaining == 0)
            {
                ITextBuffer buffer;
                string path;
                if (_editor.TryGetBufferFromDocCookie(_rdt, docCookie, out ITextBuffer resolvedBuffer, out string resolvedPath))
                {
                    buffer = resolvedBuffer;
                    path = resolvedPath;
                }
                else if (_cookieInfo.TryGetValue(docCookie, out (ITextBuffer Buffer, string Path) cached))
                {
                    buffer = cached.Buffer;
                    path = cached.Path;
                }
                else
                {
                    buffer = null;
                    path = null;
                }

                // Clear (cancel pending lints + drop this buffer's diagnostics), but don't
                // mark the buffer closed: SSMS's lock counts can hit 0 while the tab stays open, and
                // a closed mark from here would then mute its linting. The editor-view listener
                // (SqlFluffViewLifetimeListener) is what marks a buffer closed.
                if (buffer != null)
                {
                    _lint.Clear(buffer);
                }
                else if (path != null)
                {
                    _lint.DocumentClosed(path);
                }

                _cookieInfo.Remove(docCookie);
            }

            return VSConstants.S_OK;
        }

        private void Attach(uint docCookie, bool isBulkAttach)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!_editor.TryGetBufferFromDocCookie(_rdt, docCookie, out ITextBuffer buffer, out string path))
            {
                return;
            }

            _cookieInfo[docCookie] = (buffer, path);

            bool firstTime = false;
            _tracked.GetValue(buffer, b =>
            {
                firstTime = true;
                b.PostChanged += OnBufferChanged;
                return new object();
            });

            if (!firstTime)
            {
                return;
            }

            SqlFluffSettings settings = _package.GetSettings();

            // Format on open rewrites the buffer, so it's restricted to documents the user actually
            // opens after the package has loaded (isBulkAttach == false) - not to every SQL tab that
            // was already open when SSMS/the package started (AttachToOpenDocuments' bulk walk of
            // the RDT). Without this, enabling it would silently reformat every restored tab at once
            // on the next SSMS startup, which is a much bigger surprise than "format this one file
            // I just opened" - the behavior its Options description promises.
            if (settings.FormatOnOpen && !isBulkAttach)
            {
                ThreadHelper.JoinableTaskFactory
                    .RunAsync(() => _lint.FormatOnOpenAsync(buffer, path))
                    .Task.FileAndForget("sqlfluff/format-on-open");
                return;
            }

            // Re-added after being removed in #19/#26 for being unreliable in SSMS's query window
            // lifecycle - it reuses this same Attach() hook, which is already relied on today to
            // wire up "Lint while typing" on first show, so it's at least as reliable as that. Gated
            // by its own setting (default on) so it can be turned off if it still misbehaves for
            // some document lifecycle SSMS 22 exposes that this doesn't account for. Unlike Format
            // on open, this runs for bulk-attached documents too (including on package load) since
            // it only reports diagnostics rather than rewriting anything.
            if (settings.LintOnOpen)
            {
                RunLint(buffer, path);
            }
        }

        private void OnBufferChanged(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var buffer = (ITextBuffer)sender;
            SqlFluffSettings settings = _package.GetSettings();
            if (settings.LintOnType)
            {
                _lint.Schedule(buffer, _editor.GetPath(buffer), settings.TypeDelayMs);
            }
        }

        private void RunLint(ITextBuffer buffer, string path)
        {
            ThreadHelper.JoinableTaskFactory
                .RunAsync(() => _lint.LintAsync(buffer, path, userInitiated: false))
                .Task.FileAndForget("sqlfluff/lint");
        }

        public int OnBeforeSave(uint docCookie)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            SqlFluffSettings settings = _package.GetSettings();

            // Fix on save takes priority over Format on save when both are on - Fix already covers
            // everything Format's safe subset would do, so running both would just re-run SQLFluff
            // a second time for no effect.
            bool runFix = settings.FixOnSave;
            bool runFormat = !runFix && settings.FormatOnSave;

            if ((runFix || runFormat) &&
                _editor.TryGetBufferFromDocCookie(_rdt, docCookie, out ITextBuffer buffer, out string path))
            {
                try
                {
                    // OnBeforeSave is a synchronous COM callback: the save proceeds as soon as this
                    // returns, so fixing/formatting has to complete first. JoinableTaskFactory.Run
                    // pumps the UI thread's message queue while it waits, which is what avoids
                    // deadlocking here (a plain .Result/.Wait() on an awaiter that needs the UI
                    // thread would hang).
                    ThreadHelper.JoinableTaskFactory.Run(() => runFix
                        ? _lint.FixForSaveAsync(buffer, path)
                        : _lint.FormatForSaveAsync(buffer, path));
                }
                catch (Exception ex)
                {
                    // Never let a fix/format failure block or corrupt the save.
                    OutputLog.Write((runFix ? "Fix" : "Format") + " on save failed: " + ex.Message);
                }
            }

            return VSConstants.S_OK;
        }

        public int OnAfterFirstDocumentLock(uint docCookie, uint dwRDTLockType, uint dwReadLocksRemaining, uint dwEditLocksRemaining) => VSConstants.S_OK;
        public int OnAfterAttributeChange(uint docCookie, uint grfAttribs) => VSConstants.S_OK;
        public int OnAfterAttributeChangeEx(uint docCookie, uint grfAttribs, IVsHierarchy pHierOld, uint itemidOld, string pszMkDocumentOld, IVsHierarchy pHierNew, uint itemidNew, string pszMkDocumentNew) => VSConstants.S_OK;
        public int OnAfterDocumentWindowHide(uint docCookie, IVsWindowFrame pFrame) => VSConstants.S_OK;
    }
}
