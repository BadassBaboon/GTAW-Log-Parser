using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace GTAWParser.Shared
{
    /// <summary>
    /// Discovers the most recently used GTA World <c>.storage</c> file under
    /// a RAGEMP installation directory by inspecting every
    /// <c>client_resources\*\.storage</c> for the GTA World server tag.
    /// </summary>
    public static class ChatLogScanner
    {
        private const string DefaultServer = "play.gta.world_22005";
        private static readonly Regex ServerTag = new Regex("\"server_version\":\"GTA World[^\"]*\"", RegexOptions.Compiled);

        public static string ResourceDirectory { get; private set; } = "Not Found";
        public static string LogLocation { get; private set; } = Path.Combine("client_resources", DefaultServer, ".storage");

        /// <summary>
        /// Scans <paramref name="directoryPath"/> for the newest GTA World
        /// .storage file and updates <see cref="ResourceDirectory"/> +
        /// <see cref="LogLocation"/> to point at it. Silent on failure (writes
        /// to Debug).
        /// </summary>
        public static void InitializeServerIp(string directoryPath)
        {
            ResourceDirectory = "Not Found";
            LogLocation = Path.Combine("client_resources", DefaultServer, ".storage");

            try
            {
                if (string.IsNullOrWhiteSpace(directoryPath)) return;

                string clientResources = Path.Combine(directoryPath, "client_resources");
                if (!Directory.Exists(clientResources)) return;

                List<string> potentialLogs = new List<string>();
                foreach (string resourceDirectory in Directory.GetDirectories(clientResources))
                {
                    string storagePath = Path.Combine(resourceDirectory, ".storage");
                    if (!File.Exists(storagePath))
                        continue;

                    string log;
                    using (StreamReader sr = new StreamReader(storagePath))
                        log = sr.ReadToEnd();

                    if (!ServerTag.IsMatch(log))
                        continue;

                    potentialLogs.Add(resourceDirectory);
                }

                if (potentialLogs.Count == 0) return;

                while (potentialLogs.Count > 1)
                {
                    DateTime t0 = File.GetLastWriteTimeUtc(Path.Combine(potentialLogs[0], ".storage"));
                    DateTime t1 = File.GetLastWriteTimeUtc(Path.Combine(potentialLogs[1], ".storage"));
                    potentialLogs.Remove(t0 > t1 ? potentialLogs[1] : potentialLogs[0]);
                }

                string finalName = Path.GetFileName(potentialLogs[0]);
                if (string.IsNullOrEmpty(finalName)) return;

                ResourceDirectory = finalName;
                LogLocation = Path.Combine("client_resources", finalName, ".storage");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"InitializeServerIp failed: {ex}");
            }
        }
    }
}
