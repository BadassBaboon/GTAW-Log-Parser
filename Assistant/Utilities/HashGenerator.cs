using System;
using System.Text;
using System.Windows;
using Assistant.Localization;
using System.Security.Cryptography;

namespace Assistant.Utilities
{
    public static class HashGenerator
    {
        /// <summary>
        /// Hashes the given chat log and bumps the same-hash counter in
        /// settings; warns when the same log has been parsed
        /// SameHashWarnAmount or more times in a row.
        /// </summary>
        public static void SaveParsedHash(string log)
        {
            string hash = ComputeMd5Hex(log);
            string lastAutoHash = Properties.Settings.Default.LastParsedAutoHash;

            Properties.Settings.Default.SameHashAutoCount = lastAutoHash == hash
                ? Properties.Settings.Default.SameHashAutoCount + 1
                : 1;
            Properties.Settings.Default.LastParsedAutoHash = hash;
            Properties.Settings.Default.Save();

            if (Properties.Settings.Default.SameHashAutoCount >= Properties.Settings.Default.SameHashWarnAmount)
                MessageBox.Show(
                    string.Format(Strings.SameHashWarning, Properties.Settings.Default.SameHashWarnAmount),
                    Strings.Warning, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private static string ComputeMd5Hex(string input)
        {
            byte[] data = MD5.HashData(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(data).ToLowerInvariant();
        }
    }
}
