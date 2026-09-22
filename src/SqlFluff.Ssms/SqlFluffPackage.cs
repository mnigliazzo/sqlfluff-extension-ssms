using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using SqlFluff.Ssms.Core;
using SqlFluff.Ssms.Options;
using SqlFluff.Ssms.Services;
using Task = System.Threading.Tasks.Task;

namespace SqlFluff.Ssms
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration("SQLFluff for SSMS", "SQLFluff linter and formatter integration.", "1.0.0")]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideOptionPage(typeof(SqlFluffOptionsPage), "SQLFluff", "General", 0, 0, true)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.ShellInitialized_string, PackageAutoLoadFlags.BackgroundLoad)]
    [Guid(PackageGuids.PackageString)]
    public sealed class SqlFluffPackage : AsyncPackage
    {
        private ErrorListService _errors;
        private LintService _lint;
        private EditorServices _editor;
        private FolderBatchService _folderBatch;
        private DocumentEvents _documentEvents;
        private IVsRunningDocumentTable _rdt;
        private uint _rdtCookie;
        private CancellationTokenSource _folderBatchCts;

        // Used by MEF-composed editor components (e.g. the Light Bulb suggested-actions source) that
        // have no other way to reach this package's services.
        internal static SqlFluffPackage Instance { get; private set; }

        internal LintService LintService => _lint;
        internal EditorServices EditorServices => _editor;

        internal SqlFluffSettings GetSettings()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return ((SqlFluffOptionsPage)GetDialogPage(typeof(SqlFluffOptionsPage))).ToSettings();
        }

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            Instance = this;

            var componentModel = (IComponentModel)await GetServiceAsync(typeof(SComponentModel));
            _editor = new EditorServices(
                this,
                componentModel.GetService<IVsEditorAdaptersFactoryService>(),
                componentModel.GetService<ITextDocumentFactoryService>());

            _errors = new ErrorListService(this);
            _lint = new LintService(this, _editor, _errors);

            _rdt = (IVsRunningDocumentTable)await GetServiceAsync(typeof(SVsRunningDocumentTable));
            _folderBatch = new FolderBatchService(this, _rdt, _editor, _lint, _errors);

            var commands = await GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            if (commands != null)
            {
                AddCommand(commands, PackageIds.CmdLint, (s, e) => RunOnActiveDocument(EditorAction.Lint), requiresSql: true);
                AddCommand(commands, PackageIds.CmdFix, (s, e) => RunOnActiveDocument(EditorAction.Fix), requiresSql: true);
                AddCommand(commands, PackageIds.CmdFormat, (s, e) => RunOnActiveDocument(EditorAction.Format), requiresSql: true);
                AddCommand(commands, PackageIds.CmdClear, (s, e) => ClearActiveDocument(), requiresSql: true);
                AddCommand(commands, PackageIds.CmdOptions, (s, e) => ShowOptionPage(typeof(SqlFluffOptionsPage)), requiresSql: false);
                AddFolderCommand(commands, PackageIds.CmdLintFolder, FolderBatchAction.Lint);
                AddFolderCommand(commands, PackageIds.CmdFixFolder, FolderBatchAction.Fix);
                AddFolderCommand(commands, PackageIds.CmdFormatFolder, FolderBatchAction.Format);
            }

            _documentEvents = new DocumentEvents(this, _rdt, _editor, _lint);
            _rdt.AdviseRunningDocTableEvents(_documentEvents, out _rdtCookie);
            _documentEvents.AttachToOpenDocuments();

            // Also visible any time via Tools > Options > SQLFluff > Extension version.
            OutputLog.Write("SQLFluff for SSMS v" + GetType().Assembly.GetName().Version.ToString(3) + " loaded.");

            // Non-blocking: warn once if sqlfluff isn't reachable, instead of waiting for the first Lint/Fix to fail.
            JoinableTaskFactory.RunAsync(CheckSqlFluffAvailabilityAsync).Task.FileAndForget("sqlfluff/availability-check");
        }

        private async Task CheckSqlFluffAvailabilityAsync()
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync();
            SqlFluffSettings settings = GetSettings();

            string error = await Task.Run(() => SqlFluffRunner.CheckAvailabilityAsync(settings, CancellationToken.None));
            if (error == null)
            {
                return;
            }

            await JoinableTaskFactory.SwitchToMainThreadAsync();
            OutputLog.Write(error);
            OutputLog.SetStatus("SQLFluff: not found. Install with 'pip install sqlfluff' or set its path in Tools > Options > SQLFluff.");
        }

        private void AddCommand(OleMenuCommandService service, int id, EventHandler handler, bool requiresSql)
        {
            var command = new OleMenuCommand(handler, new CommandID(PackageGuids.CmdSet, id));
            if (requiresSql)
            {
                command.BeforeQueryStatus += (sender, args) =>
                {
                    ThreadHelper.ThrowIfNotOnUIThread();
                    ((OleMenuCommand)sender).Enabled = _editor.TryGetActiveSqlView(out _, out _, out _);
                };
            }

            service.AddCommand(command);
        }

        private void AddFolderCommand(OleMenuCommandService service, int id, FolderBatchAction action)
        {
            var command = new OleMenuCommand((s, e) => RunFolderAction(action), new CommandID(PackageGuids.CmdSet, id));
            command.BeforeQueryStatus += (sender, args) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                ((OleMenuCommand)sender).Enabled = !string.IsNullOrEmpty(_editor.GetOpenFolderPath());
            };

            service.AddCommand(command);
        }

        private enum EditorAction
        {
            Lint,
            Fix,
            Format,
        }

        private void RunOnActiveDocument(EditorAction action)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!_editor.TryGetActiveSqlView(out IWpfTextView view, out ITextBuffer buffer, out string path))
            {
                OutputLog.SetStatus("SQLFluff: open a SQL query window first.");
                return;
            }

            Func<Task> operation;
            switch (action)
            {
                case EditorAction.Fix:
                    operation = () => _lint.FixAsync(view, buffer, path);
                    break;
                case EditorAction.Format:
                    operation = () => _lint.FormatAsync(view, buffer, path);
                    break;
                default:
                    operation = () => _lint.LintAsync(view, buffer, path, userInitiated: true);
                    break;
            }

            JoinableTaskFactory.RunAsync(operation).Task.FileAndForget("sqlfluff/" + action.ToString().ToLowerInvariant());
        }

        private void ClearActiveDocument()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_editor.TryGetActiveSqlView(out _, out ITextBuffer buffer, out string path))
            {
                _lint.Clear(buffer, path);
                OutputLog.SetStatus("SQLFluff: diagnostics cleared.");
            }
        }

        // Re-running a folder command while one is already in flight cancels it instead of
        // starting a second one — the simplest way to offer a "Cancel" without a dedicated dialog.
        private void RunFolderAction(FolderBatchAction action)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_folderBatchCts != null)
            {
                OutputLog.SetStatus("SQLFluff: cancelling...");
                _folderBatchCts.Cancel();
                return;
            }

            string folder = _editor.GetOpenFolderPath();
            if (string.IsNullOrEmpty(folder))
            {
                OutputLog.SetStatus("SQLFluff: open a folder first.");
                return;
            }

            JoinableTaskFactory
                .RunAsync(() => RunFolderActionAsync(folder, action))
                .Task.FileAndForget("sqlfluff/folder-" + action.ToString().ToLowerInvariant());
        }

        private async Task RunFolderActionAsync(string folder, FolderBatchAction action)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync();
            OutputLog.SetStatus("SQLFluff: scanning folder...");

            IReadOnlyList<string> files = await Task.Run(() => FolderBatchService.EnumerateSqlFiles(folder));
            await JoinableTaskFactory.SwitchToMainThreadAsync();

            if (files.Count == 0)
            {
                OutputLog.SetStatus("SQLFluff: no .sql files found under " + folder + ".");
                return;
            }

            if (action != FolderBatchAction.Lint && !ConfirmFolderRewrite(folder, files, action))
            {
                OutputLog.SetStatus("SQLFluff: cancelled.");
                return;
            }

            string verb = action == FolderBatchAction.Lint ? "linting" : action == FolderBatchAction.Fix ? "fixing" : "formatting";
            OutputLog.SetStatus("SQLFluff: " + verb + " " + files.Count + " file(s)... (run the command again to cancel)");

            var cts = new CancellationTokenSource();
            _folderBatchCts = cts;
            var progress = new Progress<FolderBatchProgress>(p =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                OutputLog.SetStatus("SQLFluff: " + verb + " " + (p.Completed + 1) + "/" + p.Total + " - " + p.RelativePath);
            });

            FolderBatchSummary summary;
            try
            {
                summary = await _folderBatch.RunAsync(folder, files, action, progress, cts.Token);
            }
            finally
            {
                _folderBatchCts = null;
            }

            await JoinableTaskFactory.SwitchToMainThreadAsync();
            OutputLog.Write(BuildSummaryLog(folder, action, summary));
            OutputLog.SetStatus((summary.WasCancelled ? "SQLFluff: cancelled. " : string.Empty) + BuildSummaryStatus(action, summary));

            if (action == FolderBatchAction.Lint && summary.TotalViolations > 0)
            {
                _errors.Show();
            }
        }

        private bool ConfirmFolderRewrite(string folder, IReadOnlyList<string> files, FolderBatchAction action)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            string verb = action == FolderBatchAction.Fix ? "Fix" : "Format";
            const int previewCount = 15;

            var sb = new StringBuilder();
            sb.Append(verb).Append(" will rewrite up to ").Append(files.Count)
              .Append(" .sql file(s) on disk under:\n").Append(folder).Append("\n\n");

            foreach (string file in files.Take(previewCount))
            {
                sb.Append("  ").Append(FolderBatchService.MakeRelativePath(folder, file)).Append('\n');
            }

            if (files.Count > previewCount)
            {
                sb.Append("  ...and ").Append(files.Count - previewCount).Append(" more\n");
            }

            sb.Append("\nFiles open in the editor with unsaved changes will be skipped. Continue?");

            int result = VsShellUtilities.ShowMessageBox(
                this, sb.ToString(), "SQLFluff: " + verb + " All Files in Folder",
                OLEMSGICON.OLEMSGICON_QUERY, OLEMSGBUTTON.OLEMSGBUTTON_YESNO, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_SECOND);

            return result == (int)VSConstants.MessageBoxResult.IDYES;
        }

        private static string BuildSummaryStatus(FolderBatchAction action, FolderBatchSummary summary)
        {
            if (action == FolderBatchAction.Lint)
            {
                return "SQLFluff: linted " + summary.TotalFiles + " file(s), " + summary.TotalViolations +
                       " issue(s) found. See the SQLFluff output pane and Error List.";
            }

            return "SQLFluff: " + summary.Rewritten + " fixed, " + summary.Unchanged + " unchanged, " +
                   summary.Skipped + " skipped, " + summary.Failed + " failed. See the SQLFluff output pane.";
        }

        private static string BuildSummaryLog(string folder, FolderBatchAction action, FolderBatchSummary summary)
        {
            var sb = new StringBuilder();
            sb.Append("Folder ").Append(action.ToString().ToLowerInvariant()).Append(" under ").Append(folder).Append(':').Append(Environment.NewLine);
            foreach (string line in summary.Details)
            {
                sb.Append("  ").Append(line).Append(Environment.NewLine);
            }

            return sb.ToString();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                if (_rdt != null && _rdtCookie != 0)
                {
                    _rdt.UnadviseRunningDocTableEvents(_rdtCookie);
                    _rdtCookie = 0;
                }

                _errors?.Dispose();
                _errors = null;

                if (Instance == this)
                {
                    Instance = null;
                }
            }

            base.Dispose(disposing);
        }
    }
}
