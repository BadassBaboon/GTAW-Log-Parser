using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Assistant.Localization;
using GTAWParser.Shared;

namespace Assistant.Controllers
{
    public static class AppController
    {
        public const string AssemblyVersion = "6.3.1";
        public static readonly string Version = $"v{AssemblyVersion}";
        public static bool IsBetaVersion => false;
        public static bool CanFollowSystemColor = false;
        public static bool CanFollowSystemMode = false;

        public const string ParameterPrefix = "--";
        public const string MutexName = "GTAWChatLogAssistant";
        public static readonly string[] ProcessNames = { "FiveM", "FiveM_ROSLauncher", "FiveM_GTAProcess", "GTA5", "GTA5_Enhanced" };
        public const string ProductHeader = "GTAW-FiveM-Log-Parser";
        public const string GitHubOwner = "BadassBaboon";
        public const string GitHubRepo = "GTAW-Log-Parser";

        public static string ResourceDirectory => "FiveM local NUI chat";
        public static string LogLocation => FiveMChatCaptureService.SessionFilePath;

        public static readonly string ExecutablePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
        public static readonly string StartupPath = (!string.IsNullOrEmpty(ExecutablePath) ? Path.GetDirectoryName(ExecutablePath) : null) ?? AppContext.BaseDirectory;
        public static string PreviousLog = string.Empty;

        public static bool IsFiveMRunning() => FiveMDetector.IsFiveMRunning();

        /// <summary>
        /// Initializes the FiveM NUI chat capture service.
        /// </summary>
        public static void InitializeServerIp()
        {
            ServerTimezoneHelper.AutoDetect = Properties.Settings.Default.AutoDetectServerTimezone;
            string tz = Properties.Settings.Default.ServerTimezone;
            if (!string.IsNullOrEmpty(tz))
            {
                ServerTimezoneHelper.CurrentTimezoneSetting = tz;
            }
            FiveMChatCaptureService.Initialize();
        }

        /// <summary>
        /// Parses the current captured FiveM chat log and caches the timestamped version in <see cref="PreviousLog"/>.
        /// </summary>
        public static string ParseChatLog(bool removeTimestamps, bool showError = false)
        {
            string log = FiveMChatCaptureService.ReadCapturedChat(false);

            if (string.IsNullOrWhiteSpace(log))
            {
                if (showError && !Properties.Settings.Default.DisableErrorPopups)
                {
                    MessageBox.Show(
                        "No FiveM GTAW chat is currently available. Open GTAW on FiveM and wait for its HUD to load.",
                        Strings.Error,
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                return string.Empty;
            }

            PreviousLog = log;
            return removeTimestamps ? FiveMChatCaptureService.ReadCapturedChat(true) : log;
        }

        /// <summary>
        /// Migrates legacy scattered AppData directories (Local\GTAW-Log-Parser-FiveM, Roaming\GTAWChatLogAssistant)
        /// into the single unified directory: Local\GTAW-Log-Parser\
        /// </summary>
        public static void MigrateLegacyAppDataDirectories()
        {
            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string roamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

                string unifiedBase = Path.Combine(localAppData, "GTAW-Log-Parser");
                string sessionDir = Path.Combine(unifiedBase, "session");
                string configDir = Path.Combine(unifiedBase, "config");
                string rollbackDir = Path.Combine(unifiedBase, "rollback");

                Directory.CreateDirectory(sessionDir);
                Directory.CreateDirectory(configDir);
                Directory.CreateDirectory(rollbackDir);

                // 1. Migrate Local\GTAW-Log-Parser-FiveM -> Local\GTAW-Log-Parser\session and rollback
                string legacyLocalFiveM = Path.Combine(localAppData, "GTAW-Log-Parser-FiveM");
                if (Directory.Exists(legacyLocalFiveM))
                {
                    string legacyCurrent = Path.Combine(legacyLocalFiveM, "current-session.txt");
                    string targetCurrent = Path.Combine(sessionDir, "current-session.txt");
                    if (File.Exists(legacyCurrent) && !File.Exists(targetCurrent))
                    {
                        File.Move(legacyCurrent, targetCurrent);
                    }

                    string legacyPrevious = Path.Combine(legacyLocalFiveM, "previous-session.txt");
                    string targetPrevious = Path.Combine(sessionDir, "previous-session.txt");
                    if (File.Exists(legacyPrevious) && !File.Exists(targetPrevious))
                    {
                        File.Move(legacyPrevious, targetPrevious);
                    }

                    string legacyRollback = Path.Combine(legacyLocalFiveM, "Rollback");
                    if (Directory.Exists(legacyRollback))
                    {
                        foreach (string file in Directory.GetFiles(legacyRollback))
                        {
                            string dest = Path.Combine(rollbackDir, Path.GetFileName(file));
                            if (!File.Exists(dest)) File.Move(file, dest);
                        }
                        Directory.Delete(legacyRollback, true);
                    }

                    try
                    {
                        Directory.Delete(legacyLocalFiveM, true);
                    }
                    catch { }
                }

                // 2. Migrate Roaming\GTAWChatLogAssistant -> Local\GTAW-Log-Parser\config
                string legacyRoaming = Path.Combine(roamingAppData, "GTAWChatLogAssistant");
                if (Directory.Exists(legacyRoaming))
                {
                    string legacySettings = Path.Combine(legacyRoaming, "ai_settings.json");
                    string targetSettings = Path.Combine(configDir, "ai_settings.json");
                    if (File.Exists(legacySettings) && !File.Exists(targetSettings))
                    {
                        File.Move(legacySettings, targetSettings);
                    }

                    try
                    {
                        Directory.Delete(legacyRoaming, true);
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Debug(ex, "Failed to complete legacy AppData migration");
            }
        }
    }
}
