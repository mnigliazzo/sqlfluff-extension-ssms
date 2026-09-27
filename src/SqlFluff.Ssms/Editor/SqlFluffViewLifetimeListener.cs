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
            ITextBuffer buffer = textView.TextDataModel.DocumentBuffer;
            if (!SqlBufferHeuristics.IsLikelySql(buffer, Documents))
            {
                return;
            }

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
                    SqlFluffPackage.Instance?.LintService?.BufferClosed(buffer);
                }
            };
        }
    }
}
