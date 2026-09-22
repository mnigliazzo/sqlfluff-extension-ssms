namespace SqlFluff.Ssms.Core
{
    internal enum DiagnosticSeverity
    {
        Warning,
        Error,
        Message,
    }

    internal sealed class SqlFluffSettings
    {
        public string ExecutablePath { get; set; }
        public string Dialect { get; set; }
        public string ConfigFile { get; set; }
        public string Rules { get; set; }
        public string ExcludeRules { get; set; }
        public int TimeoutSeconds { get; set; }
        public bool LintOnSave { get; set; }
        public bool LintOnType { get; set; }
        public int TypeDelayMs { get; set; }
        public DiagnosticSeverity Severity { get; set; }
        public bool AutoSaveAfterFix { get; set; }
        public bool FormatOnSave { get; set; }

        public SqlFluffSettings Clone()
        {
            return new SqlFluffSettings
            {
                ExecutablePath = ExecutablePath,
                Dialect = Dialect,
                ConfigFile = ConfigFile,
                Rules = Rules,
                ExcludeRules = ExcludeRules,
                TimeoutSeconds = TimeoutSeconds,
                LintOnSave = LintOnSave,
                LintOnType = LintOnType,
                TypeDelayMs = TypeDelayMs,
                Severity = Severity,
                AutoSaveAfterFix = AutoSaveAfterFix,
                FormatOnSave = FormatOnSave,
            };
        }
    }
}
