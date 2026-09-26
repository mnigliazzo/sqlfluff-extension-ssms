using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.CommandBars;
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
                AddCommand(commands, PackageIds.CmdCheckForUpdates, (s, e) => CheckForUpdates(userInitiated: true), requiresSql: false);
                AddCommand(commands, PackageIds.CmdInstallSqlFluffTool, (s, e) => CheckSqlFluffTool(userInitiated: true), requiresSql: false);
                AddFolderCommand(commands, PackageIds.CmdLintFolder, FolderBatchAction.Lint);
                AddFolderCommand(commands, PackageIds.CmdFixFolder, FolderBatchAction.Fix);
                AddFolderCommand(commands, PackageIds.CmdFormatFolder, FolderBatchAction.Format);
            }

            _documentEvents = new DocumentEvents(this, _rdt, _editor, _lint);
            _rdt.AdviseRunningDocTableEvents(_documentEvents, out _rdtCookie);
            _documentEvents.AttachToOpenDocuments();

            EnsureToolbarVisibleOnce();

            // Also visible any time via Tools > Options > SQLFluff > Extension version.
            OutputLog.Write("SQLFluff for SSMS v" + GetType().Assembly.GetName().Version.ToString(3) + " loaded.");

            // Non-blocking. Sequenced rather than two independent fire-and-forget tasks: both may
            // call OutputLog.SetStatus, and running them concurrently would let whichever finishes
            // last silently clobber the other's status bar text. The extension's own update notice
            // (silent unless CheckForUpdatesOnStartup finds something, and never prompts on its
            // own) goes first, since the sqlfluff tool check that follows may pop a blocking
            // install/upgrade prompt - the more actionable of the two, since nothing lints at all
            // until sqlfluff itself is reachable.
            JoinableTaskFactory.RunAsync(async () =>
            {
                SqlFluffSettings startupSettings = GetSettings();
                if (startupSettings.CheckForUpdatesOnStartup)
                {
                    await CheckForUpdatesAsync(userInitiated: false);
                }

                if (startupSettings.CheckSqlFluffToolOnStartup)
                {
                    await CheckSqlFluffAvailabilityAsync(userInitiated: false);
                }
            }).Task.FileAndForget("sqlfluff/startup-checks");
        }

        // The toolbar's DefaultDocked CommandFlag in SqlFluffPackage.vsct is supposed to make VS show
        // it automatically the first time the package loads, but SSMS 22 doesn't reliably honor that
        // (issue #27 found the same kind of gap for the query editor's context menu - SSMS 22 doesn't
        // always behave like a plain VS shell for command UI). Force it visible via DTE.CommandBars
        // once, then leave the user's own show/hide choice alone from then on.
        private void EnsureToolbarVisibleOnce()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var page = (SqlFluffOptionsPage)GetDialogPage(typeof(SqlFluffOptionsPage));
            if (page.ToolbarShownOnce)
            {
                return;
            }

            // Broad catch is deliberate: this runs inline in InitializeAsync, so anything left
            // uncaught fails the whole package load (see DocumentEvents.OnBeforeSave for the same
            // must-not-fail reasoning). SSMS 22's DTE/CommandBars implementation is exactly the kind
            // of "doesn't always behave like a plain VS shell" surface where an unexpected exception
            // type is plausible, not just the handful of COM-ish ones.
            try
            {
                var dte = (EnvDTE.DTE)GetService(typeof(SDTE));
                var commandBars = (CommandBars)dte.CommandBars;
                CommandBar toolbar = commandBars["SQLFluff"];
                toolbar.Visible = true;
            }
            catch (Exception ex)
            {
                // Only mark it "handled" once the user has actually had a chance to see the
                // toolbar - if showing it failed, keep retrying on future startups rather than
                // silently giving up with nothing but a buried Output pane line.
                OutputLog.Write("SQLFluff: couldn't show the toolbar automatically - enable it manually via right-click on any toolbar > SQLFluff. (" + ex.Message + ")");
                return;
            }

            page.ToolbarShownOnce = true;
            page.SaveSettingsToStorage();
        }

        private void CheckSqlFluffTool(bool userInitiated)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            JoinableTaskFactory.RunAsync(() => CheckSqlFluffAvailabilityAsync(userInitiated)).Task.FileAndForget("sqlfluff/check-sqlfluff-tool");
        }

        // Guards the whole check-then-install/upgrade flow below against running twice at once -
        // e.g. the silent startup check still waiting on a slow PyPI round trip (or on the user
        // sitting on the install-prompt dialog) when the user separately invokes "Install/Update
        // SQLFluff Tool..." manually. Without this, both could reach RunPipInstallAsync and launch
        // pip concurrently against the same environment (see RunFolderAction's _folderBatchCts for
        // the same kind of guard around another external, mutating operation).
        private bool _sqlFluffToolCheckInFlight;

        // Checks that the sqlfluff *tool* (not the extension) is installed and reachable, and, if
        // it is, that it's not outdated per PyPI. The silent startup check (userInitiated: false)
        // still prompts to install/upgrade when something's actually missing or outdated - unlike
        // the extension's own update check, there's no useful "just note it and move on" for a
        // tool the extension can't function without at all (though it can be turned off entirely
        // via "Check SQLFluff tool on startup" in Options). The explicit "Install/Update SQLFluff
        // Tool..." command (userInitiated: true) additionally reports back when everything's
        // already fine, so the command doesn't look like it did nothing.
        private async Task CheckSqlFluffAvailabilityAsync(bool userInitiated)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync();
            if (_sqlFluffToolCheckInFlight)
            {
                if (userInitiated)
                {
                    OutputLog.SetStatus("SQLFluff: already checking/installing the sqlfluff tool - please wait for it to finish.");
                }

                return;
            }

            _sqlFluffToolCheckInFlight = true;
            try
            {
                SqlFluffSettings settings = GetSettings();
                if (userInitiated)
                {
                    OutputLog.SetStatus("SQLFluff: checking the sqlfluff tool...");
                }

                SqlFluffAvailability availability = await Task.Run(() => SqlFluffRunner.CheckAvailabilityAsync(settings, CancellationToken.None));

                await JoinableTaskFactory.SwitchToMainThreadAsync();
                if (!availability.IsAvailable)
                {
                    OutputLog.Write(availability.Error);

                    if (availability.IsConfiguredPathInvalid)
                    {
                        // Installing/upgrading via pip elsewhere can't fix this - Options'
                        // Executable path always wins once it's set to something non-default (see
                        // SqlFluffRunner.Resolve), so offering to run pip here would just leave the
                        // user stuck in a loop where it "succeeds" but SQLFluff still isn't reachable.
                        OutputLog.SetStatus("SQLFluff: the configured executable path in Tools > Options > SQLFluff is invalid - fix or clear it there.");
                        return;
                    }

                    OutputLog.SetStatus("SQLFluff: not found. Install with 'pip install sqlfluff' or set its path in Tools > Options > SQLFluff.");
                    await OfferInstallSqlFluffToolAsync();
                    return;
                }

                await CheckSqlFluffToolUpdateAsync(availability.Version, userInitiated);
            }
            finally
            {
                _sqlFluffToolCheckInFlight = false;
            }
        }

        private async Task OfferInstallSqlFluffToolAsync()
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync();
            int result = VsShellUtilities.ShowMessageBox(
                this,
                "SQLFluff (the Python linter/formatter this extension relies on) was not found.\n\n" +
                "Install it now via 'pip install sqlfluff'? This requires Python and pip to already be installed.",
                "SQLFluff for SSMS: SQLFluff Tool Not Found",
                OLEMSGICON.OLEMSGICON_QUERY, OLEMSGBUTTON.OLEMSGBUTTON_YESNO, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);

            if (result != (int)VSConstants.MessageBoxResult.IDYES)
            {
                OutputLog.SetStatus("SQLFluff: not installed. Run SQLFluff > Install/Update SQLFluff Tool... any time, or install manually with 'pip install sqlfluff'.");
                return;
            }

            await RunPipInstallAsync(upgrade: false);
        }

        private async Task CheckSqlFluffToolUpdateAsync(string installedVersion, bool userInitiated)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync();
            if (string.IsNullOrEmpty(installedVersion))
            {
                if (userInitiated)
                {
                    OutputLog.SetStatus("SQLFluff: tool found, but its version couldn't be determined to check for an update.");
                }

                return;
            }

            string latest;
            try
            {
                latest = await Task.Run(() => SqlFluffInstaller.GetLatestVersionAsync(CancellationToken.None));
            }
            catch (SqlFluffUpdateCheckException ex)
            {
                await JoinableTaskFactory.SwitchToMainThreadAsync();
                OutputLog.Write("SQLFluff tool version check failed: " + ex.Message);
                if (userInitiated)
                {
                    OutputLog.SetStatus("SQLFluff: tool version check failed - see the SQLFluff output pane.");
                }

                return;
            }

            await JoinableTaskFactory.SwitchToMainThreadAsync();
            if (latest == null || !PyPiPackageInfoParser.IsNewer(installedVersion, latest))
            {
                if (userInitiated)
                {
                    OutputLog.SetStatus("SQLFluff: tool is up to date (v" + installedVersion + ").");
                }

                return;
            }

            OutputLog.Write("A newer SQLFluff tool release is available on PyPI: v" + latest + " (installed: v" + installedVersion + ").");

            int result = VsShellUtilities.ShowMessageBox(
                this,
                "A newer version of the SQLFluff tool is available: v" + latest + " (you have v" + installedVersion + ").\n\n" +
                "Upgrade now via 'pip install --upgrade sqlfluff'?",
                "SQLFluff for SSMS: SQLFluff Tool Update Available",
                OLEMSGICON.OLEMSGICON_QUERY, OLEMSGBUTTON.OLEMSGBUTTON_YESNO, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_SECOND);

            if (result != (int)VSConstants.MessageBoxResult.IDYES)
            {
                OutputLog.SetStatus("SQLFluff: tool v" + latest + " available. Run SQLFluff > Install/Update SQLFluff Tool... any time, or 'pip install --upgrade sqlfluff'.");
                return;
            }

            await RunPipInstallAsync(upgrade: true);
        }

        // Shared by both the "not found" and "outdated" prompts above - the only difference
        // between installing and upgrading sqlfluff via pip is the --upgrade flag.
        private async Task RunPipInstallAsync(bool upgrade)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync();
            string verb = upgrade ? "Upgrading" : "Installing";
            OutputLog.Write(verb + " SQLFluff via pip...");
            OutputLog.SetStatus("SQLFluff: " + verb.ToLowerInvariant() + " via pip... (see the SQLFluff output pane)");

            PipInstallResult installResult;
            try
            {
                installResult = await Task.Run(() => SqlFluffInstaller.InstallAsync(
                    upgrade, line => OutputLog.Write("pip: " + line), CancellationToken.None));
            }
            catch (SqlFluffInstallException ex)
            {
                await JoinableTaskFactory.SwitchToMainThreadAsync();
                OutputLog.Write("SQLFluff install failed: " + ex.Message);
                OutputLog.SetStatus("SQLFluff: install failed - see the SQLFluff output pane.");
                return;
            }

            await JoinableTaskFactory.SwitchToMainThreadAsync();
            if (!installResult.Success)
            {
                OutputLog.Write("pip exited with code " + installResult.ExitCode + ".");
                OutputLog.SetStatus("SQLFluff: install failed (pip exited with code " + installResult.ExitCode + ") - see the SQLFluff output pane.");
                return;
            }

            SqlFluffRunner.ResetCache();
            OutputLog.Write("SQLFluff " + (upgrade ? "updated" : "installed") + " successfully via pip.");

            // Confirm it's actually reachable now rather than trusting pip's exit code alone -
            // e.g. a freshly installed sqlfluff.exe landing in a Scripts folder that isn't on PATH.
            SqlFluffSettings verifySettings = GetSettings();
            SqlFluffAvailability verify = await Task.Run(() => SqlFluffRunner.CheckAvailabilityAsync(verifySettings, CancellationToken.None));
            await JoinableTaskFactory.SwitchToMainThreadAsync();
            if (verify.IsAvailable)
            {
                OutputLog.SetStatus("SQLFluff: " + (upgrade ? "updated" : "installed") + " successfully (v" + (verify.Version ?? "unknown") + ").");
            }
            else
            {
                OutputLog.Write(verify.Error);
                OutputLog.SetStatus("SQLFluff: pip reported success, but SQLFluff still isn't reachable - see the SQLFluff output pane, or set its path in Tools > Options > SQLFluff.");
            }
        }

        private void CheckForUpdates(bool userInitiated)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            JoinableTaskFactory.RunAsync(() => CheckForUpdatesAsync(userInitiated)).Task.FileAndForget("sqlfluff/check-for-updates");
        }

        // Checks GitHub for a newer release of the extension itself (not sqlfluff). The silent
        // startup check (userInitiated: false) only ever logs/sets status - it never prompts or
        // downloads anything on its own. The explicit "Check for Updates..." command additionally
        // offers to download and launch the installer.
        private async Task CheckForUpdatesAsync(bool userInitiated)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync();
            if (userInitiated)
            {
                OutputLog.SetStatus("SQLFluff: checking for updates...");
            }

            string installedVersion = GetType().Assembly.GetName().Version.ToString(3);
            UpdateInfo latest;
            try
            {
                latest = await Task.Run(() => ExtensionUpdater.CheckForNewerReleaseAsync(installedVersion, CancellationToken.None));
            }
            catch (UpdateCheckException ex)
            {
                await JoinableTaskFactory.SwitchToMainThreadAsync();
                OutputLog.Write("Update check failed: " + ex.Message);
                if (userInitiated)
                {
                    OutputLog.SetStatus("SQLFluff: update check failed - see the SQLFluff output pane.");
                }

                return;
            }

            await JoinableTaskFactory.SwitchToMainThreadAsync();
            if (latest == null)
            {
                if (userInitiated)
                {
                    OutputLog.SetStatus("SQLFluff: you're already up to date (v" + installedVersion + ").");
                }

                return;
            }

            OutputLog.Write("Update available: v" + latest.Version + " (installed: v" + installedVersion + "). " + latest.ReleaseUrl);

            if (!userInitiated)
            {
                OutputLog.SetStatus("SQLFluff: update v" + latest.Version + " available. Run SQLFluff > Check for Updates... to install it.");
                return;
            }

            int result = VsShellUtilities.ShowMessageBox(
                this,
                "Version " + latest.Version + " is available (you have v" + installedVersion + ").\n\n" +
                "Download and install it now? SSMS may need to close before the installer can proceed.",
                "SQLFluff for SSMS: Update Available",
                OLEMSGICON.OLEMSGICON_QUERY, OLEMSGBUTTON.OLEMSGBUTTON_YESNO, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);

            if (result != (int)VSConstants.MessageBoxResult.IDYES)
            {
                OutputLog.SetStatus("SQLFluff: update v" + latest.Version + " available. See the SQLFluff output pane.");
                return;
            }

            OutputLog.SetStatus("SQLFluff: downloading v" + latest.Version + "...");
            string vsixPath;
            try
            {
                vsixPath = await Task.Run(() => ExtensionUpdater.DownloadVsixAsync(latest.VsixDownloadUrl, CancellationToken.None));
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is IOException || ex is TaskCanceledException)
            {
                await JoinableTaskFactory.SwitchToMainThreadAsync();
                OutputLog.Write("Update download failed: " + ex.Message);
                OutputLog.SetStatus("SQLFluff: update download failed - see the SQLFluff output pane, or download it manually from " + latest.ReleaseUrl);
                return;
            }

            await JoinableTaskFactory.SwitchToMainThreadAsync();
            OutputLog.Write("Downloaded update to " + vsixPath + "; launching the installer.");
            OutputLog.SetStatus("SQLFluff: launching the installer for v" + latest.Version + "...");

            try
            {
                ExtensionUpdater.LaunchInstaller(vsixPath);
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                OutputLog.Write("Could not launch the installer: " + ex.Message);
                OutputLog.SetStatus("SQLFluff: could not launch the installer - open " + vsixPath + " manually.");
            }
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
