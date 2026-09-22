# Changelog

All notable changes to this project are documented here.

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versioning follows [Semantic Versioning](https://semver.org/).

## [Unreleased]

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

[Unreleased]: https://github.com/mnigliazzo/sqlfluff-extension-ssms/compare/v1.1.0...HEAD
[1.1.0]: https://github.com/mnigliazzo/sqlfluff-extension-ssms/compare/v1.0.0...v1.1.0
[1.0.0]: https://github.com/mnigliazzo/sqlfluff-extension-ssms/releases/tag/v1.0.0
