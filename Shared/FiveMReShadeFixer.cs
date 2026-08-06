using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Serilog;

namespace GTAWParser.Shared
{
    public static class FiveMReShadeFixer
    {
        private static readonly Regex ReShadeBypassRegex = new Regex(
            @"ReShade5=ID:[a-fA-F0-9]+\s+acknowledged that ReShade 5\.x has a bug that will lead to game crashes",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly string[] ReShadeFileNames = new[]
        {
            "dxgi.dll", "d3d11.dll", "dxgi.ini", "d3d11.ini",
            "ReShade.ini", "ReShade.log", "ReShadePreset.ini"
        };

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
                        // Check if file content contains [LOGGER] or [SYSTEM] or [OVERLAY] or ReShade headers
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
                        catch { }
                    }
                }

                if (itemsToMove.Count == 0)
                {
                    // Check if plugins already has dxgi.dll / reshade-shaders
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
        /// Scans the newest FiveM log file for the ReShade 5+ warning bypass line.
        /// </summary>
        public static bool ScanLogForReShadeBypass(string fivemDir, out string bypassLine, out string logFileName, out string statusMessage)
        {
            bypassLine = string.Empty;
            logFileName = string.Empty;
            statusMessage = string.Empty;

            try
            {
                FiveMPaths paths = FiveMDetector.ResolveFiveMPaths(fivemDir);
                if (!Directory.Exists(paths.LogsDirectory))
                {
                    statusMessage = "FiveM logs directory does not exist yet. Please launch FiveM once and try again.";
                    return false;
                }

                FileInfo[] logFiles = new DirectoryInfo(paths.LogsDirectory).GetFiles("*.log");
                if (logFiles.Length == 0)
                {
                    statusMessage = "No FiveM log files found. Please launch FiveM once to generate log files.";
                    return false;
                }

                // Sort newest log first
                Array.Sort(logFiles, (a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));

                foreach (FileInfo logFile in logFiles)
                {
                    using (FileStream fs = new FileStream(logFile.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (StreamReader reader = new StreamReader(fs))
                    {
                        string? line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            Match match = ReShadeBypassRegex.Match(line);
                            if (match.Success)
                            {
                                bypassLine = match.Value;
                                logFileName = logFile.Name;
                                statusMessage = $"Found ReShade bypass key in {logFile.Name}!";
                                Log.Information("Found ReShade bypass key: {Key} in {LogFile}", bypassLine, logFile.Name);
                                return true;
                            }
                        }
                    }
                }

                statusMessage = "ReShade warning string not found in logs yet. Please make sure FiveM was launched with ReShade installed, then close FiveM and try again.";
                return false;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error scanning log for ReShade bypass");
                statusMessage = $"Error reading log files: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Safely updates CitizenFX.ini to include the [Addons] section and ReShade bypass key.
        /// </summary>
        public static bool ApplyReShadeBypassToIni(string fivemDir, string bypassLine, out string statusMessage)
        {
            statusMessage = string.Empty;

            try
            {
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
                        // Encountered next section after [Addons]
                        break;
                    }
                }

                if (existingKeyIndex >= 0)
                {
                    lines[existingKeyIndex] = bypassLine;
                    statusMessage = "ReShade bypass key updated in CitizenFX.ini under [Addons].";
                }
                else if (addonsSectionIndex >= 0)
                {
                    lines.Insert(addonsSectionIndex + 1, bypassLine);
                    statusMessage = "ReShade bypass key added to existing [Addons] section in CitizenFX.ini.";
                }
                else
                {
                    if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[lines.Count - 1]))
                    {
                        lines.Add(string.Empty);
                    }
                    lines.Add("[Addons]");
                    lines.Add(bypassLine);
                    statusMessage = "Added [Addons] section and ReShade bypass key to CitizenFX.ini.";
                }

                File.WriteAllLines(iniPath, lines);
                Log.Information("CitizenFX.ini updated successfully with ReShade bypass key: {Key}", bypassLine);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to update CitizenFX.ini");
                statusMessage = $"Error updating CitizenFX.ini: {ex.Message}";
                return false;
            }
        }
    }
}
