using System.Diagnostics;
using System.IO;
using System.Windows;
using Assistant.Localization;
using GTAWParser.Shared;

namespace Assistant.Controllers
{
    public static class AppController
    {
        public const string AssemblyVersion = "4.1.8";
        public static readonly string Version = $"v{AssemblyVersion}";
        public static bool IsBetaVersion => false;
        public static bool CanFollowSystemColor = false;
        public static bool CanFollowSystemMode = false;

        public const string ParameterPrefix = "--";
        public const string MutexName = "GTAWChatLogAssistant";
        public static readonly string[] ProcessNames = { "GTA5", "GTA5_Enhanced" };
        public const string ProductHeader = "GTAW-Log-Parser";
        public const string GitHubOwner = "blancodagoat";
        public const string GitHubRepo = "GTAW-Log-Parser";

        public static string ResourceDirectory => ChatLogScanner.ResourceDirectory;
        public static string LogLocation => ChatLogScanner.LogLocation;

        public static readonly string ExecutablePath = Process.GetCurrentProcess().MainModule?.FileName;
        public static readonly string StartupPath = Path.GetDirectoryName(ExecutablePath);
        public static string PreviousLog = string.Empty;

        /// <summary>
        /// Probes the configured RAGEMP directory for the most recent
        /// GTA World .storage file and updates the log location.
        /// </summary>
        public static void InitializeServerIp()
        {
            ChatLogScanner.InitializeServerIp(Properties.Settings.Default.DirectoryPath);
        }

        /// <summary>
        /// Parses the most recent chat log at <paramref name="directoryPath"/>
        /// and caches the timestamped version in <see cref="PreviousLog"/>.
        /// </summary>
        public static string ParseChatLog(string directoryPath, bool removeTimestamps, bool showError = false)
        {
            string log = ChatLogParser.Parse(directoryPath, _ =>
            {
                if (showError)
                    MessageBox.Show(Strings.ParseError, Strings.Error, MessageBoxButton.OK, MessageBoxImage.Error);
            });

            if (!string.IsNullOrEmpty(log))
                PreviousLog = log;

            return removeTimestamps ? ChatLogParser.StripTimestamps(log) : log;
        }
    }
}
