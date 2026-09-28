# Architecture

## Execution model: everything goes through stdin/stdout

The extension **never reads or writes the SQL file on disk** for linting/fixing. `LintService` pulls the current text straight from the VS editor's `ITextBuffer.CurrentSnapshot` (so it works on unsaved changes, unsaved-new documents, whatever's in memory) and pipes it to `sqlfluff lint|fix|format --stdin-filename <path> -` via `SqlFluffRunner` (`Core/SqlFluffRunner.cs`), which manages the child `Process`, writes UTF-8 bytes to `StandardInput`, and parses either the JSON lint output or the rewritten SQL text from `StandardOutput`. `--stdin-filename` is only used by sqlfluff to infer file type/config location, not to actually read that path.

`SqlFluffRunner` also handles executable discovery (PATH → common Python `Scripts` directories → `py -m sqlfluff` fallback), caches the resolved launch command, and exposes `CheckAvailabilityAsync` (used once at package startup to warn early if sqlfluff isn't reachable, rather than waiting for the first Lint/Fix to fail).

## Diagnostics flow: one shared store, multiple consumers

`Editor/ViolationStore.cs` is a `ConditionalWeakTable<ITextBuffer, …>`-backed store keyed by text buffer — the single source of truth for "what did sqlfluff find in this buffer, and where (as a `Span` on a specific `ITextSnapshot`)". Three independent consumers read from it and get notified of changes via a per-buffer subscription (not a static event, to avoid pinning buffers in memory):

1. `Editor/SqlFluffTaggerProvider.cs` — `ITagger<IErrorTag>` implementation that turns violations into editor squiggles.
2. `Services/ErrorListService.cs` — an `ITableDataSource` on the Error List table (the model VS/SSMS language services use, not the legacy `ErrorListProvider`/`ErrorTask` API). Each open buffer owns one snapshot factory, replaced on re-lint and removed on close — ownership is by buffer, never by path, because unsaved queries have no reliable path and path-keyed entries let one document's clear wipe or miss another's. Only folder-wide Lint results for files that aren't open are owned by path. Row clicks go through `Services/ErrorListNavigation.cs` (an `ITableControlEventProcessorProvider`), since the Error List's default navigation opens `DocumentName` from disk, which fails for unsaved queries.
3. `Editor/SqlFluffSuggestedActionsSource.cs` — `ISuggestedActionsSourceProvider`/`ISuggestedAction` implementation that surfaces "Fix with SQLFluff" / "Format with SQLFluff" in the native Light Bulb (`Alt+.`) menu when the cursor is on a violation span.

`LintService.LintAsync` is what populates the store (via `SqlFluffRunner.LintAsync` + `ViolationStore.ToSpan` to map sqlfluff's 1-based line/column positions onto a snapshot `Span`), and `LintService.Clear` / `ClearDiagnostics` empties it.

Closing a document is detected by `Editor/SqlFluffViewLifetimeListener.cs` (last editor view on a buffer closed → `LintService.BufferClosed`), not only by the RDT's `OnBeforeLastDocumentUnlock`: SSMS's query window doesn't reliably bring RDT lock counts to 0 when a tab closes, which is how an unsaved query closed with "Don't Save" used to leave its violations in the Error List. Only the view listener marks a buffer closed (which also drops any lint that finishes afterwards); the RDT path just clears, since its lock counts can reach 0 while the tab is still open.

## Fix and Format are the same code path with a different verb

`LintService.RewriteAsync` (private, called by the public `FixAsync`/`FormatAsync`) is shared logic parameterized by a `RewriteMode` enum. The only difference between them is which `SqlFluffRunner` method they call (`FixAsync` → `sqlfluff fix`, all fixable rules; `FormatAsync` → `sqlfluff format`, a stable safe subset) — everything else (selection-vs-document targeting, diffing old/new text, applying a minimal edit, re-linting afterward) is identical. When adding a third rewrite-style command, extend this enum/method rather than duplicating the flow.

`ApplyMinimalEdit` (bottom of `LintService.cs`) computes the common prefix/suffix between old and new text and replaces only the differing middle, so the editor's caret position, scroll position, and undo stack stay sane instead of a full-buffer replace.

Fix/Format apply changes to the in-memory buffer only — they do **not** save the document. The status bar prompts the user to save manually. (An opt-in auto-save setting was attempted once and reverted for complexity; see issue #2 in the tracker if picking that back up.)

## Package wiring and cross-component access

`SqlFluffPackage.cs` is the `AsyncPackage` entry point. It owns the long-lived service instances (`LintService`, `ErrorListService`, `EditorServices`) and wires up:

- Menu/toolbar/context-menu commands (defined in `SqlFluffPackage.vsct`, IDs in `PackageGuids.cs`) via `OleMenuCommandService`.
- `Services/DocumentEvents.cs`, an `IVsRunningDocTableEvents3` implementation that triggers lint-on-save and drives the lint-while-typing debounce (via `LintService.Schedule`), and cleans up Error List entries when a document's last lock is released (i.e., it's closed). There is no lint-on-open — it was removed as unreliable/unwanted; `DocumentEvents` still tracks each buffer on first show, but only to subscribe the lint-while-typing handler.

Because `Editor/SqlFluffSuggestedActionsSource.cs` is composed by MEF (no constructor injection available), it reaches the package's services through `SqlFluffPackage.Instance`, a static singleton set in `InitializeAsync` / cleared in `Dispose`. This is the one deliberate exception to normal DI in this codebase — needed because MEF components and `AsyncPackage` are wired up through entirely separate mechanisms in the VS extensibility model.

## Extension self-update

The extension itself (not SQLFluff) can update in-place from SSMS, since it has no VS Marketplace listing to drive auto-update the normal way — it's only ever distributed as a `.vsix` attached to a GitHub Release (see [docs/RELEASE.md](RELEASE.md)). `Core/UpdateInfoParser.cs` is pure logic (parses the JSON from GitHub's `GET /repos/.../releases/latest`, compares versions) and is unit tested; `Core/ExtensionUpdater.cs` does the actual `HttpClient` fetch/download and launches the downloaded `.vsix` via `Process.Start(UseShellExecute: true)` — i.e. whatever's registered to open a `.vsix` (VSIXInstaller, normally), identical to a user double-clicking a manually downloaded one. `SqlFluffPackage.CheckForUpdatesAsync` is the only caller and has two modes: a silent startup check (gated by the "Check for updates on startup" option, default on) that just logs/sets status if a newer release exists, and the explicit **SQLFluff > Check for Updates...** command, which additionally prompts and, on confirmation, downloads and launches the installer. Neither path ever downloads or installs without an explicit user "yes" to that prompt.

## Notifications and prompts

Anything the user didn't ask for — the startup checks' "SQLFluff tool not found" and "tool update available" — goes through `Services/InfoBarService.cs`: a non-blocking info bar in the main window (`IVsInfoBarUIFactory`), the Visual Studio pattern for unsolicited notifications. Modal message boxes are only used to confirm an action the user explicitly invoked (e.g. SQLFluff > Install/Update SQLFluff Tool..., Check for Updates..., the folder-wide commands).

## Menus and toolbar

Commands, menus and the SQLFluff toolbar are declared in `SqlFluffPackage.vsct`. **Bump the version in `[ProvideMenuResource("Menus.ctmenu", N)]` whenever the `.vsct` changes**: Visual Studio only re-merges an extension's command UI (including showing a new `DefaultDocked` toolbar or new buttons) when that number changes. It stayed at 1 for a long time, which is why new buttons didn't appear after upgrading. The bump isn't enough to *show* the toolbar, though: SSMS 22 ignores `DefaultDocked`, so `SqlFluffPackage.EnsureToolbarVisibleOnce` forces it visible through `EnvDTE`/`CommandBars` once per extension version (tracked as `ToolbarShownForVersion` in the user settings store), so a user who hides it isn't overridden on every startup. Removing that code in #76 left the toolbar hidden for everyone (#89). A `.claude/hooks/check-vsct-version.ps1` hook now warns automatically if a `.vsct` edit doesn't bump this number — see `CLAUDE.md`'s "Active hooks and skills" section.

## Settings

`Core/SqlFluffSettings.cs` is a plain POCO snapshot of user options — a fresh instance is read at the start of each lint/fix/format operation rather than passed around as mutable global state.

- **Where options live:** Unified Settings (Tools > Options > SQLFluff), not a `DialogPage`. `Options/registration.json` declares them, and `[ProvideSettingsManifest]` registers it. `Core/SettingsSchema.cs` is the pure description of every option: moniker, the legacy `DialogPage` property name, type, default, and how it maps onto `SqlFluffSettings`. `SettingsSchemaTests` fails if it drifts from `registration.json`.
- **How they're read:** `Options/ExtensionOptions.cs` gets Unified Settings' `ISettingsManager` directly from the `SVsUnifiedSettingsManager` service. That service is documented, but its type lives in an internal interop assembly, so it's re-declared by GUID. Asking for it directly, instead of via a `[ProvideSettingsObserver]` observer, keeps reads synchronous from the first line of `InitializeAsync` — "Lint on open" runs for restored tabs during package load.
- **Upgrading from the `DialogPage` era:** the first run copies non-default values from the old storage (`DialogPage\SqlFluff.Ssms.Options.SqlFluffOptionsPage` in the user settings store, every value an invariant string) over once, in code (`MigrateLegacyOnce`), not via registration.json's `migration` blocks, which SSMS only uses on registry values that aren't strings. After that the old storage is never read. If Unified Settings is unavailable or turned off ("classic mode"), the extension uses defaults and says so in the output pane; there is no legacy options page to fall back to.
- **Opening the page:** SQLFluff > Options invokes the standard `Tools.Options` command with the category moniker `"sqlfluff"` as its argument. Unified Settings' handler for that command treats a non-GUID argument as a setting/category moniker and scrolls to it (verified by decompiling SSMS 22's `ToolsOptionsCommand`; an unknown moniker is only logged). This is the moniker counterpart of the page GUID that `Package.ShowOptionPage` passes for a `DialogPage`.

