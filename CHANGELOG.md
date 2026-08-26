# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [6.5.0] - 2026-08-26

### Added
- **GTA World Roleplay Screenshot Editor (`ScreenshotEditorWindow.xaml`, `ScreenshotEditorWindow.xaml.cs`, `ScreenshotRenderer.cs`, `RoleplayChatColorizer.cs`, `ResolutionPreset.cs`)**:
  - Built-in Roleplay Screenshot Editor accessible from the top menu bar between FiveM Tools and About, dedicated to crafting character showcase and faction thread screenshots:
    - **Unified Canvas Workspace**: Load background screenshots via file browser, drag-and-drop, or direct clipboard paste (`Ctrl+V`). Interactive canvas pan (click & drag), mouse wheel zoom, Aspect Fit, Aspect Fill, and 100% reset.
    - **Single Streamlined Chat Lines Section**: Merged the previous multi-step workflow into a single, cohesive workspace. Anything imported, pasted, or typed is immediately rendered on the canvas and available for customization without intermediate steps.
    - **Drag & Drop Line Reordering**: Added drag handles next to line swatches for smooth reordering of canvas lines.
    - **Fluid In-Place Editing & Enter Key**: Edit lines directly in the sidebar; pressing Enter inside any line automatically creates a new line below it and focuses its editor.
    - **Unified Official GTA World Color Swatches**: Cleaned and consolidated color presets against live in-game server palettes (Radio Blue `#1E90FF`, Action Purple `#C2A2DA`, Phone Yellow `#FFFF00`, Cash & Item Green `#32CD32`, Whispers `#DCDCDC`, OOC Grey `#A6ACAF`, and Server Notice Amber `#EE9A00`).
    - **Line & Selection-Level Recoloring**: Clicking a line's swatch recolors the full line, while selecting/highlighting any substring in a line and choosing a swatch or custom hex code recolors only the selected text.
    - **Rich HTML Chatlog Importer (`ChatLogHtmlParser.cs`)**:
      - Strips HTML boilerplate, doctype, `<style>` CSS rule blocks, `<script>` tags, and comments.
      - Automatically extracts `<div class="chat-line">` lines or falls back to `<p>`, `<div>`, and `<br>` elements from forums and browsers.
      - Extracts and normalizes inline colors (`#RRGGBB`, `#RGB`, `rgb(r, g, b)`, named colors) and typography (bold, italic).
      - Automatically strips timestamps (`<span class="timestamp">` and `[HH:MM:SS]`) and decodes HTML entities.
    - **Prioritized HTML Ground-Truth Colors**: HTML chat logs carry authentic colors captured directly from FiveM's CEF NUI DOM or forum styles. These are strictly prioritized over heuristic regex guessing.
    - **Auto-Colour Toggle**: Switchable checkbox (enabled by default) that applies roleplay chat color rules, or leaves text plain white when turned off for manual recoloring.
    - **Prioritized Custom Backup Directory**: Opening **Import chatlog** automatically detects and navigates to the user's custom chatlog backup directory configured in Backup Settings.
    - **Standard & Community Resolution Presets with Persistent Memory**: Presets for 16:9 (`1920x1080`, `1600x900`, `1280x720`), widescreen (`1680x1050`, `1440x900`, `1200x800`), classic 4:3/5:4 (`1024x768`, `1280x960`, `1280x1024`), square (1:1), and community standards (`1300x730`, `1150x750`, `1650x1060`) with custom resolution inputs. The user's selected preset and canvas dimensions are automatically remembered across sessions so screenshot styles remain consistent.
    - **Spoiler & Censor Redaction Bars (`IsCensored`)**: Added a censor option in the line and selection color menu to hide sensitive details like money balances, item quantities, and names. Redacts highlighted text or lines with Discord-style solid black spoiler boxes on the rendered screenshot while keeping text legible in the editor sidebar.
    - **Streamlined 2-Column Style & Position Inspector**:
      - Compact 2-column inspector eliminating vertical scrolling.
      - 2x2 corner snap buttons (`↖ ↗ / ↙ ↘`) paired with side-by-side X and Y pixel offsets.
      - Font family dropdown paired with a size slider and live numeric readout.
      - Line spacing and outline width sliders side by side.
      - Horizontal effects checkboxes (`Bold`, `Drop shadow`, `Backing box`) with contextual opacity slider.
    - **Adaptive UI Locking**: Resolution presets, zoom controls, fit/fill buttons, and export buttons remain cleanly disabled until a background screenshot is loaded.
    - **Unified Windows Save Dialog**: Replaced separate PNG and JPEG buttons with a single "Save Image" button opening a standard Windows save dialog defaulting to PNG with JPEG selectable in the file type dropdown.
    - **1-Click Clipboard Export**: `Ctrl+Shift+C` and "Copy image" button for instant sharing to Discord, forums, or image hosts.

