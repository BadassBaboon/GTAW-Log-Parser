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
        public const string AssemblyVersion = "6.3.0";
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
    }
}
