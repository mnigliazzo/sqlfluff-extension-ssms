using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;
using SqlFluff.Ssms.Core;

namespace SqlFluff.Ssms.Options
{
    [Guid(PackageGuids.OptionsPageString)]
    [ComVisible(true)]
    public class SqlFluffOptionsPage : DialogPage
    {
        private const string ExecutionCategory = "SQLFluff";
        private const string TriggersCategory = "Automatic linting";
        private const string DisplayCategory = "Display";
        private const string UpdatesCategory = "Updates";

        // Read from the running assembly's own metadata rather than a source-checked-in
        // constant: the version is never committed to AssemblyInfo.cs for a given release (see
        // CLAUDE.md's Release process) — it's only ever patched in transiently by CI right before
        // that release's build, so this is the only value that's always accurate for whatever is
        // actually installed.
        [Category(ExecutionCategory)]
        [DisplayName("Extension version")]
        [Description("The installed version of the SQLFluff for SSMS extension itself (not the sqlfluff tool).")]
        [ReadOnly(true)]
        public string ExtensionVersion => typeof(SqlFluffOptionsPage).Assembly.GetName().Version.ToString(3);

        [Category(ExecutionCategory)]
        [DisplayName("SQLFluff executable")]
        [Description("Full path to sqlfluff.exe. Leave as 'sqlfluff' to auto-detect (PATH, the Python Scripts folders, then 'py -m sqlfluff').")]
        public string ExecutablePath { get; set; } = "sqlfluff";

        [Category(ExecutionCategory)]
        [DisplayName("Dialect")]
        [Description("SQLFluff dialect passed as --dialect (for SQL Server use 'tsql'). Leave empty to use the dialect from your .sqlfluff config.")]
        public string Dialect { get; set; } = "tsql";

        [Category(ExecutionCategory)]
        [DisplayName("Config file")]
        [Description("Fallback .sqlfluff path used only when no .sqlfluff is found by walking up from the open document's folder (e.g. an unsaved new document, or one outside any project that has its own .sqlfluff). A .sqlfluff found near the document always takes priority over this. Leave empty to rely entirely on that discovery plus SQLFluff's own defaults.")]
        public string ConfigFile { get; set; } = string.Empty;

        [Category(ExecutionCategory)]
        [DisplayName("Rules")]
        [Description("Comma-separated rules to run (--rules), e.g. 'LT01,LT02'. Leave empty to use the config / all default rules.")]
        public string Rules { get; set; } = string.Empty;

        [Category(ExecutionCategory)]
        [DisplayName("Excluded rules")]
        [Description("Comma-separated rules to skip (--exclude-rules), e.g. 'LT05,AM04'.")]
        public string ExcludeRules { get; set; } = string.Empty;

        [Category(ExecutionCategory)]
        [DisplayName("Timeout (seconds)")]
        [Description("Maximum time a SQLFluff run may take before it is cancelled.")]
        public int TimeoutSeconds { get; set; } = 60;

        [Category(ExecutionCategory)]
        [DisplayName("Auto-save after fix")]
        [Description("Automatically save the document after Fix or Format successfully change it. When off (default), you save manually (Ctrl+S).")]
        public bool AutoSaveAfterFix { get; set; } = false;

        [Category(TriggersCategory)]
        [DisplayName("Lint on open")]
        [Description("Lint a SQL document the first time it is shown, so you see existing issues without having to type, save, or wait first - matching how linters in other IDEs (e.g. ESLint in VS Code) behave on file open.")]
        public bool LintOnOpen { get; set; } = true;

        [Category(TriggersCategory)]
        [DisplayName("Lint on save")]
        [Description("Lint a SQL document every time it is saved.")]
        public bool LintOnSave { get; set; } = true;

        [Category(TriggersCategory)]
        [DisplayName("Format on open")]
        [Description("Run Format (the safe, stable rule subset) on a SQL document the first time it is shown, before you've touched it. Off by default - unlike Lint on open, this rewrites the buffer (marking it dirty) as soon as the file opens, with no explicit action from you.")]
        public bool FormatOnOpen { get; set; } = false;

        [Category(TriggersCategory)]
        [DisplayName("Format on save")]
        [Description("Run Format (the safe, stable rule subset) on a SQL document before it is saved, so the formatted result is what gets written to disk. Off by default. If formatting fails, the save proceeds with the document unchanged.")]
        public bool FormatOnSave { get; set; } = false;