### Fixed
- **Screenshot Editor Chat Dragging Pegged The CPU (`ScreenshotRenderer.cs`, `ScreenshotEditorWindow.xaml.cs`)**:
  - Every mouse move re-rasterised the entire chat block, and the eight-pass glyph outline made that roughly 85% of the frame: a 120-line block measured **68 ms per frame**, so mouse events arrived faster than frames could be produced. Dragging changes the block position but not one pixel of its content, so it is now rendered once to its own bitmap at drag start and composited each frame: **68 ms to 8.6 ms**, and the cost no longer grows with line count. Image panning benefits identically. The cache is discarded on mouse-up and on lost mouse capture, so the exported image is always a full render.
- **Old Backups Kept Their Doubled Line Spacing (`BackupController.cs`)**:
  - `WriteBackupFileWithDeduplication` decided whether to replace a backup by comparing byte length. A correctly normalised file is *shorter* than the same log written with the old spacing, so the corrected content was discarded and the corrupted file kept indefinitely. The check now compares content: an identical log is rewritten when only its line endings differ, and the truncation guard counts lines instead of bytes.
- **Screenshot Editor Palette Drift (`RoleplayChatColorizer.cs`)**:
  - The `/me & /do` swatch was `#C2A3DA` while the classifier and the live game both use `#C2A2DA`, so an auto-coloured emote and a manually-swatched one stored different values in the same screenshot. Unified on the measured `#C2A2DA`.
  - Removed the last literal colors from the Screenshot Editor (swatch hairline, rich-text caret and foreground, redaction fill and text); every color in the window now resolves through the theme block.

- **Theme Selector Unlocked Regardless Of Settings (`ProgramSettingsWindow.xaml`, `ProgramSettingsWindow.xaml.cs`)**:
  - The theme dropdown's only lock condition was `Use system accent color`. While on the GTA World default theme the list now stays disabled, and a `Use a custom accent color` checkbox enables customization without stranding unreachable accents.
  - Fixed blank theme dropdown caused by mismatched string identifier when unticking system accent.
- **GTA World Chat Color Accuracy (`ChatLineClassifier.cs`, `RoleplayChatColorizer.cs`)**:
  - `/do` shares `/me` purple (`#C2A2DA`) in game; fixed `/do` rendering green or teal.
  - Fixed `[Radio]` and `[Dispatch]` misdetected as advertisements due to un-bounded "Ad" regex.
  - Corrected success green to `#32CD32` based on live in-game DOM inspection.
  - Added classification for raw typed `/me` and `/do` commands, private messages (`(( PM from ... ))`), and bracket-tagged server messages.
  - Preserved `/low` speech dimming in Live Tail (`#DCDCDC`).
  - Audited against 59 lines captured from a live GTA World English session with regression tests.
- **Eliminated Empty Space Lines in Saved .txt Chatlog Backups (`BackupController.cs`, `MainWindow.xaml.cs`, `ChatLogFilterWindow.xaml.cs`, `Main.cs`, `FiveMChatCaptureService.cs`, `ChatLogParser.cs`)**:
  - Replaced flawed `.Replace("\n", Environment.NewLine)` calls with centralized `ChatLogParser.NormalizeLineEndings` across all log exporters and clipboard operations.
  - Collapses degenerate `\r\r\n` sequences into standard single `\r\n` line endings on Windows, eliminating unintended blank lines between chat lines across automated backups, manual saves, and clipboard copies.

## [6.4.0] - 2026-08-24

