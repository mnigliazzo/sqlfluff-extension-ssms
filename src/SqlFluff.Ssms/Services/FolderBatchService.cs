using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using SqlFluff.Ssms.Core;
using SqlFluff.Ssms.Editor;

namespace SqlFluff.Ssms.Services
{
    internal enum FolderBatchAction
    {
        Lint,
        Fix,
        Format,
    }

    internal sealed class FolderBatchSummary
    {
        public int TotalFiles { get; set; }
        public int Rewritten { get; set; }
        public int Unchanged { get; set; }
        public int Skipped { get; set; }
        public int Failed { get; set; }
        public int TotalViolations { get; set; }
        public bool WasCancelled { get; set; }
        public List<string> Details { get; } = new List<string>();
    }

    // Reported after each file so the caller can show live progress (see SqlFluffPackage.
    // RunFolderActionAsync).
    internal readonly struct FolderBatchProgress
    {
        public FolderBatchProgress(int completed, int total, string relativePath)
        {
            Completed = completed;
            Total = total;
            RelativePath = relativePath;
        }

        public int Completed { get; }
        public int Total { get; }
        public string RelativePath { get; }
    }

    // Drives Lint/Fix/Format across every .sql file under the open folder, not just open editor
    // buffers. Open, non-dirty files are rewritten through their existing ITextBuffer (reusing
    // LintService, so squiggles/Error List stay consistent with the single-document commands);
    // closed files are read and written straight off disk, since there's no buffer to route
    // through. Files open with unsaved changes are always skipped for Fix/Format, to avoid
    // clobbering edits the user hasn't saved yet.
    internal sealed class FolderBatchService
    {
        private readonly SqlFluffPackage _package;
        private readonly IVsRunningDocumentTable _rdt;
        private readonly EditorServices _editor;
        private readonly LintService _lint;
        private readonly ErrorListService _errors;

        public FolderBatchService(SqlFluffPackage package, IVsRunningDocumentTable rdt, EditorServices editor, LintService lint, ErrorListService errors)
        {
            _package = package;
            _rdt = rdt;
            _editor = editor;
            _lint = lint;
            _errors = errors;
        }

