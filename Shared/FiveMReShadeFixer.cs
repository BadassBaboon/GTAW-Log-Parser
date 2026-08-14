using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Serilog;

namespace GTAWParser.Shared
{
    public static class FiveMReShadeFixer
    {
        private static readonly Regex ReShadeKeyRegex = new Regex(
            @"ReShade5=ID:[a-fA-F0-9]+\s+acknowledged that ReShade 5\.x has a bug that will lead to game crashes",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly string[] ReShadeFileNames = new[]
        {
            "dxgi.dll", "d3d11.dll", "dxgi.ini", "d3d11.ini",
            "ReShade.ini", "ReShade.log", "ReShadePreset.ini"
        };

        /// <summary>
        /// Computes Jenkins One-at-a-Time Hash for a string (used by FiveM for computer ID hashing).
        /// </summary>
        public static uint HashString(string str)
        {
            uint hash = 0;
            foreach (char c in str)
            {
                hash += (byte)c;
                hash += (hash << 10);
                hash ^= (hash >> 6);
            }
            hash += (hash << 3);
            hash ^= (hash >> 11);
            hash += (hash << 15);
            return hash;
        }

        /// <summary>
        /// Generates the exact ReShade 5+ acknowledgment line based on the machine name hash.
        /// </summary>
        public static string GenerateReShadeAckLine()
        {
            uint hash = HashString(Environment.MachineName.ToLowerInvariant());
            return $"ReShade5=ID:{hash:x8} acknowledged that ReShade 5.x has a bug that will lead to game crashes";
        }

        // Backward-compatibility alias
        public static string GenerateReShadeBypassLine() => GenerateReShadeAckLine();

        /// <summary>
        /// Moves ReShade files and folders from FiveM root directory into FiveM.app\plugins.
        /// </summary>
        public static bool MoveReShadeFilesToPlugins(string fivemDir, out int movedCount, out string statusMessage)
        {
            movedCount = 0;
            statusMessage = string.Empty;

            try
            {
                FiveMPaths paths = FiveMDetector.ResolveFiveMPaths(fivemDir);
                if (!paths.IsValid)
                {
                    statusMessage = "Invalid FiveM directory path specified.";
                    return false;
                }

                if (!Directory.Exists(paths.PluginsDirectory))
                {
                    Directory.CreateDirectory(paths.PluginsDirectory);
                    Log.Information("Created plugins directory: {Path}", paths.PluginsDirectory);
                }

                List<string> itemsToMove = new List<string>();

                // Check for reshade-shaders folder in root
                string reshadeFolder = Path.Combine(paths.RootDirectory, "reshade-shaders");
                if (Directory.Exists(reshadeFolder))
                {
                    itemsToMove.Add(reshadeFolder);
                }

                // Check for known ReShade files in root
                foreach (string fileName in ReShadeFileNames)
                {
                    string filePath = Path.Combine(paths.RootDirectory, fileName);
                    if (File.Exists(filePath))
                    {
                        itemsToMove.Add(filePath);
                    }
                }

                // Check for any *.ini preset files in root that might be ReShade presets
                foreach (string iniFile in Directory.GetFiles(paths.RootDirectory, "*.ini"))
                {
                    string name = Path.GetFileName(iniFile);
                    if (!name.Equals("CitizenFX.ini", StringComparison.OrdinalIgnoreCase) &&
                        !itemsToMove.Contains(iniFile))
                    {
                        try
                        {
                            string content = File.ReadAllText(iniFile);
                            if (content.Contains("ReShade", StringComparison.OrdinalIgnoreCase) ||
                                content.Contains("PreprocessorDefinitions", StringComparison.OrdinalIgnoreCase) ||
                                content.Contains("Techniques=", StringComparison.OrdinalIgnoreCase))
                            {
                                itemsToMove.Add(iniFile);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Debug(ex, "Failed to read INI file {Ini} during ReShade probe", iniFile);
                        }
                    }
                }

                if (itemsToMove.Count == 0)
                {
                    bool alreadyInPlugins = File.Exists(Path.Combine(paths.PluginsDirectory, "dxgi.dll")) ||
                                            File.Exists(Path.Combine(paths.PluginsDirectory, "d3d11.dll")) ||
                                            Directory.Exists(Path.Combine(paths.PluginsDirectory, "reshade-shaders"));

                    if (alreadyInPlugins)
                    {
                        statusMessage = "ReShade files are already in FiveM.app\\plugins!";
                        return true;
                    }

                    statusMessage = "No ReShade files found in FiveM root directory. Please run the ReShade installer and select FiveM.exe first.";
                    return false;
                }

                foreach (string item in itemsToMove)
                {
                    string dest = Path.Combine(paths.PluginsDirectory, Path.GetFileName(item));
                    if (Directory.Exists(item))
                    {
                        if (Directory.Exists(dest))
                            Directory.Delete(dest, true);
                        Directory.Move(item, dest);
                        movedCount++;
                        Log.Information("Moved ReShade directory {Source} -> {Dest}", item, dest);
                    }
                    else if (File.Exists(item))
                    {
                        if (File.Exists(dest))
                            File.Delete(dest);
                        File.Move(item, dest);
                        movedCount++;
                        Log.Information("Moved ReShade file {Source} -> {Dest}", item, dest);
                    }
                }

                statusMessage = $"Successfully moved {movedCount} ReShade file(s)/folder(s) to FiveM.app\\plugins!";
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to move ReShade files to plugins");
                statusMessage = $"Error moving files: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Scans the newest FiveM log file for the ReShade 5+ warning line. Fallback uses machine name hash.
        /// </summary>
        public static bool ScanLogForReShadeKey(string fivemDir, out string ackLine, out string logFileName, out string statusMessage)
        {
            ackLine = string.Empty;
            logFileName = string.Empty;
            statusMessage = string.Empty;

            try
            {
                FiveMPaths paths = FiveMDetector.ResolveFiveMPaths(fivemDir);
                if (Directory.Exists(paths.LogsDirectory))
                {
                    FileInfo[] logFiles = new DirectoryInfo(paths.LogsDirectory).GetFiles("*.log");
                    if (logFiles.Length > 0)
                    {
                        Array.Sort(logFiles, (a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
                        foreach (FileInfo logFile in logFiles)
                        {
                            using (FileStream fs = new FileStream(logFile.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                            using (StreamReader reader = new StreamReader(fs))
                            {
                                string? line;
                                while ((line = reader.ReadLine()) != null)
                                {
                                    Match match = ReShadeKeyRegex.Match(line);
                                    if (match.Success)
                                    {
                                        ackLine = match.Value;
                                        logFileName = logFile.Name;
                                        statusMessage = $"Found ReShade key in {logFile.Name}!";
                                        Log.Information("Found ReShade key: {Key} in {LogFile}", ackLine, logFile.Name);
                                        return true;
                                    }
                                }
                            }
                        }
                    }
                }

                // Fallback: Generate line directly using machine name hash
                ackLine = GenerateReShadeAckLine();
                logFileName = "AutoGenerated";
                statusMessage = "Auto-generated ReShade key using computer hardware hash.";
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error scanning log for ReShade key, using machine hash fallback");
                ackLine = GenerateReShadeAckLine();
                logFileName = "AutoGenerated";
                statusMessage = "Auto-generated ReShade key using computer hardware hash.";
                return true;
            }
        }

        // Backward-compatibility alias
        public static bool ScanLogForReShadeBypass(string fivemDir, out string bypassLine, out string logFileName, out string statusMessage)
            => ScanLogForReShadeKey(fivemDir, out bypassLine, out logFileName, out statusMessage);

        /// <summary>
        /// Safely updates CitizenFX.ini to include the [Addons] section and ReShade key.
        /// </summary>
        public static bool ApplyReShadeKeyToIni(string fivemDir, string ackLine, out string statusMessage)
        {
            statusMessage = string.Empty;

            try
            {
                if (string.IsNullOrWhiteSpace(fivemDir) || !Directory.Exists(fivemDir))
                {
                    statusMessage = "FiveM installation directory is invalid or not found.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(ackLine))
                {
                    ackLine = GenerateReShadeAckLine();
                }

                FiveMPaths paths = FiveMDetector.ResolveFiveMPaths(fivemDir);
                string iniPath = paths.CitizenFXIniPath;

                string? iniDir = Path.GetDirectoryName(iniPath);
                if (!string.IsNullOrEmpty(iniDir) && !Directory.Exists(iniDir))
                {
                    Directory.CreateDirectory(iniDir);
                }

                List<string> lines = new List<string>();
                if (File.Exists(iniPath))
                {
                    lines.AddRange(File.ReadAllLines(iniPath));
                }

                int addonsSectionIndex = -1;
                int existingKeyIndex = -1;

                for (int i = 0; i < lines.Count; i++)
                {
                    string trimmed = lines[i].Trim();
                    if (trimmed.Equals("[Addons]", StringComparison.OrdinalIgnoreCase))
                    {
                        addonsSectionIndex = i;
                    }
                    else if (addonsSectionIndex >= 0 && trimmed.StartsWith("ReShade5=", StringComparison.OrdinalIgnoreCase))
                    {
                        existingKeyIndex = i;
                    }
                    else if (addonsSectionIndex >= 0 && trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                    {
                        break;
                    }
                }

                if (existingKeyIndex >= 0)
                {
                    lines[existingKeyIndex] = ackLine;
                    statusMessage = "ReShade key updated in CitizenFX.ini under [Addons].";
                }
                else if (addonsSectionIndex >= 0)
                {
                    lines.Insert(addonsSectionIndex + 1, ackLine);
                    statusMessage = "ReShade key added to existing [Addons] section in CitizenFX.ini.";
                }
                else
                {
                    if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[lines.Count - 1]))
                    {
                        lines.Add(string.Empty);
                    }
                    lines.Add("[Addons]");
                    lines.Add(ackLine);
                    statusMessage = "Added [Addons] section and ReShade key to CitizenFX.ini.";
                }

                File.WriteAllLines(iniPath, lines);
                Log.Information("CitizenFX.ini updated successfully with ReShade key: {Key}", ackLine);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to update CitizenFX.ini");
                statusMessage = $"Error updating CitizenFX.ini: {ex.Message}";
                return false;
            }
        }

        // Backward-compatibility alias
        public static bool ApplyReShadeBypassToIni(string fivemDir, string bypassLine, out string statusMessage)
            => ApplyReShadeKeyToIni(fivemDir, bypassLine, out statusMessage);
    }
}
