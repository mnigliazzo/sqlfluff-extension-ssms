using System;
using System.IO;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;

namespace SqlFluff.Ssms.Editor
{
    // MEF components are exported against the generic "text" content type (SSMS's own SQL content
    // type name isn't a stable, documented value we can hardcode), so every editor extensibility
    // point we register gets asked to activate for buffers we don't want — plain text files, other
    // languages, etc. This is the shared runtime filter that keeps our tagger and Light Bulb source
    // scoped to what's actually SQL.
    internal static class SqlBufferHeuristics
    {
        public static bool IsLikelySql(ITextBuffer buffer, ITextDocumentFactoryService documents)
        {
            string path = documents != null && documents.TryGetTextDocument(buffer, out ITextDocument document)
                ? document.FilePath
                : null;

            if (!string.IsNullOrEmpty(path) && string.Equals(SafeExtension(path), ".sql", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return buffer.ContentType.TypeName.IndexOf("sql", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string SafeExtension(string path)
        {
            try
            {
                return Path.GetExtension(path);
            }
            catch (ArgumentException)
            {
                return string.Empty;
            }
        }
    }
}
