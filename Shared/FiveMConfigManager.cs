using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
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
                    string[] lines = File.ReadAllLines(paths.CitizenFXIniPath);
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
                    string[] lines = File.ReadAllLines(paths.CitizenFXIniPath);
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
                    string[] lines = File.ReadAllLines(cfgPath);
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
                    lines.AddRange(File.ReadAllLines(cfgPath));
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
                lines.AddRange(File.ReadAllLines(iniPath));
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
    }
}
