using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Settings;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Settings;
using Microsoft.VisualStudio.Utilities.UnifiedSettings;
using ISettingsManager = Microsoft.VisualStudio.Utilities.UnifiedSettings.ISettingsManager;
using SqlFluff.Ssms.Core;
using SqlFluff.Ssms.Services;

namespace SqlFluff.Ssms.Options
{
    // Service identity for Unified Settings' ISettingsManager. ISettingsManager's docs name the
    // service (SVsUnifiedSettingsManager), but its type ships only in an internal interop assembly
    // (Microsoft.Internal.VisualStudio.Interop), so it's re-declared here by the same GUID -
    // GetServiceAsync resolves services by type GUID. Requesting it directly, rather than waiting
    // for a [ProvideSettingsObserver] observer, makes values readable synchronously from the very
    // start of package initialization (e.g. "Lint on open" for tabs restored at startup).
    [Guid("e3684f31-344e-42ea-9047-b620fdc7ac25")]
    internal interface SVsUnifiedSettingsManager
    {
    }

    // User options live in Unified Settings (Tools > Options > SQLFluff, declared in
    // Options/registration.json). Values from the previous DialogPage-based options page are copied
    // over once, and are also what's read if Unified Settings is unavailable or turned off
    // ("classic mode"), per ISettingsReader's guidance to fall back to legacy storage there.
    internal sealed class ExtensionOptions
    {
        private const string LegacyCollection = @"DialogPage\SqlFluff.Ssms.Options.SqlFluffOptionsPage";
        private const string StateCollection = "SqlFluff.Ssms";
        private const string MigratedFlag = "LegacyOptionsMigrated";

        private readonly ISettingsManager _unified;
        private readonly WritableSettingsStore _store;
        private string _lastExecutablePath;
        private bool _reportedReadFailure;

        private ExtensionOptions(ISettingsManager unified, WritableSettingsStore store)
        {
            _unified = unified;
            _store = store;
        }

        public static async Task<ExtensionOptions> CreateAsync(AsyncPackage package, CancellationToken cancellationToken)
        {
            var unified = await package.GetServiceAsync(typeof(SVsUnifiedSettingsManager)) as ISettingsManager;
            await package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            WritableSettingsStore store = new ShellSettingsManager(package).GetWritableSettingsStore(SettingsScope.UserSettings);
            var options = new ExtensionOptions(unified, store);
            if (unified == null)
            {
                OutputLog.Write("Unified Settings isn't available; using the previous options storage (read-only).");
            }
            else
            {
                options.MigrateLegacyOnce();
            }

            return options;
        }

        // Not user-facing: see SqlFluffPackage.EnsureToolbarVisibleOnce.
        public string ToolbarShownForVersion
        {
            get => GetState(nameof(ToolbarShownForVersion));
            set => SetState(nameof(ToolbarShownForVersion), value);
        }

        // Not user-facing: see SqlFluffPackage.CheckMcpServerAsync.
        public string McpServerOfferedForVersion
        {
            get => GetState(nameof(McpServerOfferedForVersion));
            set => SetState(nameof(McpServerOfferedForVersion), value);
        }

        public SqlFluffSettings Read()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            ISettingsReader reader = _unified?.GetReader();
            SqlFluffSettings settings = SettingsSchema.Build(definition => ReadUnified(reader, definition) ?? ReadLegacy(definition));

            // A changed executable path invalidates SqlFluffRunner's cached launch command - the
            // old DialogPage did this in SaveSettingsToStorage.
            if (_lastExecutablePath != null && !string.Equals(_lastExecutablePath, settings.ExecutablePath, StringComparison.Ordinal))
            {
                SqlFluffRunner.ResetCache();
            }

            _lastExecutablePath = settings.ExecutablePath;
            return settings;
        }

        private object ReadUnified(ISettingsReader reader, SettingDefinition definition)
        {
            if (reader == null)
            {
                return null;
            }

