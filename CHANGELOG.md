# Changelog

Versions through **1.2.1** below were written by hand, one entry per PR. From the next release on, this file is no longer maintained manually — release notes are generated automatically from closed GitHub Issues assigned to each version's [milestone](../../milestones), grouped by label (`enhancement` → Added, `bug` → Fixed, `chore`/`documentation` → Changed), the same categories this file already used. See the [Releases page](../../releases) for the full history going forward, and [CONTRIBUTING.md](CONTRIBUTING.md) for how issues/milestones drive a release.

Versioning follows [Semantic Versioning](https://semver.org/). The categories below follow [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [1.2.1] - 2026-09-22

### Fixed

- The squiggle tagger and Light Bulb ("Fix with SQLFluff" / "Format with SQLFluff") were registered against the generic `text` MEF content type, since SSMS doesn't expose a documented, stable SQL-specific one to target precisely. That meant they could in principle activate in any text editor, not just SQL query windows. Added a runtime check (file extension `.sql`, falling back to the buffer's content type name containing "sql") so both only activate for actual SQL buffers.

## [1.2.0] - 2026-09-22

### Added

- **Auto-save after fix** option (Tools > Options > SQLFluff > General, off by default): when enabled, a successful Fix or Format saves the document automatically afterward, through the same Running Document Table path as `Ctrl+S`. When disabled (the default), behavior is unchanged and you save manually. Closes #2.

## [1.1.0] - 2026-09-22

### Added

- **Format** command, separate from Fix: runs `sqlfluff format` (a safe, stable subset of fixable rules) instead of `sqlfluff fix` (all fixable rules). Available in the Tools menu, context menu, toolbar, and via `Ctrl+K, Ctrl+Shift+D`.
- **Light Bulb integration** (`Alt+.`): squiggles now offer "Fix with SQLFluff" and "Format with SQLFluff" as quick actions, in addition to the dedicated commands.
- **Startup availability check**: the extension checks once, in the background, whether `sqlfluff` is reachable when SSMS loads, and reports a clear message in the status bar/output pane if not — instead of only failing on the first Lint/Fix.

### Changed

- The Fix command's menu text no longer says "Fix / Format"; Fix and Format are now distinct entries with distinct tooltips.

## [1.0.0] - 2026-09-21

### Added

- Initial release: SQLFluff linter and formatter integration for SSMS 22.
- **Lint** command with results in the Error List and squiggles in the editor.
- **Fix** command (selection or whole document).
- **Clear Diagnostics** command.
- Automatic linting on document open and on save (configurable).
- Optional lint-while-typing with a configurable debounce delay.
- Options page (Tools > Options > SQLFluff): executable path, dialect, config file, rules/exclusions, timeout, severity.
- Auto-detection of the `sqlfluff` executable from PATH, common Python `Scripts` directories, or `py -m sqlfluff`.
- Toolbar, context menu, and keyboard shortcuts (`Ctrl+K, Ctrl+Shift+L` for Lint, `Ctrl+K, Ctrl+Shift+F` for Fix).

[Unreleased]: https://github.com/mnigliazzo/sqlfluff-extension-ssms/compare/v1.2.1...HEAD
[1.2.1]: https://github.com/mnigliazzo/sqlfluff-extension-ssms/compare/v1.2.0...v1.2.1
[1.2.0]: https://github.com/mnigliazzo/sqlfluff-extension-ssms/compare/v1.1.0...v1.2.0
[1.1.0]: https://github.com/mnigliazzo/sqlfluff-extension-ssms/compare/v1.0.0...v1.1.0
[1.0.0]: https://github.com/mnigliazzo/sqlfluff-extension-ssms/releases/tag/v1.0.0
