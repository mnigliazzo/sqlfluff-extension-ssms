using System;
using System.Collections.Generic;
using System.Globalization;

namespace SqlFluff.Ssms.Core
{
    internal enum SettingKind
    {
        Boolean,
        Integer,
        String,
        Severity,
    }

    internal sealed class SettingDefinition
    {
        public SettingDefinition(string moniker, string legacyName, SettingKind kind, object defaultValue, Action<SqlFluffSettings, object> apply)
        {
            Moniker = moniker;
            LegacyName = legacyName;
            Kind = kind;
            Default = defaultValue;
            Apply = apply;
        }

        // Unified Settings moniker, as registered in Options/registration.json.
        public string Moniker { get; }

        // Property name the pre-Unified-Settings DialogPage stored this option under.
        public string LegacyName { get; }

        public SettingKind Kind { get; }

        public object Default { get; }

        public Action<SqlFluffSettings, object> Apply { get; }
    }

    // The extension's user options, independent of where they're stored. Monikers and defaults
    // must match Options/registration.json - SettingsSchemaTests checks every entry against it.
    internal static class SettingsSchema
    {
        public const int MinTypeDelayMs = 300;

        public static readonly IReadOnlyList<SettingDefinition> All = new[]
        {
            new SettingDefinition("sqlfluff.execution.executablePath", "ExecutablePath", SettingKind.String, "sqlfluff", (s, v) => s.ExecutablePath = (string)v),
            new SettingDefinition("sqlfluff.execution.dialect", "Dialect", SettingKind.String, "tsql", (s, v) => s.Dialect = (string)v),
            new SettingDefinition("sqlfluff.execution.configFile", "ConfigFile", SettingKind.String, string.Empty, (s, v) => s.ConfigFile = (string)v),
            new SettingDefinition("sqlfluff.execution.rules", "Rules", SettingKind.String, string.Empty, (s, v) => s.Rules = (string)v),
            new SettingDefinition("sqlfluff.execution.excludeRules", "ExcludeRules", SettingKind.String, string.Empty, (s, v) => s.ExcludeRules = (string)v),
            new SettingDefinition("sqlfluff.execution.timeoutSeconds", "TimeoutSeconds", SettingKind.Integer, 60, (s, v) => s.TimeoutSeconds = (int)v),
            new SettingDefinition("sqlfluff.execution.autoSaveAfterFix", "AutoSaveAfterFix", SettingKind.Boolean, false, (s, v) => s.AutoSaveAfterFix = (bool)v),
            new SettingDefinition("sqlfluff.automaticLinting.lintOnOpen", "LintOnOpen", SettingKind.Boolean, true, (s, v) => s.LintOnOpen = (bool)v),
            new SettingDefinition("sqlfluff.automaticLinting.lintOnSave", "LintOnSave", SettingKind.Boolean, true, (s, v) => s.LintOnSave = (bool)v),
            new SettingDefinition("sqlfluff.automaticLinting.formatOnOpen", "FormatOnOpen", SettingKind.Boolean, false, (s, v) => s.FormatOnOpen = (bool)v),
            new SettingDefinition("sqlfluff.automaticLinting.formatOnSave", "FormatOnSave", SettingKind.Boolean, false, (s, v) => s.FormatOnSave = (bool)v),
            new SettingDefinition("sqlfluff.automaticLinting.fixOnSave", "FixOnSave", SettingKind.Boolean, false, (s, v) => s.FixOnSave = (bool)v),
            new SettingDefinition("sqlfluff.automaticLinting.lintWhileTyping", "LintOnType", SettingKind.Boolean, true, (s, v) => s.LintOnType = (bool)v),
            new SettingDefinition("sqlfluff.automaticLinting.typingDelayMs", "TypeDelayMs", SettingKind.Integer, 1500, (s, v) => s.TypeDelayMs = (int)v),
            new SettingDefinition("sqlfluff.display.reportViolationsAs", "Severity", SettingKind.Severity, "warning", (s, v) => s.Severity = ParseSeverity((string)v)),
            new SettingDefinition("sqlfluff.updates.checkForUpdatesOnStartup", "CheckForUpdatesOnStartup", SettingKind.Boolean, true, (s, v) => s.CheckForUpdatesOnStartup = (bool)v),
            new SettingDefinition("sqlfluff.updates.checkSqlFluffToolOnStartup", "CheckSqlFluffToolOnStartup", SettingKind.Boolean, true, (s, v) => s.CheckSqlFluffToolOnStartup = (bool)v),
        };

        // read returns the stored value for a definition (already of the definition's CLR type),
        // or null to fall back to its default.
        public static SqlFluffSettings Build(Func<SettingDefinition, object> read)
        {
            var settings = new SqlFluffSettings();
            foreach (SettingDefinition definition in All)
            {
                definition.Apply(settings, read(definition) ?? definition.Default);
            }

            settings.TypeDelayMs = Math.Max(MinTypeDelayMs, settings.TypeDelayMs);
            return settings;
        }

        // The old DialogPage persisted every option as an invariant string produced by its
        // TypeConverter: "True"/"False", "1500", and the OptionSeverity enum name ("Warning").
        public static bool TryParseLegacy(SettingDefinition definition, string raw, out object value)
        {
            value = null;
            if (raw == null)
            {
                return false;
            }

            switch (definition.Kind)
            {
                case SettingKind.Boolean:
                    if (bool.TryParse(raw.Trim(), out bool flag))
                    {
                        value = flag;
                        return true;
                    }

                    return false;

                case SettingKind.Integer:
                    if (int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
                    {
                        value = number;
                        return true;
                    }

                    return false;

                case SettingKind.Severity:
                    string severity = LegacySeverityToMoniker(raw.Trim());
                    value = severity;
                    return severity != null;

                default:
                    value = raw;
                    return true;
            }
        }

        public static DiagnosticSeverity ParseSeverity(string value)
        {
            switch ((value ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "error":
                    return DiagnosticSeverity.Error;
                case "message":
                    return DiagnosticSeverity.Message;
                default:
                    return DiagnosticSeverity.Warning;
            }
        }

        private static string LegacySeverityToMoniker(string raw)
        {
            switch (raw.ToLowerInvariant())
            {
                case "warning":
                case "0":
                    return "warning";
                case "error":
                case "1":
                    return "error";
                case "message":
                case "2":
                    return "message";
                default:
                    return null;
            }
        }
    }
}
