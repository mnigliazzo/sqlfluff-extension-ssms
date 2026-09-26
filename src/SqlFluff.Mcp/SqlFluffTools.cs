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
        [McpServerTool(Name = "sqlfluff_lint")]
        [Description("Lints SQL text with sqlfluff and returns the violations found, exactly as 'sqlfluff lint' would report them.")]
        public static async Task<LintToolResult> Lint(
            [Description("The SQL text to lint.")] string sql,
            [Description("sqlfluff dialect (e.g. tsql, postgres, snowflake). Defaults to tsql if omitted.")] string dialect = null,
            [Description("Absolute path of the file this SQL came from or would be saved as. Used only so sqlfluff can infer file type and discover the nearest .sqlfluff config walking up from its directory - the file itself is never read from disk. Omit if the SQL doesn't correspond to a real file yet.")] string filePath = null,
            [Description("Absolute path of the project/workspace root. Used to discover a .sqlfluff config by walking upward from here when filePath doesn't exist on disk yet - the more reliable hint for AI-generated SQL that hasn't been saved.")] string workingDirectory = null,
            [Description("Absolute path to a .sqlfluff config file to use as a fallback. A .sqlfluff discovered near filePath or workingDirectory always takes precedence over this.")] string configFile = null,
            [Description("Path or command name of the sqlfluff executable, if it isn't on this process's PATH (e.g. 'C:\\Python312\\Scripts\\sqlfluff.exe'). Defaults to 'sqlfluff'.")] string executablePath = null,
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
            [Description("sqlfluff dialect (e.g. tsql, postgres, snowflake). Defaults to tsql if omitted.")] string dialect = null,
            [Description("Absolute path of the file this SQL came from or would be saved as. Used only so sqlfluff can infer file type and discover the nearest .sqlfluff config walking up from its directory - the file itself is never read from disk. Omit if the SQL doesn't correspond to a real file yet.")] string filePath = null,
            [Description("Absolute path of the project/workspace root. Used to discover a .sqlfluff config by walking upward from here when filePath doesn't exist on disk yet - the more reliable hint for AI-generated SQL that hasn't been saved.")] string workingDirectory = null,
            [Description("Absolute path to a .sqlfluff config file to use as a fallback. A .sqlfluff discovered near filePath or workingDirectory always takes precedence over this.")] string configFile = null,
            [Description("Path or command name of the sqlfluff executable, if it isn't on this process's PATH (e.g. 'C:\\Python312\\Scripts\\sqlfluff.exe'). Defaults to 'sqlfluff'.")] string executablePath = null,
            CancellationToken cancellationToken = default)
            => Rewrite(SqlFluffRunner.FixAsync, sql, dialect, filePath, workingDirectory, configFile, executablePath, cancellationToken);

        [McpServerTool(Name = "sqlfluff_format")]
        [Description("Applies only sqlfluff's safe, stable formatting subset to SQL text and returns the rewritten SQL, exactly as 'sqlfluff format' would.")]
        public static Task<RewriteToolResult> Format(
            [Description("The SQL text to format.")] string sql,
            [Description("sqlfluff dialect (e.g. tsql, postgres, snowflake). Defaults to tsql if omitted.")] string dialect = null,
            [Description("Absolute path of the file this SQL came from or would be saved as. Used only so sqlfluff can infer file type and discover the nearest .sqlfluff config walking up from its directory - the file itself is never read from disk. Omit if the SQL doesn't correspond to a real file yet.")] string filePath = null,
            [Description("Absolute path of the project/workspace root. Used to discover a .sqlfluff config by walking upward from here when filePath doesn't exist on disk yet - the more reliable hint for AI-generated SQL that hasn't been saved.")] string workingDirectory = null,
            [Description("Absolute path to a .sqlfluff config file to use as a fallback. A .sqlfluff discovered near filePath or workingDirectory always takes precedence over this.")] string configFile = null,
            [Description("Path or command name of the sqlfluff executable, if it isn't on this process's PATH (e.g. 'C:\\Python312\\Scripts\\sqlfluff.exe'). Defaults to 'sqlfluff'.")] string executablePath = null,
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

            return new RewriteToolResult
            {
                Sql = rewritten,
                Changed = rewritten != sql,
            };
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
        private static void RequireAbsoluteIfGiven(string path, string paramName)
        {
            if (!string.IsNullOrWhiteSpace(path) && !Path.IsPathRooted(path))
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
                ConfigFile = SqlFluffConfigResolver.Resolve(filePath, workingDirectory, configFile),
                TimeoutSeconds = 60,
            };
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
