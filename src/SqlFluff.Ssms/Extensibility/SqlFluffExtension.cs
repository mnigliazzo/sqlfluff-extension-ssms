using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Microsoft.VisualStudio.Extensibility.Shell;

namespace SqlFluff.Ssms.Extensibility
{
    // Entry point for the VisualStudio.Extensibility (new model) side of this VSIX, hosted in-process
    // next to the VSSDK package (VssdkCompatibleExtension) because the editor pieces this extension
    // relies on - Error List table source, squiggle tagger, Light Bulb - have no out-of-process
    // equivalent yet.
    [VisualStudioContribution]
    internal sealed class SqlFluffExtension : Extension
    {
        public override ExtensionConfiguration ExtensionConfiguration => new ExtensionConfiguration
        {
            RequiresInProcessHosting = true,
        };
    }

    // Beta probe: whether SSMS 22 loads VisualStudio.Extensibility contributions at all. If this
    // command shows up under Tools and its prompt appears, the new model is usable here.
    [VisualStudioContribution]
    internal sealed class NewModelProbeCommand : Command
    {
        public NewModelProbeCommand(VisualStudioExtensibility extensibility)
            : base(extensibility)
        {
        }

        public override CommandConfiguration CommandConfiguration => new CommandConfiguration("%SqlFluff.NewModelProbe.DisplayName%")
        {
            Placements = new[] { CommandPlacement.KnownPlacements.ToolsMenu },
            Icon = new CommandIconConfiguration(ImageMoniker.KnownValues.Extension, IconSettings.IconAndText),
        };

        public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
        {
            await Extensibility.Shell().ShowPromptAsync(
                "VisualStudio.Extensibility works in this SSMS (in-process). This command was declared entirely in code, with no .vsct.",
                PromptOptions.OK,
                cancellationToken);
        }
    }
}