### Added
- **Automatic & Multi-Region GTA World Server Timezone Support (`ServerTimezoneHelper.cs`, `FiveMChatCaptureService.cs`, `ProgramSettingsWindow.xaml`, `ProgramSettingsWindow.xaml.cs`)**:
  - **Live Auto-Detection (`Auto-detect server timezone`)**: Added an `"Auto-detect server timezone"` checkbox (enabled by default) that automatically extracts the live server clock in real-time:
    - **In-Game Minimap HUD (`.wxTime`)**: Continuously samples the active in-game clock sitting directly above the minimap (`citFrames['client'] -> .wxTime`), allowing seamless calibration even when the Assistant is launched mid-game.
    - **Login & Loading Screen (`#clock`)**: Samples the clock from the top-right widget on the login screen (`citFrames['loadingScreen'] -> #clock`), calibrating the offset before the character spawns.
    - **In-Game Native Timestamps**: Synchronizes against incoming chat lines containing `[HH:mm:ss]`.
  - **Manual Regional Overrides**: When unticked, unlocks the dedicated `Server Timezone` selector dropdown in Program Settings with full support for all regional GTA World servers:
    - **`GTA World English (UTC / Server Time)`** (UTC+0)
    - **`GTA World Türkiye (Istanbul / UTC+3)`** (TRT / UTC+3)
    - **`GTA World Korea (Seoul / UTC+9)`** (KST / UTC+9)
    - **`GTA World Russia (St. Petersburg / UTC+3)`** (MSK / UTC+3)
    - **`GTA World France (Paris / CET)`** (CET/CEST)
    - **`GTA World Spain (Madrid / CET)`** (CET/CEST)
    - **`Local PC Time (System Clock)`** (Client OS local timezone)
  - Log parsing, Live Tail streaming, and automated backup exports now calculate session headers and missing timestamps against the accurate GTA World server clock, eliminating timezone skew for players who play with in-game `/timestamp` turned off.

### Changed
- **Theme-Adaptive Live Tail Viewport (`LiveTailWindow.xaml`, `LiveTailWindow.xaml.cs`)**:
  - Replaced high-contrast stark black viewport background in Light Mode with a soft, balanced Nord slate charcoal surface (`#2B303A`) with a subtle slate silver border (`#C4CBD5`), preserving AAA contrast for white and yellow text while smoothly blending into the light application window.

### Fixed
- **AI Assistant Typography & Command Formatting (`AiTextSanitizer.cs`, `AiAssistantController.cs`)**:
  - **Typographic Slop Normalization**: Added deterministic normalization that converts curly apostrophes (`’`, `‘`), smart quotes (`“`, `”`, `«`, `»`), and em-dashes (`—`, `–`) generated by `gpt-oss-20b`/`120b` into standard ASCII straight quotes (`'`, `"`) and clean punctuation, preventing in-game chat font rendering glitches.
  - **`/me` & `/my` Third-Person Pronoun Stripping**: Enforced strict verb-first present-tense formatting for roleplay action commands, automatically stripping redundant AI-inserted third-person subjects (`He`, `She`, `They`, `The man`) and possessive pronouns (`His`, `Her`) so `/me` and `/my` actions seamlessly follow the character's name without double pronouns.
  - **Clipboard Race Condition Fix (`MainWindow.xaml.cs`, `KeyboardHookManager.cs`)**: Fixed an issue where the clipboard was prematurely overwritten/cleared 40ms after simulating a paste, which caused FiveM/CEF to occasionally paste empty text under GPU or frame-delay conditions. Added hardware-level inter-key hold intervals to guarantee reliable copy and paste capture across all frame rates.

## [6.3.0] - 2026-08-24

