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
        [Description("Optional path to a .sqlfluff / pyproject.toml file passed as --config. Leave empty to let SQLFluff discover config next to the SQL file and in your user profile.")]
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
        [Description("Lint a SQL document the first time it is shown.")]
        public bool LintOnOpen { get; set; } = true;

        [Category(TriggersCategory)]
        [DisplayName("Lint on save")]
        [Description("Lint a SQL document every time it is saved.")]
        public bool LintOnSave { get; set; } = true;

        [Category(TriggersCategory)]
        [DisplayName("Lint while typing")]
        [Description("Lint automatically after you stop typing. SQLFluff can be slow on large scripts, so this is off by default.")]
        public bool LintOnType { get; set; } = false;

        [Category(TriggersCategory)]
        [DisplayName("Typing delay (ms)")]
        [Description("How long to wait after the last keystroke before linting while typing.")]
        public int TypeDelayMs { get; set; } = 1500;

        [Category(DisplayCategory)]
        [DisplayName("Report violations as")]
        [Description("How rule violations are shown in the Error List and as squiggles. Parse errors are always shown as errors.")]
        public OptionSeverity Severity { get; set; } = OptionSeverity.Warning;

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
