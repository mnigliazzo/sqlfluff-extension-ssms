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

        public bool TryGetBufferFromDocCookie(IVsRunningDocumentTable rdt, uint cookie, out ITextBuffer buffer, out string path)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            buffer = null;
            path = null;

            IntPtr docDataPtr = IntPtr.Zero;
            try
            {
                int hr = rdt.GetDocumentInfo(
                    cookie, out _, out _, out _, out string moniker, out _, out _, out docDataPtr);
                if (ErrorHandler.Failed(hr) || docDataPtr == IntPtr.Zero)
                {
                    return false;
                }

                object docData = Marshal.GetObjectForIUnknown(docDataPtr);
                IVsTextBuffer vsBuffer = docData as IVsTextBuffer;
                if (vsBuffer == null && docData is IVsTextBufferProvider provider &&
                    ErrorHandler.Succeeded(provider.GetTextBuffer(out IVsTextLines lines)))
                {
                    vsBuffer = lines;
                }

                if (vsBuffer == null)
                {
                    return false;
                }

                ITextBuffer documentBuffer = _adapters.GetDocumentBuffer(vsBuffer);
                if (documentBuffer == null)
                {
                    return false;
                }

                string filePath = GetPath(documentBuffer) ?? moniker;
                if (!IsSql(documentBuffer, filePath))
                {
                    return false;
                }

                buffer = documentBuffer;
                path = filePath;
                return true;
            }
            finally
            {
                if (docDataPtr != IntPtr.Zero)
                {
                    Marshal.Release(docDataPtr);
                }
            }
        }

        public string GetPath(ITextBuffer buffer)
        {
            return _documents.TryGetTextDocument(buffer, out ITextDocument document) ? document.FilePath : null;
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