### Added
- **Unified AppData Storage Architecture (`Shared/Logging.cs`, `Shared/FiveMChatCaptureService.cs`, `Assistant/Controllers/AiAssistantController.cs`, `Assistant/Controllers/UpdateController.cs`, `AppController.cs`)**:
  - Consolidated all scattered AppData paths into a single, clean, standardized directory at `%LOCALAPPDATA%\GTAW-Log-Parser\` (`config\`, `session\`, `logs\`, `rollback\`).
  - Added automatic startup migration in `AppController.MigrateLegacyAppDataDirectories()` that silently moves settings and session files from legacy paths (`AppData\Roaming\GTAWChatLogAssistant` and `AppData\Local\GTAW-Log-Parser-FiveM`) and permanently cleans up the old directories.
- **Rich HTML Chatlog Export and WYSIWYG Forum Paste Safety (`ChatLogHtmlExporter.cs`, `MainWindow.xaml.cs`)**:
  - Added standalone HTML export with full color preservation, clean dark mode container styling, and responsive layout.
  - Spans carry only foreground color styles (`color: #RRGGBB`) without inline background blocks, preventing intrusive dark highlight boxes when copying and pasting into Invision Community (IPS) forum WYSIWYG editors.
  - Integrated into the Main Window "Save As..." dialog with `.txt` and `.html` file type filters.
- **Configurable Automatic Backup Formats (`BackupController.cs`, `BackupSettingsWindow.xaml`, `BackupSettingsWindow.xaml.cs`)**:
  - Added `BackupFormat` setting with dropdown options: `Plain Text (.txt)`, `Rich HTML (.html)`, or `Both (.txt and .html)`.
  - Automatically exports backups using the selected format during periodic (interval) and game-exit backup runs with automatic deduplication.
- **Per-Feature AI Toggles and Shortcuts (`MainWindow.xaml`, `MainWindow.xaml.cs`, `AiAssistantSettings`, `KeyboardHookManager`)**:
  - Added dedicated on/off toggle checkboxes for each AI capability: Accent & Action Enricher (`AiAccentEnabled`), Language Translation (`AiTranslateEnabled`), and Auto-Correct (`AiCorrectEnabled`).
  - Disabling a feature's checkbox disables and grays out its shortcut configuration button and removes key combination interception in `KeyboardHookManager`.
- **FiveM Foreground Window Hotkey Filter (`KeyboardHookManager.cs`, `MainWindow.xaml`)**:
  - Added `AiFiveMOnlyEnabled` checkbox (`FiveM hotkey only`, enabled by default).
  - Uses Win32 `GetForegroundWindow` and process name inspection to restrict `Ctrl+T`, `Ctrl+U`, `Ctrl+H`, and `~` hotkeys to FiveM processes (`FiveM_GTAProcess`, `GTA5`, `citizenfx`).
  - When other applications (such as Chrome, VS Code, or Notepad) are active, key combinations pass through to the operating system.
- **Live Tail Search Bar and Navigation (`LiveTailWindow.xaml`, `LiveTailWindow.xaml.cs`)**:
  - Added an inline search bar with match counter (`0/0`, `1 of 14`, `No matches`), Next match (`▼`), Previous match (`▲`), and Clear search (`✕`) buttons.
  - Re-engineered search engine to perform paragraph-level multi-span slicing, allowing search terms with leading/trailing spaces (e.g. `"Benjamin "`) and multi-word phrases to seamlessly match across different colored text spans while preserving per-span formatting and navigation stepping.
- **NUI DOM CSS Color Extraction and Per-Span Colorizer (`CapturedChatLine.cs`, `FiveMChatCaptureService.cs`, `LiveTailWindow.xaml.cs`)**:
  - Replaced heuristic regex coloring with live Chrome DevTools Protocol DOM extraction from FiveM's CEF browser.
  - Traverses `.chat__messages > li` child nodes via `TreeWalker` to extract inline `style.color` and computed styles per text segment.
  - Emits structured `CapturedChatLine` objects over `CapturedLineReceived` event with exact per-span and dominant hex colors while preserving plain-text `current-session.txt` format.
  - Renders multi-colored messages (e.g. weather forecasts, store notices, and server info) with segment-level color runs in Live Tail.
  - Added thread-safe `DynamicBrushCache` with frozen `SolidColorBrush` instances for zero-allocation rendering.
  - Updated unclassified text and default fallback to white (`#FFFFFF`).
- **Live Tail Auto-Start, Buffer Capping, and Continuous Auto-Scroll (`LiveTailWindow.xaml.cs`)**:
  - Automatically begins log streaming on window load (`LiveTail_Loaded`) without requiring manual activation.
  - Capped active in-memory line buffer at 5,000 lines to ensure minimal RAM usage and high UI responsiveness.
  - Automatically scrolls to the bottom on every received line and upon initial session load.
