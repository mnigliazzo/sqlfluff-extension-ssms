using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using SqlFluff.Ssms.Core;
using StreamJsonRpc;

namespace SqlFluff.Ssms.NewModel
{
    // Minimal Language Server Protocol server: full-text document sync in, sqlfluff lint
    // diagnostics out. Runs inside the extension's own process, on one end of an in-memory pipe.
    internal sealed class SqlFluffLanguageServer
    {
        private const int TextDocumentSyncFull = 1;
        private const int SeverityError = 1;
        private const int SeverityWarning = 2;

        private readonly JsonRpc _rpc;
        private readonly TraceSource _trace;
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _pendingLints = new ConcurrentDictionary<string, CancellationTokenSource>();

        public SqlFluffLanguageServer(Stream stream, TraceSource trace)
        {
            _trace = trace;
            _rpc = new JsonRpc(new HeaderDelimitedMessageHandler(stream, stream));
            _rpc.AddLocalRpcTarget(this, new JsonRpcTargetOptions { UseSingleObjectParameterDeserialization = true });
        }

        public void Start() => _rpc.StartListening();

        [JsonRpcMethod("initialize")]
        public object Initialize(JToken parameters) => new
        {
            capabilities = new { textDocumentSync = TextDocumentSyncFull },
            serverInfo = new { name = "SQLFluff" },
        };

        [JsonRpcMethod("initialized")]
        public void Initialized(JToken parameters)
        {
        }

        [JsonRpcMethod("shutdown")]
        public object Shutdown() => null;

        [JsonRpcMethod("exit")]
        public void Exit() => _rpc.Dispose();

        [JsonRpcMethod("textDocument/didOpen")]
        public void DidOpen(JToken parameters)
        {
            JToken document = parameters["textDocument"];
            ScheduleLint((string)document["uri"], (string)document["text"]);
        }

        [JsonRpcMethod("textDocument/didChange")]
        public void DidChange(JToken parameters)
        {
            string text = (string)parameters["contentChanges"]?.LastOrDefault()?["text"];
            if (text != null)
            {
                ScheduleLint((string)parameters["textDocument"]["uri"], text);
            }
        }

        [JsonRpcMethod("textDocument/didClose")]
        public void DidClose(JToken parameters)
        {
            string uri = (string)parameters["textDocument"]["uri"];
            if (_pendingLints.TryRemove(uri, out CancellationTokenSource pending))
            {
                pending.Cancel();
            }

            _ = PublishAsync(uri, new List<LintViolation>());
        }

        private void ScheduleLint(string uri, string text)
        {
            var cts = new CancellationTokenSource();
            CancellationTokenSource previous = null;
            _pendingLints.AddOrUpdate(uri, cts, (_, old) => { previous = old; return cts; });
            previous?.Cancel();
            _ = LintAsync(uri, text, cts.Token);
        }

        private async Task LintAsync(string uri, string text, CancellationToken cancellationToken)
        {
            try
            {
                // Debounce: a burst of keystrokes only lints once typing pauses.
                await Task.Delay(750, cancellationToken).ConfigureAwait(false);

                string path = PathFromUri(uri);
                var settings = new SqlFluffSettings
                {
                    ExecutablePath = "sqlfluff",
                    Dialect = "tsql",
                    ConfigFile = SqlFluffConfigResolver.Resolve(path, null, null),
                    TimeoutSeconds = 60,
                };

                IReadOnlyList<LintViolation> violations = string.IsNullOrWhiteSpace(text)
                    ? new List<LintViolation>()
                    : await SqlFluffRunner.LintAsync(text, path, settings, cancellationToken).ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();
                await PublishAsync(uri, violations).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _trace.TraceEvent(TraceEventType.Warning, 0, "SQLFluff lint failed for " + uri + ": " + ex.Message);
            }
        }

        private Task PublishAsync(string uri, IReadOnlyList<LintViolation> violations)
        {
            var diagnostics = violations.Select(v => new
            {
                range = new
                {
                    start = new { line = Math.Max(0, v.StartLine - 1), character = Math.Max(0, v.StartColumn - 1) },
                    end = new
                    {
                        line = Math.Max(0, (v.EndLine > 0 ? v.EndLine : v.StartLine) - 1),
                        character = Math.Max(0, (v.EndLine > 0 ? v.EndColumn : v.StartColumn) - 1),
                    },
                },
                severity = v.IsParseError ? SeverityError : SeverityWarning,
                code = v.Code,
                source = "sqlfluff",
                message = string.IsNullOrEmpty(v.Name) ? v.Description : v.Description + " (" + v.Name + ")",
            }).ToArray();

            return _rpc.NotifyWithParameterObjectAsync("textDocument/publishDiagnostics", new { uri, diagnostics });
        }

        private static string PathFromUri(string uri)
        {
            return Uri.TryCreate(uri, UriKind.Absolute, out Uri parsed) && parsed.IsFile ? parsed.LocalPath : uri;
        }
    }
}
