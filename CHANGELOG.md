# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [6.0.0] - 2026-07-14

Fork modernized and updated to support global AI Assistant text replacement and customizable accent profiles.

### Added
- Integrated AI Assistant controller and Groq API client supporting active model selection (`llama-3.1-8b-instant`, `llama-3.3-70b-versatile`, `openai/gpt-oss-120b`).
- Global keyboard hook simulation using hardware-level scan codes mapped via dynamic `MapVirtualKey` Win32 APIs, making hotkeys compatible with FiveM, RageMP, Discord, and system text areas.
- Bind `~` (tilde) key to `T` option (like SA:MP) to seamlessly open the chat box under keyboard hooks.
- Notification audio cues (`done.wav` and `failed.wav`) played on successful translations or processing failures.
- Custom speech accent profile manager to add, edit, and delete contraction patterns, contraction rules, and vocabulary guidelines.
- Relocated **Always close to system tray** and **Start with Windows** preferences to the main Program Settings panel with optimized category grouping and DPI-aware Borders.

### Changed
- Promoted translation shortcut from `Ctrl+Y` (redo collision) to `Ctrl+U`.
- Upgraded the default Tony Soprano accent profile parameters and added post-processing filters to completely strip em-dashes.
- Optimized hotkey response times to be instantaneous using active clipboard polling (every 2ms, max 50ms) instead of fixed thread sleeps.
- Changed default setting of `AlwaysCloseToTray` to `True` (enabled by default) and decoupled it from the automatic backup settings.
- Repository owner/fork owner updated to `BadassBaboon`.

## [5.0.0] - 2026-05-16

Major modernization. **Breaking:** end-users now need the .NET 8 Desktop
Runtime installed, or run the self-contained build.

### Changed
- **Target framework:** .NET Framework 4.8 → **.NET 8 (`net8.0-windows`)**
- **Project format:** legacy `MSBuild` csproj → SDK-style (`Microsoft.NET.Sdk`), net ~300 lines of XML removed
- **MahApps.Metro:** 1.6.5 → **2.4.10**. Theming rewritten for the new `ThemeManager.Current.ChangeTheme(app, "Light.Amber")` + `ThemeSyncMode` API
- **MahApps.Metro.IconPacks.Material:** 3.7.0 → **5.0.0**. Renamed icons: `Settings` → `Cog`, `GithubFace` → `Github`, `FacebookBox` → `Facebook`
- **Octokit:** 0.48.0 → **14.0.0**
- **Extended.Wpf.Toolkit:** 4.0.1 → **4.7.x**
- **Repository owner:** `AdvGTAW` → `blancodagoat` (fork)

### Added
- New `Shared/` class library (`GTAWParser.Shared.dll`) hosting `LocalizationController`, `ChatLogScanner`, and `ChatLogParser`. Both Parser and Assistant `ProjectReference` it; eliminates the byte-identical duplication that existed between the two apps.
- Managed `ShellLink` P/Invoke wrapper (`Assistant/Utilities/ShellLink.cs`) for startup-shortcut creation, replacing the legacy `IWshRuntimeLibrary` COM reference. Unblocks `dotnet build` from the CLI.
- Native `<PublishSingleFile>true</PublishSingleFile>` support for both projects.

### Removed
- `Costura.Fody`, `Fody`, `Resource.Embedder` packages — superseded by .NET 8's native single-file publish and satellite-assembly handling.
- `IWshRuntimeLibrary` COM reference.
- `LanguagePickerWindow.xaml(.cs)` — its only caller had been commented out for years.
- `Logo_MouseLeftButtonUp` event handler — body was entirely commented out.

### Fixed
- **`ChatLogFilterWindow`:** per-line regex recompilation (7 patterns × N lines) and `string +=` concatenation in the filter loop replaced with statically compiled `Regex` fields and a `StringBuilder` pre-sized to the input. Roughly **7–14× faster** filtering on large logs.
- **`BackupController`:** replaced `Thread` + boolean flags + 10-second `Thread.Sleep` polling with `Task` + `CancellationTokenSource` + `Task.Delay(ms, ct)`. App shutdown is no longer delayed by up to 10 seconds while the backup loop wakes up.
- **`ChatLogParser`:** regex-over-JSON replaced with `Utf8JsonReader` streaming. More robust against `.storage` format changes; no full-document materialisation.
- Silent `catch { }` blocks (9 sites) now write to `Debug.WriteLine` so failures are visible to an attached debugger.
- `Process.Start(url)` calls (6 sites) replaced with `Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })`, the required pattern on .NET 5+.
- `Application.ExitInternal` reflection invocation replaced with `Application.Exit()`.
- `StyleController`: the `ManagementEventWatcher` registry-polling dance for "follow system mode/color" replaced with MahApps 2.x's built-in `ThemeManager.Current.ThemeSyncMode`. Same UX, ~145 fewer lines.
- All `directoryPath + "\\..."` string concatenation replaced with `Path.Combine`.
- `HashGenerator` manual hex `StringBuilder` loop replaced with `Convert.ToHexString` + `MD5.HashData`.
- `IsBetaVersion` `const bool` promoted to property; removes 4 `#pragma warning disable 162` blocks and 10 `// ReSharper disable once` suppression comments.
- Hardcoded mutex names hoisted to `ProgramController.MutexName` / `AppController.MutexName` constants.

### Internal
- Project structure: `Parser/` (WinForms) + `Assistant/` (WPF) + `Shared/` (new). `dotnet build GTAW-Log-Parser.sln` builds all three from the CLI.
- Net **−1210 lines** across the codebase (36 files changed, 6 deleted, 5 new) for the modernization.

## [4.1.8] - prior

See git history.
