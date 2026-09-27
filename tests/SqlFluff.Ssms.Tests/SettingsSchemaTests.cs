using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SqlFluff.Ssms.Core;
using Xunit;

namespace SqlFluff.Ssms.Tests
{
    public class SettingsSchemaTests
    {
        private static JsonElement LoadRegistrationProperties()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "registration.json");
            var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path), options);
            return document.RootElement.GetProperty("properties").Clone();
        }

        [Fact]
        public void EverySchemaSettingIsRegisteredWithMatchingTypeAndDefault()
        {
            JsonElement properties = LoadRegistrationProperties();

            foreach (SettingDefinition definition in SettingsSchema.All)
            {
                Assert.True(properties.TryGetProperty(definition.Moniker, out JsonElement registered), definition.Moniker + " is missing from registration.json");

                string expectedType = definition.Kind switch
                {
                    SettingKind.Boolean => "boolean",
                    SettingKind.Integer => "integer",
                    _ => "string",
                };
                Assert.Equal(expectedType, registered.GetProperty("type").GetString());

                JsonElement registeredDefault = registered.GetProperty("default");
                object actualDefault = definition.Kind switch
                {
                    SettingKind.Boolean => registeredDefault.GetBoolean(),
                    SettingKind.Integer => registeredDefault.GetInt32(),
                    _ => registeredDefault.GetString(),
                };
                Assert.Equal(definition.Default, actualDefault);
            }
        }

        [Fact]
        public void RegistrationHasNoSettingsMissingFromTheSchema()
        {
            var known = new HashSet<string>(SettingsSchema.All.Select(d => d.Moniker));
            foreach (JsonProperty property in LoadRegistrationProperties().EnumerateObject())
            {
                Assert.Contains(property.Name, known);
            }
        }

        [Fact]
        public void SeverityEnumValuesAreAllParseable()
        {
            JsonElement severity = LoadRegistrationProperties().GetProperty("sqlfluff.display.reportViolationsAs");
            var parsed = severity.GetProperty("enum").EnumerateArray().Select(v => SettingsSchema.ParseSeverity(v.GetString())).ToList();
            Assert.Equal(new[] { DiagnosticSeverity.Warning, DiagnosticSeverity.Error, DiagnosticSeverity.Message }, parsed);
        }

        [Fact]
        public void BuildWithNothingStoredMatchesPreviousOptionsPageDefaults()
        {
            SqlFluffSettings settings = SettingsSchema.Build(_ => null);

            Assert.Equal("sqlfluff", settings.ExecutablePath);
            Assert.Equal("tsql", settings.Dialect);
            Assert.Equal(string.Empty, settings.ConfigFile);
            Assert.Equal(string.Empty, settings.Rules);
            Assert.Equal(string.Empty, settings.ExcludeRules);
            Assert.Equal(60, settings.TimeoutSeconds);
            Assert.False(settings.AutoSaveAfterFix);
            Assert.True(settings.LintOnOpen);
            Assert.True(settings.LintOnSave);
            Assert.False(settings.FormatOnOpen);
            Assert.False(settings.FormatOnSave);
            Assert.False(settings.FixOnSave);
            Assert.True(settings.LintOnType);
            Assert.Equal(1500, settings.TypeDelayMs);
            Assert.Equal(DiagnosticSeverity.Warning, settings.Severity);
            Assert.True(settings.CheckForUpdatesOnStartup);
            Assert.True(settings.CheckSqlFluffToolOnStartup);
            Assert.True(settings.CheckMcpServerOnStartup);
        }

        [Fact]
        public void BuildUsesStoredValuesAndClampsTypingDelay()
        {
            var stored = new Dictionary<string, object>
            {
                ["sqlfluff.execution.dialect"] = "ansi",
                ["sqlfluff.automaticLinting.lintOnOpen"] = false,
                ["sqlfluff.automaticLinting.typingDelayMs"] = 100,
                ["sqlfluff.display.reportViolationsAs"] = "error",
            };

            SqlFluffSettings settings = SettingsSchema.Build(d => stored.TryGetValue(d.Moniker, out object v) ? v : null);

            Assert.Equal("ansi", settings.Dialect);
            Assert.False(settings.LintOnOpen);
            Assert.Equal(SettingsSchema.MinTypeDelayMs, settings.TypeDelayMs);
            Assert.Equal(DiagnosticSeverity.Error, settings.Severity);
            Assert.Equal(60, settings.TimeoutSeconds);
        }

        [Theory]
        [InlineData("LintOnOpen", "False", false)]
        [InlineData("LintOnOpen", "true", true)]
        [InlineData("TypeDelayMs", "2500", 2500)]
        [InlineData("Severity", "Error", "error")]
        [InlineData("Severity", "2", "message")]
        [InlineData("Dialect", "ansi", "ansi")]
        [InlineData("Rules", "", "")]
        public void TryParseLegacyConvertsDialogPageStrings(string legacyName, string raw, object expected)
        {
            SettingDefinition definition = SettingsSchema.All.Single(d => d.LegacyName == legacyName);

            Assert.True(SettingsSchema.TryParseLegacy(definition, raw, out object value));
            Assert.Equal(expected, value);
        }

        [Theory]
        [InlineData("LintOnOpen", "yes")]
        [InlineData("TypeDelayMs", "fast")]
        [InlineData("Severity", "Critical")]
        [InlineData("Dialect", null)]
        public void TryParseLegacyRejectsUnparseableValues(string legacyName, string raw)
        {
            SettingDefinition definition = SettingsSchema.All.Single(d => d.LegacyName == legacyName);

            Assert.False(SettingsSchema.TryParseLegacy(definition, raw, out _));
        }

        [Fact]
        public void LegacyNamesMatchThePreviousOptionsPageProperties()
        {
            // The old SqlFluffOptionsPage's persisted property names, which migration reads from.
            var previous = new[]
            {
                "ExecutablePath", "Dialect", "ConfigFile", "Rules", "ExcludeRules", "TimeoutSeconds",
                "AutoSaveAfterFix", "LintOnOpen", "LintOnSave", "FormatOnOpen", "FormatOnSave", "FixOnSave",
                "LintOnType", "TypeDelayMs", "Severity", "CheckForUpdatesOnStartup",
                "CheckSqlFluffToolOnStartup", "CheckMcpServerOnStartup",
            };

            Assert.Equal(previous.OrderBy(n => n), SettingsSchema.All.Select(d => d.LegacyName).OrderBy(n => n));
        }
    }
}
