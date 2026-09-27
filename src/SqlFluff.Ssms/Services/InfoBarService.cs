using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace SqlFluff.Ssms.Services
{
    // Non-blocking notifications in the main window's info bar - the Visual Studio pattern for
    // things the user didn't ask for (e.g. startup checks), instead of a modal message box that
    // interrupts whatever they're doing. Modal confirmations remain for user-invoked commands.
    internal sealed class InfoBarService
    {
        private readonly AsyncPackage _package;

        public InfoBarService(AsyncPackage package)
        {
            _package = package;
        }

        // onDismissed runs when the bar is closed without any action being clicked (the X).
        public async Task ShowAsync(string message, IReadOnlyList<(string Text, Action OnClick)> actions, Action onDismissed = null)
        {
            await _package.JoinableTaskFactory.SwitchToMainThreadAsync();

            var shell = await _package.GetServiceAsync(typeof(SVsShell)) as IVsShell;
            var factory = await _package.GetServiceAsync(typeof(SVsInfoBarUIFactory)) as IVsInfoBarUIFactory;
            await _package.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (shell == null || factory == null ||
                Microsoft.VisualStudio.ErrorHandler.Failed(shell.GetProperty((int)__VSSPROPID7.VSSPROPID_MainWindowInfoBarHost, out object hostObject)) ||
                !(hostObject is IVsInfoBarHost host))
            {
                OutputLog.Write(message);
                return;
            }

            var model = new InfoBarModel(
                new[] { new InfoBarTextSpan("SQLFluff: " + message) },
                actions.Select(action => (IVsInfoBarActionItem)new InfoBarHyperlink(action.Text, action.OnClick)).ToArray(),
                KnownMonikers.StatusInformation,
                isCloseButtonVisible: true);

            IVsInfoBarUIElement element = factory.CreateInfoBar(model);
            var events = new Events(onDismissed);
            element.Advise(events, out uint cookie);
            events.Cookie = cookie;
            host.AddInfoBar(element);
        }

        private sealed class Events : IVsInfoBarUIEvents
        {
            private readonly Action _onDismissed;
            private bool _actionClicked;

            public Events(Action onDismissed)
            {
                _onDismissed = onDismissed;
            }

            public uint Cookie { get; set; }

            public void OnActionItemClicked(IVsInfoBarUIElement infoBarUIElement, IVsInfoBarActionItem actionItem)
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                _actionClicked = true;
                (actionItem.ActionContext as Action)?.Invoke();
                infoBarUIElement.Close();
            }

            public void OnClosed(IVsInfoBarUIElement infoBarUIElement)
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                infoBarUIElement.Unadvise(Cookie);
                if (!_actionClicked)
                {
                    _onDismissed?.Invoke();
                }
            }
        }
    }
}