            try
            {
                switch (definition.Kind)
                {
                    case SettingKind.Boolean:
                        return ValueOrNull(reader.GetValue<bool>(definition.Moniker));
                    case SettingKind.Integer:
                        return ValueOrNull(reader.GetValue<int>(definition.Moniker));
                    default:
                        return ValueOrNull(reader.GetValue<string>(definition.Moniker));
                }
            }
            catch (Exception ex)
            {
                // Beta-quality API surface in SSMS; never let an options read break linting.
                if (!_reportedReadFailure)
                {
                    _reportedReadFailure = true;
                    OutputLog.Write("Couldn't read option '" + definition.Moniker + "' from Unified Settings (" + ex.Message + "); using the previous options storage.");
                }

                return null;
            }
        }

        private static object ValueOrNull<T>(SettingRetrieval<T> retrieval)
        {
            return retrieval.Outcome == SettingRetrievalOutcome.Success ? (object)retrieval.Value : null;
        }

        private object ReadLegacy(SettingDefinition definition)
        {
            if (!_store.PropertyExists(LegacyCollection, definition.LegacyName))
            {
                return null;
            }

            return SettingsSchema.TryParseLegacy(definition, _store.GetString(LegacyCollection, definition.LegacyName), out object value)
                ? value
                : null;
        }

        private void MigrateLegacyOnce()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_store.PropertyExists(StateCollection, MigratedFlag))
            {
                return;
            }

            ISettingsWriter writer = _unified.GetWriter("SQLFluff for SSMS");
            int queued = 0;
            foreach (SettingDefinition definition in SettingsSchema.All)
            {
                object legacy = ReadLegacy(definition);
                if (legacy == null || Equals(legacy, definition.Default))
                {
                    continue;
                }

                SettingChangeResult result;
                switch (definition.Kind)
                {
                    case SettingKind.Boolean:
                        result = writer.EnqueueChange(definition.Moniker, (bool)legacy);
                        break;
                    case SettingKind.Integer:
                        result = writer.EnqueueChange(definition.Moniker, (int)legacy);
                        break;
                    default:
                        result = writer.EnqueueChange(definition.Moniker, (string)legacy);
                        break;
                }

                if (result.Outcome == SettingChangeOutcome.NotSupportedInClassicMode)
                {
                    // Unified Settings is turned off: nothing to migrate into. Leave the flag unset
                    // so the copy happens once it's turned back on.
                    return;
                }

                if (result.Outcome == SettingChangeOutcome.PendingCommit || result.Outcome == SettingChangeOutcome.PendingCommitWithoutValidation)
                {
                    queued++;
                }
                else
                {
                    OutputLog.Write("Couldn't migrate option '" + definition.Moniker + "' (" + result.Outcome + ": " + result.Message + ").");
                }
            }

            if (queued > 0)
            {
                // PendingApproval means Visual Studio will ask the user to accept the change;
                // either way it's out of our hands now, so don't re-offer it every startup.
                var commit = writer.RequestCommit("Migrate SQLFluff for SSMS options to the new settings experience");
                if (commit.Outcome != SettingCommitOutcome.Success && commit.Outcome != SettingCommitOutcome.PendingApproval)
                {
                    OutputLog.Write("Couldn't migrate previous options (" + commit.Outcome + "); will retry on next start.");
                    return;
                }

                OutputLog.Write("Migrated " + queued + " option(s) from the previous options page.");
            }

            EnsureStateCollection();
            _store.SetBoolean(StateCollection, MigratedFlag, true);
        }

        // Falls back to the old DialogPage collection so values recorded by earlier versions (e.g.
        // "MCP setup already offered for v1.15.3") carry over instead of re-prompting.
        private string GetState(string name)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_store.PropertyExists(StateCollection, name))
            {
                return _store.GetString(StateCollection, name);
            }

            return _store.PropertyExists(LegacyCollection, name) ? _store.GetString(LegacyCollection, name) : string.Empty;
        }

        private void SetState(string name, string value)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            EnsureStateCollection();
            _store.SetString(StateCollection, name, value ?? string.Empty);
        }

        private void EnsureStateCollection()
        {
            if (!_store.CollectionExists(StateCollection))
            {
                _store.CreateCollection(StateCollection);
            }
        }
    }
}
