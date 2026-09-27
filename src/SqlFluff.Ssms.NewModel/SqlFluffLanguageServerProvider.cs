using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Editor;
using Microsoft.VisualStudio.Extensibility.LanguageServer;
using Microsoft.VisualStudio.RpcContracts.LanguageServerProvider;
using Nerdbank.Streams;

namespace SqlFluff.Ssms.NewModel
{
    // Activates for .sql documents and hands SSMS a pipe to an in-process LSP server (see
    // SqlFluffLanguageServer). Diagnostics then flow through SSMS's own LSP client into squiggles and
    // the Error List - no tagger or Error List code in this extension.
    [VisualStudioContribution]
    internal sealed class SqlFluffLanguageServerProvider : LanguageServerProvider
    {
        private readonly TraceSource _trace;

        public SqlFluffLanguageServerProvider(ExtensionCore container, VisualStudioExtensibility extensibilityObject, TraceSource traceSource)
            : base(container, extensibilityObject)
        {
            _trace = traceSource;
        }

        [VisualStudioContribution]
        internal static DocumentTypeConfiguration SqlDocumentType => new DocumentTypeConfiguration("sqlfluff-sql")
        {
            FileExtensions = new[] { ".sql" },
            BaseDocumentType = LanguageServerBaseDocumentType,
        };

        public override LanguageServerProviderConfiguration LanguageServerProviderConfiguration => new LanguageServerProviderConfiguration(
            "%SqlFluff.NewModel.LanguageServer.DisplayName%",
            new[] { DocumentFilter.FromDocumentType(SqlDocumentType) });

        public override Task<IDuplexPipe> CreateServerConnectionAsync(CancellationToken cancellationToken)
        {
            (Stream toServer, Stream toClient) = FullDuplexStream.CreatePair();
            var server = new SqlFluffLanguageServer(toServer, _trace);
            server.Start();
            return Task.FromResult<IDuplexPipe>(new DuplexPipe(toClient.UsePipeReader(), toClient.UsePipeWriter()));
        }

        public override Task OnServerInitializationResultAsync(ServerInitializationResult serverInitializationResult, LanguageServerInitializationFailureInfo initializationFailureInfo, CancellationToken cancellationToken)
        {
            if (serverInitializationResult == ServerInitializationResult.Failed)
            {
                _trace.TraceEvent(TraceEventType.Error, 0, "SQLFluff language server failed to start: " + initializationFailureInfo?.StatusMessage);
                Enabled = false;
            }

            return Task.CompletedTask;
        }
    }
}
