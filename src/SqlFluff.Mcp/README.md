# SqlFluff.Mcp

An MCP (Model Context Protocol) server, exposed over stdio, that gives an AI coding
assistant access to the same sqlfluff lint/fix/format pipeline the SSMS extension uses -
so SQL an assistant writes or is about to insert can be run through the project's `.sqlfluff`
config first, the same way a human would hit Format after typing it.

This is a separate, standalone tool. It doesn't require the VSIX to be installed, doesn't
talk to SSMS or any open editor, and doesn't get installed by the VSIX - it only needs
`sqlfluff` itself on the PATH (`pip install sqlfluff`), same as the extension does.

## Tools

- `sqlfluff_lint(sql, dialect?, filePath?, workingDirectory?, configFile?, executablePath?)`
  - returns `{ clean, violations[] }`.
- `sqlfluff_fix(sql, dialect?, filePath?, workingDirectory?, configFile?, executablePath?)`
  - returns `{ sql, changed }`.
- `sqlfluff_format(sql, dialect?, filePath?, workingDirectory?, configFile?, executablePath?)`
  - returns `{ sql, changed }`.

`filePath` / `workingDirectory` let sqlfluff discover the right `.sqlfluff` config the same
way the extension does (walking up from the file's or project's directory). Pass
`workingDirectory` (your project root) when the SQL doesn't correspond to a saved file yet -
the usual case for AI-generated SQL. `configFile` is a fallback used only when no `.sqlfluff`
is discovered. `dialect` defaults to `tsql`; `executablePath` is only needed if `sqlfluff`
isn't on this process's PATH.

## Build

```powershell
dotnet build src\SqlFluff.Mcp\SqlFluff.Mcp.csproj --configuration Release
```

Output: `src\SqlFluff.Mcp\bin\Release\net8.0\SqlFluff.Mcp.dll`.

## Configuring an MCP client

Point your MCP-capable client (e.g. an AI assistant integrated into SSMS or VS) at the
built DLL, run with `dotnet`:

```json
{
  "command": "dotnet",
  "args": ["C:\\path\\to\\SqlFluff.Mcp.dll"]
}
```

The exact place this config goes depends on the client - consult its own MCP server
documentation. There's no installer for this piece; it's just a `dotnet`-run executable.