## Editor/services split

- `Core/` — SQLFluff process execution and settings model; no VS editor types, could in principle be unit tested standalone (though nothing currently does).
- `Editor/` — MEF-composed editor extensibility points (tagger, suggested actions, the violation store they both read).
- `Services/` — package-owned, non-MEF services (`LintService` orchestration, RDT/document-lifecycle glue, Error List, output pane/status bar logging via `OutputLog`, and `EditorServices` for locating the active SQL view / resolving a buffer's file path).

## Why VSSDK and not VisualStudio.Extensibility

This is an in-process VSSDK extension on purpose. The newer out-of-process **VisualStudio.Extensibility** model can't be installed into SSMS. That was verified on SSMS 22.10 with a probe extension (PR #80, reverted) and by decompiling the Visual Studio Installer 4.9.50, which is what installs new-model extensions:

- `ExtensionService.ProductSupportsExtension` checks the product against a hardcoded map, `ExtensionsHelper.productIdToTargetMapping`. It covers Community/Professional/Enterprise/SQL/TeamExplorer/WDExpress only, not `Microsoft.VisualStudio.Product.Ssms`.
- So every such VSIX fails with `UnsupportedProduct` before its manifest's install targets are considered.

The VSSDK-compatible in-process hybrid does install, but it's still VSSDK underneath and was rejected in favor of keeping one model. Re-check the installer's mapping before revisiting this.
