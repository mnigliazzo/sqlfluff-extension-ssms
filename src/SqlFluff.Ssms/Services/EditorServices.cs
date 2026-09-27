using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;

namespace SqlFluff.Ssms.Services
{
    internal sealed class EditorServices
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IVsEditorAdaptersFactoryService _adapters;
        private readonly ITextDocumentFactoryService _documents;

        public EditorServices(
            IServiceProvider serviceProvider,
            IVsEditorAdaptersFactoryService adapters,
            ITextDocumentFactoryService documents)
        {
            _serviceProvider = serviceProvider;
            _adapters = adapters;
            _documents = documents;
        }

        public bool TryGetActiveSqlView(out IWpfTextView view, out ITextBuffer buffer, out string path)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            view = null;
            buffer = null;
            path = null;

            IWpfTextView candidate = GetViewFromActiveDocumentFrame() ?? GetViewFromTextManager();
            if (candidate == null)
            {
                return false;
            }

            ITextBuffer documentBuffer = candidate.TextDataModel.DocumentBuffer;
            string filePath = GetPath(documentBuffer);
            if (!IsSql(documentBuffer, filePath))
            {
                return false;
            }

            view = candidate;
            buffer = documentBuffer;
            path = filePath;
            return true;
        }

        public bool TryGetBufferFromDocCookie(RunningDocumentTable rdt, uint cookie, out ITextBuffer buffer, out string path)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            buffer = null;
            path = null;

            RunningDocumentInfo info;
            try
            {
                info = rdt.GetDocumentInfo(cookie);
            }
            catch (COMException)
            {
                // The cookie is no longer in the table (e.g. the document is mid-teardown).
                return false;
            }

            ITextBuffer documentBuffer = BufferFromDocData(info.DocData);
            if (documentBuffer == null)
            {
                return false;
            }

            string filePath = GetPath(documentBuffer) ?? info.Moniker;
            if (!IsSql(documentBuffer, filePath))
            {
                return false;
            }

            buffer = documentBuffer;
            path = filePath;
            return true;
        }

        // Looks an already-open document up by path instead of by RDT cookie — used by the
        // folder-wide batch commands, which start from a file path on disk, not a live document
        // event. isDirty lets the caller skip files with unsaved changes rather than silently
        // overwrite them on disk out from under the open buffer.
        public bool TryGetOpenBuffer(RunningDocumentTable rdt, string path, out ITextBuffer buffer, out bool isDirty)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            buffer = null;
            isDirty = false;

            ITextBuffer documentBuffer = BufferFromDocData(rdt.FindDocument(path));
            if (documentBuffer == null)
            {
                return false;
            }

            buffer = documentBuffer;
            isDirty = _documents.TryGetTextDocument(documentBuffer, out ITextDocument document) && document.IsDirty;
            return true;
        }

        private ITextBuffer BufferFromDocData(object docData)
        {
            if (docData == null)
            {
                return null;
            }

            IVsTextBuffer vsBuffer = docData as IVsTextBuffer;
            if (vsBuffer == null && docData is IVsTextBufferProvider provider &&
                ErrorHandler.Succeeded(provider.GetTextBuffer(out IVsTextLines lines)))
            {
                vsBuffer = lines;
            }

            return vsBuffer == null ? null : _adapters.GetDocumentBuffer(vsBuffer);
        }

        public string GetPath(ITextBuffer buffer)
        {
            return _documents.TryGetTextDocument(buffer, out ITextDocument document) ? document.FilePath : null;
        }

        // Root of the currently open folder or solution, if any — including an "Open Folder"
        // workspace, which the shell represents as a solution directory too. Used to find a
        // project's .sqlfluff for documents with no on-disk path of their own to walk up from
        // (an unsaved new document).
        public string GetOpenFolderPath()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_serviceProvider.GetService(typeof(SVsSolution)) is IVsSolution solution &&
                ErrorHandler.Succeeded(solution.GetSolutionInfo(out string solutionDirectory, out _, out _)) &&
                !string.IsNullOrEmpty(solutionDirectory))
            {
                return solutionDirectory;
            }

            return null;
        }

        // path is accepted for backward compatibility with existing call sites, which already
        // resolve it; the actual check also has its own fallback via _documents.
        private bool IsSql(ITextBuffer buffer, string path)
        {
            if (!string.IsNullOrEmpty(path) &&
                string.Equals(SafeExtension(path), ".sql", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return Editor.SqlBufferHeuristics.IsLikelySql(buffer, _documents);
        }

        private static string SafeExtension(string path)
        {
            try
            {
                return Path.GetExtension(path);
            }
            catch (ArgumentException)
            {
                return string.Empty;
            }
        }

        private IWpfTextView GetViewFromActiveDocumentFrame()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var selection = _serviceProvider.GetService(typeof(SVsShellMonitorSelection)) as IVsMonitorSelection;
            if (selection == null ||
                ErrorHandler.Failed(selection.GetCurrentElementValue((uint)VSConstants.VSSELELEMID.SEID_DocumentFrame, out object value)))
            {
                return null;
            }

            IVsTextView textView = value is IVsWindowFrame frame ? VsShellUtilities.GetTextView(frame) : null;
            return textView == null ? null : _adapters.GetWpfTextView(textView);
        }

        private IWpfTextView GetViewFromTextManager()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var textManager = _serviceProvider.GetService(typeof(SVsTextManager)) as IVsTextManager;
            if (textManager == null)
            {
                return null;
            }

            textManager.GetActiveView(1, null, out IVsTextView textView);
            if (textView == null)
            {
                textManager.GetActiveView(0, null, out textView);
            }

            return textView == null ? null : _adapters.GetWpfTextView(textView);
        }
    }
}
