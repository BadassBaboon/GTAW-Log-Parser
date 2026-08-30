using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Serilog;

namespace GTAWParser.Shared
{
    /// <summary>
    /// Centralized settings persistence manager for GTAW Assistant and Parser.
    /// Stores settings as human-readable, version-agnostic JSON in %LOCALAPPDATA%\GTAW-Log-Parser\config\.
    /// Automatically imports legacy .NET user.config files across version upgrades and executable renames.
    /// </summary>
    public static class AppSettingsManager
    {
        private static readonly object SyncRoot = new object();

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
            settings.SettingsSaving -= (s, e) => Save(settings, configFilePath);
            settings.SettingsSaving += (s, e) => Save(settings, configFilePath);
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
