using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.TableControl;
using Microsoft.VisualStudio.Shell.TableManager;
using Microsoft.VisualStudio.Utilities;

namespace SqlFluff.Ssms.Services
{
    // The Error List's default navigation opens DocumentName as a file on disk, which fails for an
    // unsaved query (its "path" is just the tab name) and would open a second copy of a document
    // instead of jumping into the live buffer. Route SQLFluff rows to their own navigate callbacks.
    [Export(typeof(ITableControlEventProcessorProvider))]
    [DataSourceType(StandardTableDataSources.ErrorTableDataSource)]
    [DataSource(ErrorListService.SourceIdentifier)]
    [Name("SQLFluff Error List navigation")]
    [Order(Before = "Default")]
    internal sealed class ErrorListNavigationProvider : ITableControlEventProcessorProvider
    {
        public ITableControlEventProcessor GetAssociatedEventProcessor(IWpfTableControl tableControl) => new Processor();

        private sealed class Processor : TableControlEventProcessorBase
        {
            public override void PreprocessNavigate(ITableEntryHandle entry, TableEntryNavigateEventArgs e)
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                if (entry.TryGetSnapshot(out ITableEntriesSnapshot snapshot, out int index) &&
                    snapshot is ErrorListService.EntriesSnapshot ours &&
                    ours.TryNavigate(index))
                {
                    e.Handled = true;
                }
            }
        }
    }
}