- **UI Tooltips (`MainWindow.xaml`)**:
  - Added tooltips across action controls and buttons (`Remove timestamps`, `Bind ~ to T`, `Live preview`, `Parse`, `Save As`, and `Copy To Clipboard`).

### Changed
- **In-Game Performance Optimization (Frame-Dip Elimination) (`FiveMChatCaptureService.cs`)**:
  - Rewrote the NUI JavaScript extraction expression to use an in-memory `hexCache` and `WeakMap` cached `getComputedStyle`.
  - Completely eliminated HTML5 canvas rendering and layout-reflow thrashing in FiveM's CEF browser, eliminating in-game frame dips during continuous chat capture.
- **Phonetic Spelling Default**:
  - Changed `PhoneticEnabled` default value to `true` in `AiAssistantSettings` and UI initialization.
- **AI Assistant Labels and Layout**:
  - Renamed `AI Mode:` to `Configure AI Feature:`.
  - Renamed `Keyboard Shortcuts:` to `Active AI Features & Shortcuts:`.
  - Adjusted shortcut button width to 238 px to accommodate toggle checkboxes.
- **Silent Background Auto-Backups (`BackupController.cs`, `AppController.cs`)**:
  - Background auto-backups on FiveM game close and interval timers now pass `showError: false`.
  - Closing FiveM before joining a server or before the chat HUD renders no longer displays modal error dialogs.

### Fixed
- **Bulletproof Fuzzy Overlap Engine (`FiveMChatCaptureService.cs`, `FiveMDetector.cs`)**:
  - Re-engineered `FindOverlap` to employ a 90% fuzzy match threshold alongside aggressive whitespace and casing normalization (`NormalizeForOverlap`).
  - Closing and reopening the Assistant mid-game safely resumes the active session instead of wiping `current-session.txt` or repeatedly appending 100-line duplicate blocks.
- **In-Character Timestamp Preservation (`FiveMChatCaptureService.cs`, `ProgramController.cs`)**:
  - Anchored timestamp removal regexes (`(?m)^\[\d{1,2}:\d{1,2}:\d{1,2}\]\s*`), ensuring manual in-character roleplay timestamps within message bodies are never stripped.
- **Fallback Color Classification Accuracy (`ChatLineClassifier.cs`)**:
  - Added native regex matching for Admin kick announcements (`.*was kicked for:`) and Local OOC messages (`(( (ID) Name: message ))`), rendering Admin kicks in `#FF0000` Red and Local OOC usernames in `#31CB31` Green.
- **Exit Flush Safeguard for Interval Backups (`BackupController.cs`)**:
  - `BackupController.Quitting` now triggers an immediate backup flush for both interval-based and game-exit backup configurations.
  - Added path validation guard to prevent writing to relative paths if the backup directory is unset.
- **Live Tail Double Newlines (`LiveTailWindow.xaml.cs`)**:
  - Replaced `.Replace("\n", Environment.NewLine)` with newline normalization (`.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n')`), eliminating `\r\r\n` extra blank lines in WPF text boxes.
- **Live Preview Caret Preservation (`MainWindow.xaml.cs`)**:
  - Retained `CaretIndex` and text selection across background `LivePreviewTimer_Tick` updates to prevent cursor resets during log review.

## [6.2.0] - 2026-08-22

### Added
- **FiveM Chromium DevTools Protocol Chat Capture Engine (`FiveMChatCaptureService`)**:
  - Connects to FiveM's local CEF debugger at `http://127.0.0.1:13172/json` and isolates the GTAW chat frame (`https://cfx-nui-client/web/index.html`).
  - Reads `.chat__messages > li` DOM elements and extracts timestamps from attributes and pseudo-elements.
  - Deduplicates chat lines across 500 ms polling ticks using sliding-window matching (`FindOverlap`).
  - Appends new lines to `%LOCALAPPDATA%\GTAW-Log-Parser-FiveM\current-session.txt` with `FileShare.ReadWrite` for concurrent reads by Assistant, Parser Mini, and Live Tail.
  - Automatically reconnects without process restarts when FiveM restarts or when the HUD reloads.