        public static IReadOnlyList<string> EnumerateSqlFiles(string root)
        {
            var results = new List<string>();
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            {
                return results;
            }

            var pending = new Stack<string>();
            pending.Push(root);

            while (pending.Count > 0)
            {
                string dir = pending.Pop();

                string[] files;
                try
                {
                    files = Directory.GetFiles(dir, "*.sql");
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException)
                {
                    files = Array.Empty<string>();
                }

                results.AddRange(files);

                string[] subdirs;
                try
                {
                    subdirs = Directory.GetDirectories(dir);
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException)
                {
                    subdirs = Array.Empty<string>();
                }

                foreach (string sub in subdirs)
                {
                    string name = Path.GetFileName(sub);
                    if (string.Equals(name, ".git", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(name, ".vs", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    pending.Push(sub);
                }
            }

            results.Sort(StringComparer.OrdinalIgnoreCase);
            return results;
        }

        public static string MakeRelativePath(string root, string file)
        {
            try
            {
                string normalizedRoot = root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                    ? root
                    : root + Path.DirectorySeparatorChar;
                var rootUri = new Uri(normalizedRoot);
                var fileUri = new Uri(file);
                return Uri.UnescapeDataString(rootUri.MakeRelativeUri(fileUri).ToString()).Replace('/', Path.DirectorySeparatorChar);
            }
            catch (Exception ex) when (ex is UriFormatException || ex is InvalidOperationException || ex is ArgumentException)
            {
                return file;
            }
        }

        public async Task<FolderBatchSummary> RunAsync(
            string folderRoot, IReadOnlyList<string> files, FolderBatchAction action,
            IProgress<FolderBatchProgress> progress, CancellationToken ct)
        {
            var summary = new FolderBatchSummary { TotalFiles = files.Count };

            for (int i = 0; i < files.Count; i++)
            {
                string file = files[i];

                if (ct.IsCancellationRequested)
                {
                    summary.WasCancelled = true;
                    break;
                }

                progress?.Report(new FolderBatchProgress(i, files.Count, MakeRelativePath(folderRoot, file)));

                try
                {
                    await ProcessFileAsync(summary, folderRoot, file, action, ct);
                }
                catch (OperationCanceledException)
                {
                    summary.WasCancelled = true;
                    break;
                }
                catch (Exception ex)
                {
                    summary.Failed++;
                    summary.Details.Add(MakeRelativePath(folderRoot, file) + ": FAILED - " + ex.Message);
                }
            }

            return summary;
        }

        private async Task ProcessFileAsync(FolderBatchSummary summary, string root, string file, FolderBatchAction action, CancellationToken ct)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);
            _editor.TryGetOpenBuffer(_rdt, file, out ITextBuffer buffer, out bool isDirty);

            if (action == FolderBatchAction.Lint)
            {
                if (buffer != null)
                {
                    await _lint.LintAsync(buffer, file, userInitiated: false);
                    ViolationSet set = ViolationStore.Get(buffer);
                    int count = set?.Entries.Count ?? 0;
                    summary.TotalViolations += count;
                    summary.Details.Add(MakeRelativePath(root, file) + ": " + count + " issue(s)");
                }
                else
                {
                    await LintClosedFileAsync(summary, root, file, ct);
                }

                return;
            }

            if (buffer != null)
            {
                if (isDirty)
                {
                    summary.Skipped++;
                    summary.Details.Add(MakeRelativePath(root, file) + ": skipped (unsaved changes)");
                    return;
                }

                LintService.BatchRewriteResult result =
                    await _lint.RewriteBufferForBatchAsync(buffer, file, useFix: action == FolderBatchAction.Fix);

                switch (result.Outcome)
                {
                    case LintService.BatchRewriteOutcome.Rewritten:
                        bool saved = await _lint.SaveDocumentAsync(file);
                        summary.Rewritten++;
                        summary.Details.Add(MakeRelativePath(root, file) + (saved ? ": fixed and saved" : ": fixed, but save failed"));
                        break;
                    case LintService.BatchRewriteOutcome.Unchanged:
                        summary.Unchanged++;
                        break;
                    default:
                        summary.Failed++;
                        summary.Details.Add(MakeRelativePath(root, file) + ": FAILED - " + result.Error);
                        break;
                }
            }
            else
            {
                await RewriteClosedFileAsync(summary, root, file, action, ct);
            }
        }

        private async Task LintClosedFileAsync(FolderBatchSummary summary, string root, string file, CancellationToken ct)
        {
            string text;
            try
            {
                if (!TryReadFile(file, out text, out _, out string encodingError))
                {
                    summary.Skipped++;
                    summary.Details.Add(MakeRelativePath(root, file) + ": skipped (" + encodingError + ")");
                    return;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                summary.Failed++;
                summary.Details.Add(MakeRelativePath(root, file) + ": FAILED to read - " + ex.Message);
                return;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                summary.Unchanged++;
                return;
            }

            SqlFluffSettings settings = await GetSettingsAsync(file);

            IReadOnlyList<LintViolation> violations;
            try
            {
                violations = await Task.Run(() => SqlFluffRunner.LintAsync(text, file, settings, ct), ct);
            }
            catch (SqlFluffException ex)
            {
                summary.Failed++;
                summary.Details.Add(MakeRelativePath(root, file) + ": FAILED - " + ex.Message);
                return;
            }

            summary.TotalViolations += violations.Count;
            summary.Details.Add(MakeRelativePath(root, file) + ": " + violations.Count + " issue(s)");

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);
            _errors.PublishRaw(file, violations, settings.Severity, v => NavigateToPosition(file, v.StartLine, v.StartColumn));
        }

        private async Task RewriteClosedFileAsync(FolderBatchSummary summary, string root, string file, FolderBatchAction action, CancellationToken ct)
        {
            string original;
            Encoding encoding;
            try
            {
                if (!TryReadFile(file, out original, out encoding, out string encodingError))
                {
                    summary.Skipped++;
                    summary.Details.Add(MakeRelativePath(root, file) + ": skipped (" + encodingError + ")");
                    return;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                summary.Failed++;
                summary.Details.Add(MakeRelativePath(root, file) + ": FAILED to read - " + ex.Message);
                return;
            }

            if (string.IsNullOrWhiteSpace(original))
            {
                summary.Unchanged++;
                return;
            }

            SqlFluffSettings settings = await GetSettingsAsync(file);

            string rewritten;
            try
            {
                rewritten = action == FolderBatchAction.Fix
                    ? await Task.Run(() => SqlFluffRunner.FixAsync(original, file, settings, ct), ct)
                    : await Task.Run(() => SqlFluffRunner.FormatAsync(original, file, settings, ct), ct);
            }
            catch (SqlFluffException ex)
            {
                summary.Failed++;
                summary.Details.Add(MakeRelativePath(root, file) + ": FAILED - " + ex.Message);
                return;
            }

            string newline = DetectDominantNewline(original);
            rewritten = rewritten.Replace("\r\n", "\n").Replace("\n", newline);

            if (string.Equals(rewritten, original, StringComparison.Ordinal))
            {
                summary.Unchanged++;
                return;
            }

            try
            {
                File.WriteAllText(file, rewritten, encoding);
                summary.Rewritten++;
                summary.Details.Add(MakeRelativePath(root, file) + ": fixed");
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                summary.Failed++;
                summary.Details.Add(MakeRelativePath(root, file) + ": FAILED to write - " + ex.Message);
            }
        }

        private async Task<SqlFluffSettings> GetSettingsAsync(string file)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            return _lint.ResolveEffectiveSettings(_package.GetSettings(), file);
        }

        private void NavigateToPosition(string path, int line, int column)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                VsShellUtilities.OpenDocument(_package, path, Guid.Empty, out _, out _, out IVsWindowFrame frame);
                frame?.Show();
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                OutputLog.Write("Could not open " + path + ": " + ex.Message);
                return;
            }

