using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using GTAWParser.Shared;
using Parser.Localization;

namespace Parser.Controllers
{
    public static class ProgramController
    {
        public const string AssemblyVersion = "6.1.0";
        public static readonly string Version = $"v{AssemblyVersion}";
        public static bool IsBetaVersion => false;
        public const string ParameterPrefix = "--";
        public const string MutexName = "GTAWParserMini";

        public static string ResourceDirectory => "FiveM local NUI chat";
        public static string LogLocation => FiveMChatCaptureService.SessionFilePath;

        public static void InitializeServerIp()
        {
            // Maintained for lifecycle compatibility
        }

        /// <summary>
        /// Reads the chat currently visible in GTAW's FiveM HUD on-demand.
        /// </summary>
        public static string ParseChatLog(bool removeTimestamps)
        {
            try
            {
                string sessionChat = FiveMChatCaptureService.ReadCapturedChat(removeTimestamps);
                if (!string.IsNullOrWhiteSpace(sessionChat))
                    return sessionChat;

                List<string> lines = FiveMChatCaptureService.GetVisibleChatLinesAsync().GetAwaiter().GetResult();
                if (lines.Count == 0)
                    throw new System.IO.IOException("No chat lines captured.");

                DateTime capturedAt = DateTime.Now;
                DateTime sessionTimestamp = FiveMChatCaptureService.GetTimestamp(lines[0], capturedAt);
                string log = FiveMChatCaptureService.CreateSessionHeader(sessionTimestamp) + "\n" +
                             string.Join("\n", lines.Select(line => FiveMChatCaptureService.AddTimestamp(line, capturedAt)));

                if (removeTimestamps)
                    log = FiveMChatCaptureService.ReadCapturedChat(true);

                return log;
            }
            catch
            {
                MessageBox.Show(
                    "No FiveM GTAW chat is currently available. Open GTAW on FiveM and wait for its HUD to load.",
                    Strings.Error,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return string.Empty;
            }
        }
    }
}
