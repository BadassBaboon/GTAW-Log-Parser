using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Serilog;

namespace GTAWParser.Shared
{
    public static class FiveMConfigManager
    {
        public static readonly Dictionary<string, string> ChannelMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "production", "Release" },
            { "beta", "Beta" },
            { "canary", "Latest (Unstable)" }
        };

        public static readonly Dictionary<string, string> ReverseChannelMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Release", "production" },
            { "Beta", "beta" },
            { "Latest (Unstable)", "canary" }
        };

        /// <summary>
        /// Reads the current UpdateChannel from CitizenFX.ini under [Game].
        /// </summary>
        public static string GetUpdateChannel(string fivemDir)
        {
            try
            {
                FiveMPaths paths = FiveMDetector.ResolveFiveMPaths(fivemDir);
                if (File.Exists(paths.CitizenFXIniPath))
                {
                    string[] lines = ReadAllLinesShared(paths.CitizenFXIniPath);
                    bool inGameSection = false;
                    foreach (string rawLine in lines)
                    {
                        string line = rawLine.Trim();
                        if (line.Equals("[Game]", StringComparison.OrdinalIgnoreCase))
                        {
                            inGameSection = true;
                        }
                        else if (inGameSection && line.StartsWith("["))
                        {
                            break;
                        }
                        else if (inGameSection && line.StartsWith("UpdateChannel=", StringComparison.OrdinalIgnoreCase))
                        {
                            string val = line.Substring("UpdateChannel=".Length).Trim();
                            if (ChannelMap.TryGetValue(val, out string? displayName))
                            {
                                return displayName;
                            }
                            return val;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error reading UpdateChannel from CitizenFX.ini");
            }

            return "Release"; // Default
        }

        /// <summary>
        /// Sets the UpdateChannel in CitizenFX.ini under [Game].
        /// </summary>
        public static bool SetUpdateChannel(string fivemDir, string channelValue, out string statusMessage)
        {
            statusMessage = string.Empty;
            try
            {
                FiveMPaths paths = FiveMDetector.ResolveFiveMPaths(fivemDir);
                string iniPath = paths.CitizenFXIniPath;
                string rawChannel = ReverseChannelMap.TryGetValue(channelValue, out string? rev) ? rev : channelValue;

                UpdateIniKey(iniPath, "Game", "UpdateChannel", rawChannel);
                statusMessage = $"Update channel changed to '{channelValue}'. Please restart FiveM for changes to take effect.";
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error setting UpdateChannel in CitizenFX.ini");
                statusMessage = $"Failed to update channel: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Reads the current GTA V installation path (IVPath) from CitizenFX.ini under [Game].
        /// </summary>
        public static string GetGtaVPath(string fivemDir)
        {
            try
            {
                FiveMPaths paths = FiveMDetector.ResolveFiveMPaths(fivemDir);
                if (File.Exists(paths.CitizenFXIniPath))
                {
                    string[] lines = ReadAllLinesShared(paths.CitizenFXIniPath);
                    bool inGameSection = false;
                    foreach (string rawLine in lines)
                    {
                        string line = rawLine.Trim();
                        if (line.Equals("[Game]", StringComparison.OrdinalIgnoreCase))
                        {
                            inGameSection = true;
                        }
                        else if (inGameSection && line.StartsWith("["))
                        {
                            break;
                        }
                        else if (inGameSection && line.StartsWith("IVPath=", StringComparison.OrdinalIgnoreCase))
                        {
                            return line.Substring("IVPath=".Length).Trim();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error reading IVPath from CitizenFX.ini");
            }

            return string.Empty;
        }

        /// <summary>
        /// Validates GTA V directory and updates IVPath in CitizenFX.ini under [Game].
        /// Handles folders containing GTA5.exe, GTA5_Enhanced.exe, or both cleanly.
        /// </summary>
        public static bool SetGtaVPath(string fivemDir, string gtaDir, out string statusMessage)
        {
            statusMessage = string.Empty;
            try
            {
                if (string.IsNullOrWhiteSpace(gtaDir) || !Directory.Exists(gtaDir))
                {
                    statusMessage = "Selected GTA V directory does not exist.";
                    return false;
                }

                if (!File.Exists(Path.Combine(gtaDir, "GTA5.exe")))
                {
                    statusMessage = "Selected directory does not contain 'GTA5.exe'.";
                    return false;
                }

                FiveMPaths paths = FiveMDetector.ResolveFiveMPaths(fivemDir);
                UpdateIniKey(paths.CitizenFXIniPath, "Game", "IVPath", gtaDir);

                statusMessage = "GTA V installation path updated successfully in CitizenFX.ini!";
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error setting IVPath in CitizenFX.ini");
                statusMessage = $"Failed to update GTA V path: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Deletes FiveM.app\citizen directory to allow FiveM to redownload fresh system files.
        /// </summary>
        public static bool ClearCitizenFolder(string fivemDir, out string statusMessage)
        {
            statusMessage = string.Empty;
            try
            {
                if (string.IsNullOrWhiteSpace(fivemDir) || !Directory.Exists(fivemDir))
                {
                    statusMessage = "FiveM installation directory is invalid or not found.";
                    return false;
                }

                FiveMPaths paths = FiveMDetector.ResolveFiveMPaths(fivemDir);
                string citizenDir = Path.Combine(paths.AppDataDirectory, "citizen");

                if (Directory.Exists(citizenDir))
                {
                    Directory.Delete(citizenDir, true);
                    statusMessage = "Successfully deleted FiveM citizen folder. FiveM will redownload clean files on launch.";
                    Log.Information("Deleted FiveM citizen folder at {Path}", citizenDir);
                    return true;
                }
                else
                {
                    statusMessage = "Citizen folder does not exist or was already cleared.";
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error deleting FiveM citizen folder");
                statusMessage = $"Failed to delete citizen folder: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Deletes FiveM.app\data\server-cache-priv directory to clear downloaded server cache assets.
        /// </summary>
        public static bool ClearServerCache(string fivemDir, out string statusMessage)
        {
            statusMessage = string.Empty;
            try
            {
                if (string.IsNullOrWhiteSpace(fivemDir) || !Directory.Exists(fivemDir))
                {
                    statusMessage = "FiveM installation directory is invalid or not found.";
                    return false;
                }

                FiveMPaths paths = FiveMDetector.ResolveFiveMPaths(fivemDir);
                string serverCacheDir = Path.Combine(paths.AppDataDirectory, "data", "server-cache-priv");

                if (Directory.Exists(serverCacheDir))
                {
                    Directory.Delete(serverCacheDir, true);
                    statusMessage = "Successfully deleted server cache files. FiveM will redownload fresh server assets on connection.";
                    Log.Information("Deleted server cache folder at {Path}", serverCacheDir);
                    return true;
                }
                else
                {
                    statusMessage = "Server cache folder does not exist or was already cleared.";
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error deleting FiveM server cache folder");
                statusMessage = $"Failed to delete server cache: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Gets the standard path to FiveM's fivem.cfg file under %APPDATA%\CitizenFX\fivem.cfg.
        /// </summary>
        public static string GetFiveMCfgPath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CitizenFX", "fivem.cfg");
        }

        private static readonly Regex FovRegex = new Regex(@"^seta\s+""?cam_vehicleFirstPersonFOV""?\s+""?([^""]+)""?", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex BudgetScaleRegex = new Regex(@"^seta\s+""?vid_budgetScale""?\s+""?([^""]+)""?", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Reads the vehicle first person FOV setting from fivem.cfg.
        /// Returns 60.0f as recommended default if unconfigured or file is missing.
        /// </summary>
        public static float GetVehicleFirstPersonFov(string? customCfgPath = null)
        {
            try
            {
                string cfgPath = customCfgPath ?? GetFiveMCfgPath();
                if (File.Exists(cfgPath))
                {
                    string[] lines = ReadAllLinesShared(cfgPath);
                    foreach (string rawLine in lines)
                    {
                        string line = rawLine.Trim();
                        var match = FovRegex.Match(line);
                        if (match.Success)
                        {
                            if (float.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float fov))
                            {
                                return fov;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error reading cam_vehicleFirstPersonFOV from fivem.cfg");
            }

            return 60.0f; // Recommended default
        }

        /// <summary>
        /// Updates or sets the cam_vehicleFirstPersonFOV setting in fivem.cfg.
        /// </summary>
        public static bool SetVehicleFirstPersonFov(float fov, out string statusMessage, string? customCfgPath = null)
        {
            statusMessage = string.Empty;
            try
            {
                string cfgPath = customCfgPath ?? GetFiveMCfgPath();
                string? dir = Path.GetDirectoryName(cfgPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                List<string> lines = new List<string>();
                if (File.Exists(cfgPath))
                {
                    lines.AddRange(ReadAllLinesShared(cfgPath));
                }
                else
                {
                    lines.Add("// generated by CitizenFX");
                    lines.Add("unbindall");
                }

                string formattedValue = fov.ToString("F6", CultureInfo.InvariantCulture);
                string newLine = $"seta \"cam_vehicleFirstPersonFOV\" \"{formattedValue}\"";

                int fovLineIndex = -1;
                for (int i = 0; i < lines.Count; i++)
                {
                    if (FovRegex.IsMatch(lines[i].Trim()))
                    {
                        fovLineIndex = i;
                        break;
                    }
                }

                if (fovLineIndex >= 0)
                {
                    lines[fovLineIndex] = newLine;
                }
                else
                {
                    int insertIndex = lines.FindIndex(l => l.Trim().Equals("unbindall", StringComparison.OrdinalIgnoreCase));
                    if (insertIndex >= 0 && insertIndex + 1 <= lines.Count)
                    {
                        lines.Insert(insertIndex + 1, newLine);
                    }
                    else
                    {
                        lines.Add(newLine);
                    }
                }

                File.WriteAllLines(cfgPath, lines);
                Log.Information("Updated fivem.cfg cam_vehicleFirstPersonFOV={Value}", formattedValue);
                statusMessage = fov < 0
                    ? "Driving FOV reset to Game Default (-1). Restart FiveM to apply."
                    : $"Driving FOV set to {fov:0.#}°. Restart FiveM to apply.";
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error writing cam_vehicleFirstPersonFOV to fivem.cfg");
                statusMessage = $"Failed to update driving FOV: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Reads the extended texture budget (vid_budgetScale) setting from fivem.cfg.
        /// Returns 0 as default if unconfigured or file is missing.
        /// </summary>
        public static int GetExtendedTextureBudget(string? customCfgPath = null)
        {
            try
            {
                string cfgPath = customCfgPath ?? GetFiveMCfgPath();
                if (File.Exists(cfgPath))
                {
                    string[] lines = ReadAllLinesShared(cfgPath);
                    foreach (string rawLine in lines)
                    {
                        string line = rawLine.Trim();
                        var match = BudgetScaleRegex.Match(line);
                        if (match.Success)
                        {
                            if (int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int budget))
                            {
                                return budget;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error reading vid_budgetScale from fivem.cfg");
            }

            return 0; // Default / unconfigured
        }

        /// <summary>
        /// Updates or sets the vid_budgetScale (Extended Texture Budget) setting in fivem.cfg.
        /// Allows bypassing FiveM's in-game cap of 20 (up to 40, 60, etc.) to fix disappearing textures with heavy mods.
        /// </summary>
        public static bool SetExtendedTextureBudget(int budgetScale, out string statusMessage, string? customCfgPath = null)
        {
            statusMessage = string.Empty;
            try
            {
                if (budgetScale < 0) budgetScale = 0;

                string cfgPath = customCfgPath ?? GetFiveMCfgPath();
                string? dir = Path.GetDirectoryName(cfgPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                List<string> lines = new List<string>();
                if (File.Exists(cfgPath))
                {
                    lines.AddRange(ReadAllLinesShared(cfgPath));
                }
                else
                {
                    lines.Add("// generated by CitizenFX");
                    lines.Add("unbindall");
                }

                string formattedValue = budgetScale.ToString(CultureInfo.InvariantCulture);
                string newLine = $"seta \"vid_budgetScale\" \"{formattedValue}\"";

                int budgetLineIndex = -1;
                for (int i = 0; i < lines.Count; i++)
                {
                    if (BudgetScaleRegex.IsMatch(lines[i].Trim()))
                    {
                        budgetLineIndex = i;
                        break;
                    }
                }

                if (budgetLineIndex >= 0)
                {
                    lines[budgetLineIndex] = newLine;
                }
                else
                {
                    int insertIndex = lines.FindIndex(l => l.Trim().Equals("unbindall", StringComparison.OrdinalIgnoreCase));
                    if (insertIndex >= 0 && insertIndex + 1 <= lines.Count)
                    {
                        lines.Insert(insertIndex + 1, newLine);
                    }
                    else
                    {
                        lines.Add(newLine);
                    }
                }

                File.WriteAllLines(cfgPath, lines);
                Log.Information("Updated fivem.cfg vid_budgetScale={Value}", formattedValue);
                statusMessage = budgetScale == 0
                    ? "Extended Texture Budget reset to Default (0). Restart FiveM to apply."
                    : $"Extended Texture Budget set to {budgetScale} (Bypass). Restart FiveM to apply.";
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error writing vid_budgetScale to fivem.cfg");
                statusMessage = $"Failed to update texture budget: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Gets the standard path to FiveM's gta5_settings.xml file under %APPDATA%\CitizenFX\gta5_settings.xml.
        /// </summary>
        public static string GetGta5SettingsXmlPath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CitizenFX", "gta5_settings.xml");
        }

        /// <summary>
        /// Deletes %APPDATA%\CitizenFX\gta5_settings.xml to restore FiveM graphic and display settings to original defaults.
        /// </summary>
        public static bool ResetGraphicsSettings(out string statusMessage, string? customPath = null)
        {
            statusMessage = string.Empty;
            try
            {
                string xmlPath = customPath ?? GetGta5SettingsXmlPath();
                if (File.Exists(xmlPath))
                {
                    File.Delete(xmlPath);
                    statusMessage = "Successfully reset FiveM graphic settings. FiveM will generate a clean gta5_settings.xml on next launch.";
                    Log.Information("Deleted FiveM graphic settings at {Path}", xmlPath);
                    return true;
                }
                else
                {
                    statusMessage = "gta5_settings.xml does not exist or was already reset.";
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error deleting FiveM gta5_settings.xml");
                statusMessage = $"Failed to reset graphic settings: {ex.Message}";
                return false;
            }
        }

        private static void UpdateIniKey(string iniPath, string sectionName, string keyName, string newValue)
        {
            string? dir = Path.GetDirectoryName(iniPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            List<string> lines = new List<string>();
            if (File.Exists(iniPath))
            {
                lines.AddRange(ReadAllLinesShared(iniPath));
            }

            string sectionHeader = $"[{sectionName}]";
            string keyPrefix = $"{keyName}=";

            int sectionIndex = -1;
            int keyIndex = -1;

            for (int i = 0; i < lines.Count; i++)
            {
                string trimmed = lines[i].Trim();
                if (trimmed.Equals(sectionHeader, StringComparison.OrdinalIgnoreCase))
                {
                    sectionIndex = i;
                }
                else if (sectionIndex >= 0 && trimmed.StartsWith(keyPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    keyIndex = i;
                    break;
                }
                else if (sectionIndex >= 0 && trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                {
                    break;
                }
            }

            string newLine = $"{keyName}={newValue}";

            if (keyIndex >= 0)
            {
                lines[keyIndex] = newLine;
            }
            else if (sectionIndex >= 0)
            {
                lines.Insert(sectionIndex + 1, newLine);
            }
            else
            {
                if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[lines.Count - 1]))
                {
                    lines.Add(string.Empty);
                }
                lines.Add(sectionHeader);
                lines.Add(newLine);
            }

            File.WriteAllLines(iniPath, lines);
            Log.Information("Updated CitizenFX.ini [{Section}] {Key}={Value}", sectionName, keyName, newValue);
        }

        public static string[] ReadAllLinesShared(string path)
        {
            if (!File.Exists(path))
                return Array.Empty<string>();

            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (StreamReader sr = new StreamReader(fs, System.Text.Encoding.UTF8))
            {
                List<string> lines = new List<string>();
                string? line;
                while ((line = sr.ReadLine()) != null)
                {
                    lines.Add(line);
                }
                return lines.ToArray();
            }
        }
    }

    /// <summary>
    /// Centralized settings persistence manager for GTAW Assistant and Parser.
    /// Stores settings as human-readable, version-agnostic JSON in %LOCALAPPDATA%\GTAW-Log-Parser\config\.
    /// Automatically imports legacy .NET user.config files across version upgrades and executable renames.
    /// </summary>
    public static class AppSettingsManager
    {
        private static readonly object SyncRoot = new object();
        private static readonly Dictionary<ApplicationSettingsBase, System.Configuration.SettingsSavingEventHandler> _settingsSavingHandlers
            = new Dictionary<ApplicationSettingsBase, System.Configuration.SettingsSavingEventHandler>();

        public static readonly string ConfigDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GTAW-Log-Parser",
            "config");

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        /// <summary>
        /// Initializes settings synchronization. Loads settings from JSON (or migrates legacy user.config),
        /// and wires automatic saving whenever Settings.Default.Save() is called.
        /// </summary>
        public static void Initialize(ApplicationSettingsBase settings, string configFileName, params string[] legacySearchPatterns)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (string.IsNullOrWhiteSpace(configFileName)) throw new ArgumentException("Config file name must be provided.", nameof(configFileName));

            Directory.CreateDirectory(ConfigDirectory);
            string configFilePath = Path.Combine(ConfigDirectory, configFileName);

            // 1. Load settings from JSON or migrate from legacy user.config
            Load(settings, configFilePath, legacySearchPatterns);

            // 2. Wire automatic JSON persistence whenever settings.Save() is invoked
            //    Unsubscribe any previous handler to avoid duplicates if Initialize is called multiple times.
            if (_settingsSavingHandlers.TryGetValue(settings, out var existingHandler))
            {
                settings.SettingsSaving -= existingHandler;
                _settingsSavingHandlers.Remove(settings);
            }
            System.Configuration.SettingsSavingEventHandler handler = (s, e) => Save(settings, configFilePath);
            settings.SettingsSaving += handler;
            _settingsSavingHandlers[settings] = handler;
        }

        /// <summary>
        /// Loads settings from JSON into the given ApplicationSettingsBase instance.
        /// If the JSON file does not exist, searches for and migrates legacy user.config files.
        /// </summary>
        public static void Load(ApplicationSettingsBase settings, string configFilePath, string[]? legacySearchPatterns = null)
        {
            lock (SyncRoot)
            {
                try
                {
                    if (File.Exists(configFilePath))
                    {
                        string json = File.ReadAllText(configFilePath, Encoding.UTF8);
                        ApplyJsonToSettings(settings, json);
                        Log.Information("Loaded application settings from {Path}", configFilePath);
                        return;
                    }

                    // JSON file does not exist yet. Attempt legacy migration.
                    bool migrated = false;

                    // First attempt .NET's built-in upgrade
                    try
                    {
                        settings.Upgrade();
                    }
                    catch (Exception ex)
                    {
                        Log.Debug(ex, "Built-in Settings.Upgrade() skipped or failed");
                    }

                    // Next, search for any legacy user.config files created by earlier versions
                    if (legacySearchPatterns != null && legacySearchPatterns.Length > 0)
                    {
                        migrated = TryMigrateLegacyUserConfigs(settings, legacySearchPatterns);
                    }

                    // Persist to JSON immediately so future starts load from JSON
                    Save(settings, configFilePath);
                    Log.Information("Established centralized settings at {Path} (MigratedFromLegacy: {Migrated})", configFilePath, migrated);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to load/initialize settings from {Path}", configFilePath);
                }
            }
        }

        /// <summary>
        /// Saves all settings from the given ApplicationSettingsBase instance to a JSON file.
        /// </summary>
        public static void Save(ApplicationSettingsBase settings, string configFilePath)
        {
            lock (SyncRoot)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(configFilePath)!);

                    var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    foreach (SettingsProperty prop in settings.Properties)
                    {
                        try
                        {
                            dict[prop.Name] = settings[prop.Name];
                        }
                        catch (Exception ex)
                        {
                            Log.Debug(ex, "Failed to read property {Name} for JSON serialization", prop.Name);
                        }
                    }

                    string json = JsonSerializer.Serialize(dict, JsonOptions);
                    string tempPath = configFilePath + ".tmp";
                    File.WriteAllText(tempPath, json, Encoding.UTF8);

                    if (File.Exists(configFilePath))
                    {
                        File.Delete(configFilePath);
                    }
                    File.Move(tempPath, configFilePath);

                    Log.Debug("Persisted application settings to {Path}", configFilePath);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to save settings to {Path}", configFilePath);
                }
            }
        }

        /// <summary>
        /// Deserializes a JSON dictionary and applies property values to the ApplicationSettingsBase instance.
        /// </summary>
        public static void ApplyJsonToSettings(ApplicationSettingsBase settings, string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return;

            using var doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return;

            foreach (SettingsProperty prop in settings.Properties)
            {
                if (root.TryGetProperty(prop.Name, out JsonElement element))
                {
                    try
                    {
                        object? val = ConvertJsonElementToType(element, prop.PropertyType);
                        if (val != null)
                        {
                            settings[prop.Name] = val;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Debug(ex, "Could not apply JSON property {Name} to type {Type}", prop.Name, prop.PropertyType);
                    }
                }
            }
        }

        /// <summary>
        /// Scans %LOCALAPPDATA% for older user.config files from previous version directories.
        /// </summary>
        private static bool TryMigrateLegacyUserConfigs(ApplicationSettingsBase settings, string[] searchPatterns)
        {
            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                List<string> candidateFiles = new List<string>();

                foreach (string pattern in searchPatterns)
                {
                    try
                    {
                        string[] matchedDirs = Directory.GetDirectories(localAppData, pattern, SearchOption.TopDirectoryOnly);
                        foreach (string dir in matchedDirs)
                        {
                            try
                            {
                                string[] configs = Directory.GetFiles(dir, "user.config", SearchOption.AllDirectories);
                                candidateFiles.AddRange(configs);
                            }
                            catch { }
                        }
                    }
                    catch { }
                }

                if (candidateFiles.Count == 0)
                    return false;

                // Sort by most recently modified first
                var sortedConfigs = candidateFiles
                    .Where(f => File.Exists(f))
                    .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                    .ToList();

                foreach (string configFile in sortedConfigs)
                {
                    try
                    {
                        string xml = File.ReadAllText(configFile, Encoding.UTF8);
                        var parsedDict = ParseLegacyUserConfigXml(xml);
                        if (parsedDict.Count > 0)
                        {
                            ApplyDictionaryToSettings(settings, parsedDict);
                            Log.Information("Successfully imported legacy settings from {Path} ({Count} keys)", configFile, parsedDict.Count);
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Debug(ex, "Failed to parse legacy user.config at {Path}", configFile);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error while searching for legacy user.config files");
            }

            return false;
        }

        /// <summary>
        /// Parses a standard .NET user.config XML file into key-value pairs.
        /// </summary>
        public static Dictionary<string, string> ParseLegacyUserConfigXml(string xmlContent)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(xmlContent)) return result;

            try
            {
                XDocument doc = XDocument.Parse(xmlContent);
                var settingElements = doc.Descendants("setting");

                foreach (var setting in settingElements)
                {
                    string? name = setting.Attribute("name")?.Value;
                    string? value = setting.Element("value")?.Value;

                    if (!string.IsNullOrWhiteSpace(name) && value != null)
                    {
                        result[name] = value;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Failed to parse XML content of user.config");
            }

            return result;
        }

        /// <summary>
        /// Applies string-based key-value pairs (e.g. from XML) to an ApplicationSettingsBase instance.
        /// </summary>
        public static void ApplyDictionaryToSettings(ApplicationSettingsBase settings, Dictionary<string, string> dict)
        {
            foreach (SettingsProperty prop in settings.Properties)
            {
                if (dict.TryGetValue(prop.Name, out string? strVal))
                {
                    try
                    {
                        object? converted = ConvertStringToType(strVal, prop.PropertyType);
                        if (converted != null)
                        {
                            settings[prop.Name] = converted;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Debug(ex, "Could not convert setting {Name} string '{Value}' to {Type}", prop.Name, strVal, prop.PropertyType);
                    }
                }
            }
        }

        /// <summary>
        /// Converts a string representation to the target type with invariant culture.
        /// </summary>
        public static object? ConvertStringToType(string value, Type targetType)
        {
            if (targetType == typeof(string))
                return value;

            if (string.IsNullOrWhiteSpace(value))
            {
                return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;
            }

            Type underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;

            if (underlying == typeof(bool))
            {
                if (bool.TryParse(value, out bool b)) return b;
                if (string.Equals(value, "True", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "1", StringComparison.Ordinal)) return true;
                if (string.Equals(value, "False", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "0", StringComparison.Ordinal)) return false;
            }

            if (underlying == typeof(int))
            {
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i)) return i;
            }

            if (underlying == typeof(double))
            {
                if (double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double d)) return d;
            }

            if (underlying == typeof(float))
            {
                if (float.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out float f)) return f;
            }

            if (underlying == typeof(long))
            {
                if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l)) return l;
            }

            if (underlying.IsEnum)
            {
                try { return Enum.Parse(underlying, value, true); }
                catch { }
            }

            try
            {
                TypeConverter converter = TypeDescriptor.GetConverter(underlying);
                if (converter.CanConvertFrom(typeof(string)))
                {
                    return converter.ConvertFrom(null, CultureInfo.InvariantCulture, value);
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Converts a JsonElement to the target type.
        /// </summary>
        public static object? ConvertJsonElementToType(JsonElement element, Type targetType)
        {
            Type underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;

            if (underlying == typeof(string))
            {
                return element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString();
            }

            if (underlying == typeof(bool))
            {
                if (element.ValueKind == JsonValueKind.True) return true;
                if (element.ValueKind == JsonValueKind.False) return false;
                if (element.ValueKind == JsonValueKind.String) return ConvertStringToType(element.GetString()!, typeof(bool));
            }

            if (underlying == typeof(int))
            {
                if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out int i)) return i;
                if (element.ValueKind == JsonValueKind.String) return ConvertStringToType(element.GetString()!, typeof(int));
            }

            if (underlying == typeof(double))
            {
                if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out double d)) return d;
                if (element.ValueKind == JsonValueKind.String) return ConvertStringToType(element.GetString()!, typeof(double));
            }

            if (underlying == typeof(float))
            {
                if (element.ValueKind == JsonValueKind.Number && element.TryGetSingle(out float f)) return f;
                if (element.ValueKind == JsonValueKind.String) return ConvertStringToType(element.GetString()!, typeof(float));
            }

            if (underlying == typeof(long))
            {
                if (element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out long l)) return l;
                if (element.ValueKind == JsonValueKind.String) return ConvertStringToType(element.GetString()!, typeof(long));
            }

            if (underlying.IsEnum)
            {
                string? str = element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString();
                if (!string.IsNullOrEmpty(str))
                {
                    try { return Enum.Parse(underlying, str, true); }
                    catch { }
                }
            }

            try
            {
                return JsonSerializer.Deserialize(element.GetRawText(), targetType);
            }
            catch
            {
                return ConvertStringToType(element.ToString(), targetType);
            }
        }
    }
}
