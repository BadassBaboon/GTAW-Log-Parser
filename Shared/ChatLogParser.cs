using System;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Serilog;

namespace GTAWParser.Shared
{
    /// <summary>
    /// Extracts the chat log from a GTA World <c>.storage</c> file.
    /// </summary>
    public static class ChatLogParser
    {
        public static readonly Regex TimestampRegex = new Regex(@"\[\d{1,2}:\d{1,2}:\d{1,2}\] ", RegexOptions.Compiled);

        /// <summary>
        /// Streams the .storage file under <paramref name="directoryPath"/> and
        /// returns the decoded chat log (newlines intact, timestamps preserved,
        /// HTML entities decoded). On failure invokes <paramref name="onError"/>
        /// with the exception (or null when the chat_log field was absent) and
        /// returns empty.
        /// </summary>
        public static string Parse(string directoryPath, Action<Exception?>? onError = null)
        {
            try
            {
                string storagePath = Path.Combine(directoryPath, ChatLogScanner.LogLocation);
                byte[] bytes = File.ReadAllBytes(storagePath);

                Utf8JsonReader reader = new Utf8JsonReader(bytes);
                while (reader.Read())
                {
                    if (reader.TokenType != JsonTokenType.PropertyName) continue;
                    if (!reader.ValueTextEquals("chat_log")) continue;

                    if (!reader.Read() || reader.TokenType != JsonTokenType.String)
                        break;

                    string? log = reader.GetString();
                    if (string.IsNullOrWhiteSpace(log))
                        break;

                    // Trim the trailing newline that the JSON value carries
                    // (preserves parity with the old regex which excluded it).
                    if (log.EndsWith("\n", StringComparison.Ordinal))
                        log = log.Substring(0, log.Length - 1);

                    return WebUtility.HtmlDecode(log);
                }

                onError?.Invoke(null);
                return string.Empty;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ChatLogParser.Parse failed");
                onError?.Invoke(ex);
                return string.Empty;
            }
        }

        /// <summary>
        /// Strips <c>[HH:MM:SS]</c> timestamps from a parsed log.
        /// </summary>
        public static string StripTimestamps(string log) =>
            string.IsNullOrEmpty(log) ? log : TimestampRegex.Replace(log, string.Empty);
    }
}
