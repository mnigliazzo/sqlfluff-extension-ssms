using System;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace SqlFluff.Ssms.Services
{
    internal static class OutputLog
    {
        private static readonly Guid PaneGuid = new Guid("5b9d2e7f-4c18-4a63-8e0b-1f7a3d6c9e42");
        private static IVsOutputWindowPane _pane;

        public static void Write(string message)
        {
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                IVsOutputWindowPane pane = GetPane();
                pane?.OutputStringThreadSafe("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message + Environment.NewLine);
            });
        }

        // Brings the Output window's SQLFluff pane to the front (and shows the Output window
        // itself if it was closed) - used after writing something the user explicitly asked to
        // see (e.g. SQLFluff Documentation's `sqlfluff --help` dump), as opposed to Write's normal
        // silent logging that a user has to go looking for.
        public static void Show()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            GetPane()?.Activate();
        }

        public static void SetStatus(string text)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (Package.GetGlobalService(typeof(SVsStatusbar)) is IVsStatusbar statusBar)
            {
                statusBar.SetText(text);
            }
        }

        private static IVsOutputWindowPane GetPane()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_pane != null)
            {
                return _pane;
            }

            if (Package.GetGlobalService(typeof(SVsOutputWindow)) is IVsOutputWindow window)
            {
                Guid guid = PaneGuid;
                window.CreatePane(ref guid, "SQLFluff", 1, 1);
                window.GetPane(ref guid, out _pane);
            }

            return _pane;
        }
    }
}
