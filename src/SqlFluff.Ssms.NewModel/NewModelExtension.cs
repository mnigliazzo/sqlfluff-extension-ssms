using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Microsoft.VisualStudio.Extensibility.Shell;

namespace SqlFluff.Ssms.NewModel
{
    [VisualStudioContribution]
    internal sealed class NewModelExtension : Extension
    {
        public override ExtensionConfiguration ExtensionConfiguration => new ExtensionConfiguration
        {
            Metadata = new ExtensionMetadata(
                id: "SqlFluff.Ssms.NewModel.3f6d1c2a-8e4b-4a7f-9c15-2b7e0d9a4c61",
                version: ExtensionAssemblyVersion,
                publisherName: "Matias Nigliazzo",
                displayName: "SQLFluff for SSMS (new extension model preview)",
                description: "Preview of SQLFluff for SSMS on the out-of-process VisualStudio.Extensibility model: lints .sql files through a Language Server.")
            {
                // SSMS is amd64-only. Left unset, an AnyCPU project targets amd64 and arm64, and
                // the Visual Studio Installer (which installs new-model extensions) refuses a
                // manifest with more than one install target.
                InstallationTargetArchitecture = VisualStudioArchitecture.Amd64,
            },
        };
    }

    // Probe: if this shows up under Tools and its prompt appears, SSMS 22 loads out-of-process
    // VisualStudio.Extensibility extensions.
    [VisualStudioContribution]
    internal sealed class NewModelProbeCommand : Command
    {
        public NewModelProbeCommand(VisualStudioExtensibility extensibility)
            : base(extensibility)
        {
        }

        public override CommandConfiguration CommandConfiguration => new CommandConfiguration("%SqlFluff.NewModel.ProbeCommand.DisplayName%")
        {
            Placements = new[] { CommandPlacement.KnownPlacements.ToolsMenu },
            Icon = new CommandIconConfiguration(ImageMoniker.KnownValues.Extension, IconSettings.IconAndText),
        };

        public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
        {
            await Extensibility.Shell().ShowPromptAsync(
                "The new VisualStudio.Extensibility model works in this SSMS: this command runs in its own .NET " +
                Environment.Version + " process, outside SSMS.",
                PromptOptions.OK,
                cancellationToken);
        }
    }
}