- **Verified In-App Self-Updater with Rollback (`UpdateController`, `VersionHelper`)**:
  - Queries GitHub Releases via `Octokit`, downloads target binaries to temporary storage, and checks SHA-256 hashes when present.
  - Backs up active executables to `%LOCALAPPDATA%\GTAW-Log-Parser-FiveM\Rollback\` before applying updates.
  - Executes an atomic `.cmd` swap script that monitors the process PID, waits for process exit, overwrites binaries, and restarts the application.
  - Added `Check for Updates` and `Revert to Previous` buttons with rollback status indicators in `ProgramSettingsWindow`.
  - Added `VersionHelper` supporting semantic version parsing and comparison for release and prerelease tags.
- **FiveM Configuration and Maintenance Tools (`FiveMToolsWindow`)**:
  - Added FiveM installation directory selector and GTA V folder configurator with `GTA5.exe` path validation.
  - Added First-Person Driving FOV adjuster for `cam_vehicleFirstPersonFOV` in `%APPDATA%\CitizenFX\fivem.cfg` with presets (`60° (Recommended)`, `0° (FiveM Default)`, `-1 (Game Default)`) and custom degree inputs.
  - Added ReShade detection, file relocation to `FiveM.app\plugins`, and Jenkins One-at-a-Time hardware hash key injection for `[Addons] ReShade5` in `CitizenFX.ini`.
  - Added cache clearing tools for `data\server-cache-priv` and the `citizen` system directory.
  - Added graphic configuration reset tool that removes corrupted `%APPDATA%\CitizenFX\gta5_settings.xml` files.
- **Desktop Shortcut and Community Links**:
  - Added title bar shortcut creation button generating a `"GTA World"` desktop shortcut with `ShortcutIcon.ico` and the `--quick-launch` argument (starts minimized and connects to `fivem.gta.world`).
  - Added title bar Discord button with an embedded vector icon linking to `https://discord.gg/qRdVSkUW6n` (Baboon's Workshop).
  - Added title bar icon toggle checkboxes in `ProgramSettingsWindow` for the shortcut creator and Discord buttons.
- **AI Models Guide Window (`AiModelInfoWindow`)**:
  - Added a dedicated Metro UI window displaying speed benchmarks, daily rate limits, model descriptions, and parameter explanations for all supported GroqCloud models.
- **Groq Model Additions**:
  - Added support for `groq/compound` (multi-agent router) and `groq/compound-mini` (lightweight multi-model router).
- **Live Tail and Filtering**:
  - `LiveTailWindow` subscribes directly to `FiveMChatCaptureService.LineReceived` events for event-driven log streaming.
  - `ChatLogFilterWindow` strips timestamps prior to matching regex rules (OOC, IC, Emote, Action, PM, Radio, Ads, and custom name filters) against the active session log.

### Changed
- **Groq Model Migration**:
  - Migrated default and active AI models from decommissioned Llama 3 models to `openai/gpt-oss-20b` (Default, ~30–50 ms latency) and `openai/gpt-oss-120b` (High Quality).
  - Added automatic config migration in `AiAssistantController.LoadSettings()` to update existing user configurations without manual intervention.
  - Updated AI Accent Profile Generator to use `openai/gpt-oss-120b` for character lore analysis.
- Moved the `Check for updates automatically` toggle from `MainWindow` to `ProgramSettingsWindow` under the Updates group.
- Set the title bar releases download icon (`DisableReleasesButton`) to hidden by default in `Settings.settings`.
- Reorganized `ProgramSettingsWindow` into a balanced two-column layout with 22 px line spacing.

### Fixed
- **Thread-Safe AI Settings and Quotas**:
  - Added `_settingsLock` synchronization across `LoadSettings`, `SaveSettings`, `ResetQuotasIfNeeded`, and key quota increments in `AiAssistantController` to prevent race conditions and `IOException` collisions on rapid hotkey triggers.
- **Null Safety and Observability**:
  - Added null-safety checks in `FiveMDetector.ResolveFiveMPaths` and replaced silent catch blocks across detectors and fixers with structured `Log.Debug` logging.

### Removed
- Removed all RageMP log file paths, directory selectors, `.storage` file scanners, and RageMP process detection logic.
- Removed legacy `AutoUpdater.cs` in favor of `UpdateController.cs`.

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
