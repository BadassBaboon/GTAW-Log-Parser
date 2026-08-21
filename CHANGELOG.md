# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [6.2.0] - 2026-08-15

### Added
- **FiveM Tools & Utilities Menu (`FiveMToolsWindow`)**:
  - Dedicated tools window accessible from the main navigation for managing FiveM installation, configuration, and troubleshooting.
  - **Automated ReShade Relocation & Hardware Key Injection**: Detects ReShade files in FiveM root (`dxgi.dll`, `d3d11.dll`, `reshade-shaders`, `ReShade.ini`), automatically relocates them to `FiveM.app\plugins`, and injects the Jenkins One-at-a-Time hardware hash key into `CitizenFX.ini` (`[Addons] ReShade5=...`) with real-time status indicators.
  - **First-Person Driving Field of View (FOV) Adjuster**: Configures `cam_vehicleFirstPersonFOV` in `%APPDATA%\CitizenFX\fivem.cfg` with instant auto-saving, presets (`60° (Recommended)`, `0° (FiveM Default)`, `-1 (Game Default)`), and custom degree input.
  - **Restore FiveM Graphic Settings**: Quick-action tool to safely delete `%APPDATA%\CitizenFX\gta5_settings.xml`, resetting all corrupted or misconfigured GTA 5 graphic/display settings to original clean defaults upon next launch.
  - **1-Click GTA World Desktop Shortcut**: Dedicated title bar button (`ApplicationPlus` icon) creating a `"GTA World"` desktop shortcut with custom icon (`ShortcutIcon.ico`). Launching the shortcut runs the Assistant minimized and auto-connects FiveM directly to `fivem.gta.world` (`--quick-launch`).
  - **FiveM Settings Management**: Integrated UpdateChannel selector (`Release`, `Beta`, `Latest (Unstable)`) and GTA V directory browser/validator with `GTA5.exe` path verification.
  - **Maintenance Utilities**: Quick-action tools to clear the `citizen` folder (forces fresh system file redownload) and clear server cache assets (`data\server-cache-priv`).
  - **Deep Path Detection (`FiveMDetector`)**: Multi-tier detection probing local AppData, fixed/removable drives, active FiveM processes (`FiveM.exe`, `FiveM_ROSLauncher.exe`), and Windows Registry uninstall keys.
- **AI Models & Parameter Guide Window (`AiModelInfoWindow`)**:
  - Replaced native message dialog with a dedicated Metro UI window providing detailed speed benchmarks, daily rate limits, model descriptions, and parameter explanations for all supported GroqCloud models.
- **Groq Model Additions**:
  - Added support for `groq/compound` (multi-agent router) and `groq/compound-mini` (lightweight multi-model router).
- **Concurrency Unit Testing**:
  - Added unit test `Parse_ConcurrentWriteLock_ReadsSuccessfully` in `ChatLogParserTests` validating concurrent chat log reading while the game engine holds active write access.

### Changed
- **Groq Model Migration**:
  - Migrated default and active AI models from decommissioned Llama 3 models (`llama-3.1-8b-instant`, `llama-3.3-70b-versatile`) to high-speed open-weight models: `openai/gpt-oss-20b` (Default, ~30–50ms latency) and `openai/gpt-oss-120b` (High Quality).
  - Added automatic config migration in `AiAssistantController.LoadSettings()` to upgrade existing user configs seamlessly without manual intervention.
  - Updated AI Accent Profile Generator to utilize `openai/gpt-oss-120b` for deep lore and persona analysis.

### Fixed
- **UI Thread Asynchronous Non-Blocking Updates**:
  - Refactored update check in `MainWindow.xaml.cs` to fully asynchronous `async Task CheckForUpdatesAsync` without synchronous `.Result` calls or `ManualResetEvent` thread-blocking, eliminating WPF UI deadlock risks.
- **Thread-Safe AI Settings & Quotas**:
  - Added `_settingsLock` synchronization across `LoadSettings`, `SaveSettings`, `ResetQuotasIfNeeded`, and key quota increments in `AiAssistantController` to prevent race conditions and `IOException` file write collisions on rapid hotkey triggers.
- **Concurrent File Sharing on Active Game Logs**:
  - Refactored `ChatLogParser` and `ChatLogScanner` to read `.storage` files using `FileShare.ReadWrite`, preventing locking collisions while GTA World actively writes to the log.
- **Socket Exhaustion Prevention**:
  - Updated `AutoUpdater` to use a shared static singleton `HttpClient` rather than instantiating per-request instances.
- **Null Safety & Observability**:
  - Added null-safety checks in `FiveMDetector.ResolveFiveMPaths` and replaced silent empty catch blocks across detectors and fixers with structured `Log.Debug` observability.

## [6.1.0] - 2026-07-28

### Added
- AI Accent Profile Generator window allowing players to paste character lore/backstory and generate custom accent directives using the `llama-3.3-70b-versatile` reasoning model.
- Automated Action Enricher supporting GTA World roleplay action commands (`/me`, `/my`, `/do`, `/dolow`, `/mylow`, `/melow`, `/melong`, `/mylong`, `/dolong`, `/ame`, `/amy`, `/ado`).
- Roleplay question detection rule protecting `/do` questions from being answered or generating fabricated inventory responses.
- Anti-hallucination prompt instructions to prevent fabricating unstated physical condition details, clothing conditions, injuries, or non-existent items.
- Dynamic shortcut button labeling that updates between `Accent Shortcut` and `Accent & Action Enricher Shortcut` based on Action Enricher state.

### Fixed
- Fixed single instance mutex initialization in `App.xaml.cs` using a system-wide `Global\` namespace Mutex to prevent duplicate process instances.
- Fixed uncapitalized formatting for `/me` and `/my` action lines to align with GTA World chat formatting (`* Firstname Lastname <action>`).
- Enforced output sentence-ending punctuation (`.`, `?`, `!`) and output length constraint compliance on action enrichment.
- Added COMException retry loop to clipboard operations in `MainWindow.xaml.cs` to prevent text capture failures when the clipboard is locked by external processes.
- Compacted settings panel layout in `MainWindow.xaml` to align bottom action buttons (`Manage API Keys`, `Accent Profiles`) with navigation tabs at Y = 238.

### Changed
- Action Enricher setting set to disabled (`False`) by default.
- Renamed "Phonetic spelling & slang" checkbox to "Phonetic Spelling" and reorganized settings checkboxes side-by-side.

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