            if (!_editor.TryGetActiveSqlView(out IWpfTextView view, out ITextBuffer buffer, out _))
            {
                return;
            }

            ITextSnapshot snapshot = buffer.CurrentSnapshot;
            int lineNumber = Math.Max(0, Math.Min(line - 1, snapshot.LineCount - 1));
            ITextSnapshotLine textLine = snapshot.GetLineFromLineNumber(lineNumber);
            int offset = Math.Max(
                textLine.Start.Position,
                Math.Min(textLine.Start.Position + Math.Max(0, column - 1), textLine.End.Position));
            var point = new SnapshotPoint(snapshot, offset);

            view.Caret.MoveTo(point);
            view.ViewScroller.EnsureSpanVisible(new SnapshotSpan(point, 0), EnsureSpanVisibleOptions.AlwaysCenter);
            view.VisualElement.Focus();
        }

        // Reads a file for the folder-wide batch, detecting enough of its encoding to round-trip it
        // without corruption: a UTF-8 BOM, a UTF-16 LE/BE BOM (both preserved on write), or —
        // lacking any BOM — a strict UTF-8 decode (throwOnInvalidBytes), since that's what every
        // other path in this extension already assumes (SqlFluffRunner's PYTHONUTF8/
        // PYTHONIOENCODING handling). Anything else (e.g. a BOM-less ANSI/Windows-1252 file) can't
        // be told apart reliably from UTF-8 without full charset detection, so it's reported as
        // unsupported and left untouched rather than silently mis-decoded and corrupted on write.
        private static bool TryReadFile(string path, out string text, out Encoding encoding, out string error)
        {
            byte[] bytes = File.ReadAllBytes(path);

            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                encoding = new UTF8Encoding(true);
                text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
                error = null;
                return true;
            }

            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                encoding = Encoding.Unicode;
                text = encoding.GetString(bytes, 2, bytes.Length - 2);
                error = null;
                return true;
            }

            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                encoding = Encoding.BigEndianUnicode;
                text = encoding.GetString(bytes, 2, bytes.Length - 2);
                error = null;
                return true;
            }

            try
            {
                var strictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
                text = strictUtf8.GetString(bytes);
                encoding = new UTF8Encoding(false);
                error = null;
                return true;
            }
            catch (DecoderFallbackException)
            {
                text = null;
                encoding = null;
                error = "not valid UTF-8 and no byte-order mark found; save it as UTF-8 to include it";
                return false;
            }
        }

        private static string DetectDominantNewline(string text)
        {
            int index = text.IndexOfAny(new[] { '\r', '\n' });
            if (index < 0)
            {
                return "\r\n";
            }

            return text[index] == '\r' && index + 1 < text.Length && text[index + 1] == '\n'
                ? "\r\n"
                : text[index].ToString();
        }
    }
}
