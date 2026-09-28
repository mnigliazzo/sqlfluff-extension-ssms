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
    // Options/registration.json). Values saved by versions that still used a DialogPage options
    // page are copied over once on upgrade; after that the old storage is never read.
    internal sealed class ExtensionOptions
    {
        private const string LegacyCollection = @"DialogPage\SqlFluff.Ssms.Options.SqlFluffOptionsPage";
        private const string StateCollection = "SqlFluff.Ssms";
        private const string MigratedFlag = "LegacyOptionsMigrated";
        private const string ToolbarShownForVersionName = "ToolbarShownForVersion";

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
                OutputLog.Write("Unified Settings isn't available; SQLFluff is using its default options.");
            }
            else
            {
                options.MigrateLegacyOnce();
            }

            return options;
        }

        public SqlFluffSettings Read()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            ISettingsReader reader = _unified?.GetReader();
            SqlFluffSettings settings = SettingsSchema.Build(definition => ReadUnified(reader, definition));

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
                // Never let an options read break linting; fall back to the default.
                if (!_reportedReadFailure)
                {
                    _reportedReadFailure = true;
                    OutputLog.Write("Couldn't read option '" + definition.Moniker + "' from Unified Settings (" + ex.Message + "); using its default.");
                }

                return null;
            }
        }

        private object ValueOrNull<T>(SettingRetrieval<T> retrieval)
        {
            if (retrieval.Outcome == SettingRetrievalOutcome.NotSupportedInClassicMode && !_reportedReadFailure)
            {
                _reportedReadFailure = true;
                OutputLog.Write("Unified Settings is turned off in this SSMS; SQLFluff is using its default options until it's turned back on.");
            }

            return retrieval.Outcome == SettingRetrievalOutcome.Success ? (object)retrieval.Value : null;
        }

        private object ReadPreviousVersionValue(SettingDefinition definition)
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
                object legacy = ReadPreviousVersionValue(definition);
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

        // Not a user option: the last extension version SqlFluffPackage forced the toolbar visible for.
        public string ToolbarShownForVersion
        {
            get => GetState(ToolbarShownForVersionName);
            set => SetState(ToolbarShownForVersionName, value);
        }

        private string GetState(string name)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return _store.PropertyExists(StateCollection, name) ? _store.GetString(StateCollection, name) : string.Empty;
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
