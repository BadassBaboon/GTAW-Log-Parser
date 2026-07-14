using GTAWParser.Shared;
using Parser.Localization;
using System.Windows.Forms;

namespace Parser.Controllers
{
    public static class ProgramController
    {
        public const string AssemblyVersion = "6.0.0";
        public static readonly string Version = $"v{AssemblyVersion}";
        public static bool IsBetaVersion => false;
        public const string ParameterPrefix = "--";
        public const string MutexName = "GTAWParserMini";

        public static string ResourceDirectory => ChatLogScanner.ResourceDirectory;
        public static string LogLocation => ChatLogScanner.LogLocation;

        /// <summary>
        /// Probes the configured RAGEMP directory for the most recent
        /// GTA World .storage file and updates the log location.
        /// </summary>
        public static void InitializeServerIp()
        {
            ChatLogScanner.InitializeServerIp(Properties.Settings.Default.DirectoryPath);
        }

        /// <summary>
        /// Parses the most recent chat log under <paramref name="directoryPath"/>.
        /// Displays a localized MessageBox on failure.
        /// </summary>
        public static string ParseChatLog(string directoryPath, bool removeTimestamps)
        {
            string log = ChatLogParser.Parse(directoryPath, _ =>
            {
                MessageBox.Show(Strings.ParseError, Strings.Error, MessageBoxButtons.OK, MessageBoxIcon.Error);
            });

            return removeTimestamps ? ChatLogParser.StripTimestamps(log) : log;
        }
    }
}