        [Category(TriggersCategory)]
        [DisplayName("Fix on save")]
        [Description("Run Fix (ALL fixable rules, not just the safe subset) on a SQL document before it is saved - like VS Code/ESLint's 'fix all on save'. Caution: unlike Format, Fix can rewrite structure, not just style, on every save with no per-change review - review the diff with Fix/Undo manually first before enabling this. If both are on, Fix on save wins for that save. Off by default. If fixing itself fails/errors, the save proceeds with the document unchanged - but a Fix that runs successfully is applied and saved as-is, unreviewed.")]
        public bool FixOnSave { get; set; } = false;

        [Category(TriggersCategory)]
        [DisplayName("Lint while typing")]
        [Description("Lint automatically after you stop typing. SQLFluff can be slow on large scripts - turn this off if it becomes disruptive.")]
        public bool LintOnType { get; set; } = true;

        [Category(TriggersCategory)]
        [DisplayName("Typing delay (ms)")]
        [Description("How long to wait after the last keystroke before linting while typing.")]
        public int TypeDelayMs { get; set; } = 1500;

        [Category(DisplayCategory)]
        [DisplayName("Report violations as")]
        [Description("How rule violations are shown in the Error List and as squiggles. Parse errors are always shown as errors.")]
        public OptionSeverity Severity { get; set; } = OptionSeverity.Warning;

        [Category(UpdatesCategory)]
        [DisplayName("Check for updates on startup")]
        [Description("Check GitHub for a newer version of the SQLFluff for SSMS extension itself when SSMS starts, and note it in the SQLFluff output pane and status bar if one is found. Never downloads or installs anything on its own — use SQLFluff > Check for Updates... to do that.")]
        public bool CheckForUpdatesOnStartup { get; set; } = true;

        [Category(UpdatesCategory)]
        [DisplayName("Check SQLFluff tool on startup")]
        [Description("Check when SSMS starts whether the sqlfluff Python tool (not the extension) is installed and up to date, and, if not, prompt to install/upgrade it via pip. Turn this off on a machine without internet/PyPI access, or to manage sqlfluff yourself — use SQLFluff > Install/Update SQLFluff Tool... to run the same check on demand regardless of this setting.")]
        public bool CheckSqlFluffToolOnStartup { get; set; } = true;

        // Not user-facing. The SQLFluff toolbar's `DefaultDocked` CommandFlag (SqlFluffPackage.vsct)
        // doesn't reliably make SSMS 22 show it on its own — see SqlFluffPackage.EnsureToolbarVisibleOnce,
        // which forces it visible via DTE.CommandBars once per extension version and records the
        // version here (rather than a plain bool) so a user who later hides it isn't fought on every
        // startup, but each new release — which may add buttons to the toolbar, as #42 itself did —
        // still gets one fresh chance to surface it.
        [Browsable(false)]
        public string ToolbarShownForVersion { get; set; } = string.Empty;

        public override void SaveSettingsToStorage()
        {
            base.SaveSettingsToStorage();
            SqlFluffRunner.ResetCache();
        }

        internal SqlFluffSettings ToSettings()
        {
            return new SqlFluffSettings
            {
                ExecutablePath = ExecutablePath,
                Dialect = Dialect,
                ConfigFile = ConfigFile,
                Rules = Rules,
                ExcludeRules = ExcludeRules,
                TimeoutSeconds = TimeoutSeconds,
                LintOnOpen = LintOnOpen,
                LintOnSave = LintOnSave,
                LintOnType = LintOnType,
                TypeDelayMs = System.Math.Max(300, TypeDelayMs),
                Severity = (DiagnosticSeverity)(int)Severity,
                AutoSaveAfterFix = AutoSaveAfterFix,
                FormatOnOpen = FormatOnOpen,
                FormatOnSave = FormatOnSave,
                FixOnSave = FixOnSave,
                CheckForUpdatesOnStartup = CheckForUpdatesOnStartup,
                CheckSqlFluffToolOnStartup = CheckSqlFluffToolOnStartup,
            };
        }
    }

    public enum OptionSeverity
    {
        Warning = 0,
        Error = 1,
        Message = 2,
    }
}
