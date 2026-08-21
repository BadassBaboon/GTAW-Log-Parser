using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using Serilog;

namespace GTAWParser.Shared
{
    public class FiveMPaths
    {
        public string RootDirectory { get; set; } = string.Empty;
        public string AppDataDirectory { get; set; } = string.Empty;
        public string PluginsDirectory { get; set; } = string.Empty;
        public string LogsDirectory { get; set; } = string.Empty;
        public string CitizenFXIniPath { get; set; } = string.Empty;
        public bool IsValid => !string.IsNullOrEmpty(RootDirectory) && Directory.Exists(RootDirectory);
    }

    public static class FiveMDetector
    {
        /// <summary>
        /// Probes Windows registry, drive roots, running processes, and local appdata to automatically detect the main FiveM directory (where FiveM.exe is located).
        /// </summary>
        public static string DetectFiveMDirectory()
        {
            try
            {
                // 1. HKCU\Software\CitizenFX\FiveM -> Last Run Location, TargetExePath, InstallFolder
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\CitizenFX\FiveM"))
                {
                    if (key != null)
                    {
                        string[] valueNames = new[] { "Last Run Location", "LastRunLocation", "TargetExePath", "InstallFolder" };
                        foreach (string vName in valueNames)
                        {
                            string? val = key.GetValue(vName) as string;
                            if (!string.IsNullOrEmpty(val))
                            {
                                val = val.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                                string? checkDir = File.Exists(val) ? Path.GetDirectoryName(val) : (Directory.Exists(val) ? val : null);
                                if (!string.IsNullOrEmpty(checkDir))
                                {
                                    string root = checkDir.EndsWith("FiveM.app", StringComparison.OrdinalIgnoreCase) || checkDir.EndsWith("FiveM Application Data", StringComparison.OrdinalIgnoreCase)
                                        ? Directory.GetParent(checkDir)?.FullName ?? checkDir
                                        : checkDir;

                                    if (File.Exists(Path.Combine(root, "FiveM.exe")) || Directory.Exists(Path.Combine(root, "FiveM.app")))
                                    {
                                        Log.Information("Detected FiveM path via HKCU CitizenFX {ValName}: {Path}", vName, root);
                                        return root;
                                    }
                                }
                            }
                        }
                    }
                }

                // 2. Probe System Drives for \FiveM\FiveM.exe (e.g. G:\FiveM, D:\FiveM, C:\FiveM)
                try
                {
                    foreach (DriveInfo drive in DriveInfo.GetDrives())
                    {
                        if (drive.IsReady && (drive.DriveType == DriveType.Fixed || drive.DriveType == DriveType.Removable))
                        {
                            string candidate = Path.Combine(drive.RootDirectory.FullName, "FiveM");
                            if (File.Exists(Path.Combine(candidate, "FiveM.exe")))
                            {
                                Log.Information("Detected FiveM path via drive probe: {Path}", candidate);
                                return candidate;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "FiveM drive probe encountered an exception");
                }

                // 3. Running Process Probe (e.g. FiveM.exe, FiveM_ROSLauncher.exe)
                foreach (string procName in new[] { "FiveM", "FiveM_ROSLauncher", "FiveM_GTAProcess" })
                {
                    try
                    {
                        Process[] procs = Process.GetProcessesByName(procName);
                        foreach (Process p in procs)
                        {
                            string? mainModule = p.MainModule?.FileName;
                            if (!string.IsNullOrEmpty(mainModule) && File.Exists(mainModule))
                            {
                                string? dir = Path.GetDirectoryName(mainModule);
                                while (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                                {
                                    if (File.Exists(Path.Combine(dir, "FiveM.exe")))
                                    {
                                        Log.Information("Detected FiveM path via active process {Proc}: {Path}", procName, dir);
                                        return dir;
                                    }
                                    dir = Directory.GetParent(dir)?.FullName;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Debug(ex, "FiveM process probe for {Proc} encountered an exception", procName);
                    }
                }

                // 4. HKCU Uninstall key
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\FiveM"))
                {
                    if (key != null)
                    {
                        string? location = key.GetValue("InstallLocation") as string;
                        if (!string.IsNullOrEmpty(location) && Directory.Exists(location))
                        {
                            if (File.Exists(Path.Combine(location, "FiveM.exe")))
                            {
                                Log.Information("Detected FiveM path via HKCU Uninstall: {Path}", location);
                                return location;
                            }
                        }
                    }
                }

                // 5. Fallback: %LocalAppData%\FiveM
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string defaultFiveMPath = Path.Combine(localAppData, "FiveM");
                if (Directory.Exists(defaultFiveMPath))
                {
                    Log.Information("Detected FiveM path via LocalAppData default: {Path}", defaultFiveMPath);
                    return defaultFiveMPath;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error detecting FiveM directory");
            }

            return string.Empty;
        }

        /// <summary>
        /// Normalizes any input directory path (root, FiveM.app, or logs) into a structured <see cref="FiveMPaths"/> object.
        /// </summary>
        public static FiveMPaths ResolveFiveMPaths(string inputPath)
        {
            FiveMPaths paths = new FiveMPaths();
            if (string.IsNullOrWhiteSpace(inputPath))
                return paths;

            string cleanPath = inputPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!Directory.Exists(cleanPath))
                return paths;

            string dirName = Path.GetFileName(cleanPath);
            if (dirName.Equals("FiveM.app", StringComparison.OrdinalIgnoreCase) || 
                dirName.Equals("FiveM Application Data", StringComparison.OrdinalIgnoreCase))
            {
                paths.AppDataDirectory = cleanPath;
                paths.RootDirectory = Directory.GetParent(cleanPath)?.FullName ?? cleanPath;
            }
            else if (dirName.Equals("logs", StringComparison.OrdinalIgnoreCase) || dirName.Equals("plugins", StringComparison.OrdinalIgnoreCase))
            {
                string? parent = Directory.GetParent(cleanPath)?.FullName;
                if (parent != null && (Path.GetFileName(parent).Equals("FiveM.app", StringComparison.OrdinalIgnoreCase) || 
                                       Path.GetFileName(parent).Equals("FiveM Application Data", StringComparison.OrdinalIgnoreCase)))
                {
                    paths.AppDataDirectory = parent;
                    paths.RootDirectory = Directory.GetParent(parent)?.FullName ?? parent;
                }
                else
                {
                    paths.RootDirectory = cleanPath;
                }
            }
            else
            {
                paths.RootDirectory = cleanPath;
                string appDataFiveMApp = Path.Combine(cleanPath, "FiveM.app");
                string appDataFiveMData = Path.Combine(cleanPath, "FiveM Application Data");

                if (Directory.Exists(appDataFiveMApp))
                {
                    paths.AppDataDirectory = appDataFiveMApp;
                }
                else if (Directory.Exists(appDataFiveMData))
                {
                    paths.AppDataDirectory = appDataFiveMData;
                }
                else
                {
                    string localAppDataApp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FiveM", "FiveM.app");
                    if (Directory.Exists(localAppDataApp))
                    {
                        paths.AppDataDirectory = localAppDataApp;
                    }
                    else
                    {
                        paths.AppDataDirectory = appDataFiveMApp;
                    }
                }
            }

            if (!string.IsNullOrEmpty(paths.AppDataDirectory))
            {
                paths.PluginsDirectory = Path.Combine(paths.AppDataDirectory, "plugins");
                paths.LogsDirectory = Path.Combine(paths.AppDataDirectory, "logs");
                paths.CitizenFXIniPath = Path.Combine(paths.AppDataDirectory, "CitizenFX.ini");
            }

            return paths;
        }

        /// <summary>
        /// Launches FiveM and connects to the specified server address via the official shell URI protocol.
        /// Using explorer.exe / ShellExecute ensures FiveM's shell validation check succeeds.
        /// </summary>
        public static bool LaunchFiveMAndConnect(string serverAddress = "fivem.gta.world", string? customFiveMDir = null)
        {
            try
            {
                string uri = $"fivem://connect/{serverAddress}";
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = uri,
                    UseShellExecute = true
                };
                Process.Start(psi);
                Log.Information("Launched FiveM via Windows Shell protocol {Uri}", uri);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to launch FiveM via shell protocol for {Server}", serverAddress);
                try
                {
                    ProcessStartInfo fallbackPsi = new ProcessStartInfo
                    {
                        FileName = $"fivem://connect/{serverAddress}",
                        UseShellExecute = true
                    };
                    Process.Start(fallbackPsi);
                    return true;
                }
                catch (Exception innerEx)
                {
                    Log.Error(innerEx, "Fallback launch also failed for {Server}", serverAddress);
                    return false;
                }
            }
        }

        /// <summary>
        /// Checks whether any FiveM process (FiveM, FiveM_ROSLauncher, FiveM_GTAProcess, FiveM_ChromeBrowser) is currently running.
        /// </summary>
        public static bool IsFiveMRunning()
        {
            try
            {
                return Process.GetProcessesByName("FiveM").Length > 0 ||
                       Process.GetProcessesByName("FiveM_ROSLauncher").Length > 0 ||
                       Process.GetProcessesByName("FiveM_GTAProcess").Length > 0 ||
                       Process.GetProcessesByName("FiveM_ChromeBrowser").Length > 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
