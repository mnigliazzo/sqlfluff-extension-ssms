using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SqlFluff.Ssms.Core;

[assembly: InternalsVisibleTo("SqlFluff.Mcp.Tests")]

namespace SqlFluff.Mcp
{
    [McpServerToolType]
    public static class SqlFluffTools
    {
        private const string DialectDescription =
            "sqlfluff dialect (e.g. tsql, postgres, snowflake). Defaults to tsql if omitted.";

        private const string FilePathDescription =
            "Absolute path of the file this SQL came from or would be saved as. Used so sqlfluff can " +
            "infer file type from its extension and discover the nearest .sqlfluff config walking up " +
            "from its directory - the file itself is never read from disk, and it doesn't need to exist " +
            "yet as long as its directory does. Omit if there's no real or intended file path at all.";

        private const string WorkingDirectoryDescription =
            "Absolute path of the project/workspace root, used as the starting point for .sqlfluff " +
            "config discovery when filePath is omitted or its directory doesn't exist either - the " +
            "more reliable hint for AI-generated SQL with no real file path at all.";

        private const string ConfigFileDescription =
            "Absolute path to a .sqlfluff config file to use as a fallback. A .sqlfluff discovered near " +
            "filePath or workingDirectory always takes precedence over this.";

        private const string ExecutablePathDescription =
            "Path or command name of the sqlfluff executable, if it isn't on this process's PATH (e.g. " +
            "'C:\\Python312\\Scripts\\sqlfluff.exe'). Defaults to 'sqlfluff'.";

        [McpServerTool(Name = "sqlfluff_lint")]
        [Description("Lints SQL text with sqlfluff and returns the violations found, exactly as 'sqlfluff lint' would report them.")]
        public static async Task<LintToolResult> Lint(
            [Description("The SQL text to lint.")] string sql,
            [Description(DialectDescription)] string dialect = null,
            [Description(FilePathDescription)] string filePath = null,
            [Description(WorkingDirectoryDescription)] string workingDirectory = null,
            [Description(ConfigFileDescription)] string configFile = null,
            [Description(ExecutablePathDescription)] string executablePath = null,
            CancellationToken cancellationToken = default)
        {
            ValidateInputs(sql, filePath, workingDirectory, configFile);

            SqlFluffSettings settings = BuildSettings(dialect, filePath, workingDirectory, configFile, executablePath);
            string effectiveFilePath = ResolveFilePathHint(filePath, workingDirectory);

            IReadOnlyList<LintViolation> violations =
                await SqlFluffRunner.LintAsync(sql, effectiveFilePath, settings, cancellationToken).ConfigureAwait(false);

            return new LintToolResult
            {
                Clean = violations.Count == 0,
                Violations = violations
                    .Select(v => new LintViolationDto
                    {
                        Code = v.Code,
                        Name = v.Name,
                        Description = v.Description,
                        StartLine = v.StartLine,
                        StartColumn = v.StartColumn,
                        EndLine = v.EndLine,
                        EndColumn = v.EndColumn,
                        IsFixable = v.IsFixable,
                        Message = v.Message,
                    })
                    .ToList(),
            };
        }

        [McpServerTool(Name = "sqlfluff_fix")]
        [Description("Applies all of sqlfluff's fixable rules to SQL text and returns the rewritten SQL, exactly as 'sqlfluff fix' would.")]
        public static Task<RewriteToolResult> Fix(
            [Description("The SQL text to fix.")] string sql,
            [Description(DialectDescription)] string dialect = null,
            [Description(FilePathDescription)] string filePath = null,
            [Description(WorkingDirectoryDescription)] string workingDirectory = null,
            [Description(ConfigFileDescription)] string configFile = null,
            [Description(ExecutablePathDescription)] string executablePath = null,
            CancellationToken cancellationToken = default)
            => Rewrite(SqlFluffRunner.FixAsync, sql, dialect, filePath, workingDirectory, configFile, executablePath, cancellationToken);

        [McpServerTool(Name = "sqlfluff_format")]
        [Description("Applies only sqlfluff's safe, stable formatting subset to SQL text and returns the rewritten SQL, exactly as 'sqlfluff format' would.")]
        public static Task<RewriteToolResult> Format(
            [Description("The SQL text to format.")] string sql,
            [Description(DialectDescription)] string dialect = null,
            [Description(FilePathDescription)] string filePath = null,
            [Description(WorkingDirectoryDescription)] string workingDirectory = null,
            [Description(ConfigFileDescription)] string configFile = null,
            [Description(ExecutablePathDescription)] string executablePath = null,
            CancellationToken cancellationToken = default)
            => Rewrite(SqlFluffRunner.FormatAsync, sql, dialect, filePath, workingDirectory, configFile, executablePath, cancellationToken);

        private static async Task<RewriteToolResult> Rewrite(
            Func<string, string, SqlFluffSettings, CancellationToken, Task<string>> runnerMethod,
            string sql, string dialect, string filePath, string workingDirectory, string configFile, string executablePath,
            CancellationToken ct)
        {
            ValidateInputs(sql, filePath, workingDirectory, configFile);

            SqlFluffSettings settings = BuildSettings(dialect, filePath, workingDirectory, configFile, executablePath);
            string effectiveFilePath = ResolveFilePathHint(filePath, workingDirectory);

            string rewritten = await runnerMethod(sql, effectiveFilePath, settings, ct).ConfigureAwait(false);

            // sqlfluff always emits LF-only output regardless of the input's line endings. Comparing
            // that raw output straight against `sql` (as LintService.cs's VSIX equivalent explicitly
            // avoids doing - see its DominantNewline/Replace calls) would report Changed=true for any
            // CRLF-terminated input purely from the line-ending difference, and silently hand back SQL
            // in a different line-ending convention than the caller sent.
            string newline = DominantNewline(sql);
            string normalized = rewritten.Replace("\r\n", "\n").Replace("\n", newline);

            return new RewriteToolResult
            {
                Sql = normalized,
                Changed = !string.Equals(normalized, sql, StringComparison.Ordinal),
            };
        }

