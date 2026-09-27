using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;

namespace SqlFluff.Ssms.Editor
{
    // Clears a buffer's diagnostics when its last editor view closes. This is the reliable close
    // signal: the RDT's OnBeforeLastDocumentUnlock (DocumentEvents) only fires once every lock is
    // gone, and SSMS's query window lifecycle doesn't guarantee that happens when the tab closes -
    // e.g. closing an unsaved query with "Don't Save" left its violations in the Error List.
    [Export(typeof(IWpfTextViewCreationListener))]
    [ContentType("text")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    internal sealed class SqlFluffViewLifetimeListener : IWpfTextViewCreationListener
    {
        private sealed class ViewCount
        {
            public int Value;
        }

        [Import]
        private ITextDocumentFactoryService Documents { get; set; }

        public void TextViewCreated(IWpfTextView textView)
        {
            // Every view is counted, SQL or not: a buffer's content type can change while it's open
            // (e.g. Save As .sql), so only counting views that looked like SQL when created could
            // hit 0 while an uncounted view is still open. The SQL check happens at close instead.
            ITextBuffer buffer = textView.TextDataModel.DocumentBuffer;
            ViewCount count = buffer.Properties.GetOrCreateSingletonProperty(typeof(ViewCount), () => new ViewCount());
            count.Value++;
            SqlFluffPackage.Instance?.LintService?.BufferOpened(buffer);

            textView.Closed += (sender, args) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                count.Value--;
                if (count.Value <= 0)
                {
                    count.Value = 0;
                    if (SqlBufferHeuristics.IsLikelySql(buffer, Documents))
                    {
                        SqlFluffPackage.Instance?.LintService?.BufferClosed(buffer);
                    }
                }
            };
        }
    }
}