        internal static string DominantNewline(string text)
        {
            int index = text.IndexOf('\n');
            if (index < 0)
            {
                return "\n";
            }

            return index > 0 && text[index - 1] == '\r' ? "\r\n" : "\n";
        }

        // Throwing McpException (rather than a plain ArgumentException) is what makes the SDK
        // surface this message to the caller - any other exception type gets replaced with a
        // generic "An error occurred invoking '<tool>'." to avoid leaking internal details.
        private static void ValidateInputs(string sql, string filePath, string workingDirectory, string configFile)
        {
            if (string.IsNullOrEmpty(sql))
            {
                throw new McpException("'sql' is required and cannot be empty.");
            }

            RequireAbsoluteIfGiven(filePath, nameof(filePath));
            RequireAbsoluteIfGiven(workingDirectory, nameof(workingDirectory));
            RequireAbsoluteIfGiven(configFile, nameof(configFile));
        }

        // filePath/workingDirectory/configFile drive .sqlfluff config discovery (see BuildSettings
        // below); resolving a relative one against this server process's own working directory -
        // which has no defined relationship to the caller's project - could silently discover the
        // wrong .sqlfluff and diverge from what the same project's sqlfluff CLI/CI would report.
        // Rejecting relative paths outright is safer than guessing.
        //
        // Path.IsPathRooted is not enough here: on Windows it returns true for a drive-relative path
        // like "C:folder\file.sql" (no separator after the drive letter), which .NET then resolves
        // against that drive's *current directory*, not the drive root - exactly the kind of
        // silent-wrong-.sqlfluff resolution this check exists to prevent. IsPathFullyQualified
        // correctly rejects that case.
        internal static void RequireAbsoluteIfGiven(string path, string paramName)
        {
            if (!string.IsNullOrWhiteSpace(path) && !Path.IsPathFullyQualified(path))
            {
                throw new McpException("'" + paramName + "' must be an absolute path, got: " + path);
            }
        }

        // Mirrors LintService.ResolveEffectiveSettings: a .sqlfluff discovered from filePath's or
        // workingDirectory's directory always wins over the explicit configFile fallback, per
        // SqlFluffConfigResolver's documented precedence.
        internal static SqlFluffSettings BuildSettings(
            string dialect, string filePath, string workingDirectory, string configFile, string executablePath)
        {
            return new SqlFluffSettings
            {
                ExecutablePath = string.IsNullOrWhiteSpace(executablePath) ? "sqlfluff" : executablePath,
                Dialect = string.IsNullOrWhiteSpace(dialect) ? "tsql" : dialect,
                ConfigFile = SqlFluffConfigResolver.Resolve(filePath, EffectiveWorkingDirectory(filePath, workingDirectory), configFile),
                TimeoutSeconds = 60,
            };
        }

        // SqlFluffConfigResolver only walks up from filePath's directory when the file itself
        // exists on disk (see its own doc comment - that's the right call for the VSIX, where an
        // unsaved-new document's path is a meaningless placeholder). For AI-generated SQL that
        // hasn't been saved yet, filePath is commonly a real, intended path under a real project
        // directory that just doesn't have a file there yet - its directory is exactly as good a
        // starting point for discovery as an explicit workingDirectory, so use it as a fallback
        // openFolderPath instead of leaving discovery silently empty (matching what FilePathDescription
        // above promises callers).
        internal static string EffectiveWorkingDirectory(string filePath, string workingDirectory)
        {
            if (!string.IsNullOrWhiteSpace(workingDirectory) || string.IsNullOrWhiteSpace(filePath) || File.Exists(filePath))
            {
                return workingDirectory;
            }

            try
            {
                return Path.GetDirectoryName(Path.GetFullPath(filePath));
            }
            catch (Exception ex) when (ex is ArgumentException || ex is PathTooLongException || ex is NotSupportedException)
            {
                return workingDirectory;
            }
        }

        // SqlFluffRunner.PickWorkingDirectory requires a real, existing directory to walk up from
        // for .sqlfluffignore/.sqlfluff discovery. If the caller only gave a project root (the
        // common case for AI-generated SQL that isn't saved anywhere yet), synthesize a
        // placeholder path under it so that directory is still what gets used.
        internal static string ResolveFilePathHint(string filePath, string workingDirectory)
        {
            if (!string.IsNullOrWhiteSpace(filePath))
            {
                return filePath;
            }

            if (!string.IsNullOrWhiteSpace(workingDirectory))
            {
                return Path.Combine(workingDirectory, "ai-generated.sql");
            }

            return "ai-generated.sql";
        }
    }

    public sealed class LintToolResult
    {
        public bool Clean { get; set; }
        public List<LintViolationDto> Violations { get; set; }
    }

    public sealed class LintViolationDto
    {
        public string Code { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public int StartLine { get; set; }
        public int StartColumn { get; set; }
        public int EndLine { get; set; }
        public int EndColumn { get; set; }
        public bool IsFixable { get; set; }
        public string Message { get; set; }
    }

    public sealed class RewriteToolResult
    {
        public string Sql { get; set; }
        public bool Changed { get; set; }
    }
}
