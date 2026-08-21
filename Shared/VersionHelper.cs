using System;

namespace GTAWParser.Shared
{
    public static class VersionHelper
    {
        public static bool IsVersionNewer(string? available, string? installed)
        {
            if (string.IsNullOrWhiteSpace(available) || string.IsNullOrWhiteSpace(installed))
                return false;

            if (!TryParseVersion(available, out Version? availableVer) || !TryParseVersion(installed, out Version? installedVer) || availableVer == null || installedVer == null)
            {
                return string.CompareOrdinal(installed, available) < 0;
            }

            return availableVer.CompareTo(installedVer) > 0;
        }

        public static bool TryParseVersion(string? value, out Version? version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string normalized = value.Trim().TrimStart('v', 'V');
            int prerelease = normalized.IndexOf('-');
            if (prerelease >= 0)
                normalized = normalized.Substring(0, prerelease);

            string[] parts = normalized.Split('.');
            int[] numbers = new int[] { 0, 0, 0, 0 };
            if (parts.Length == 0 || parts.Length > 4)
                return false;

            for (int i = 0; i < parts.Length; i++)
            {
                if (!int.TryParse(parts[i], out numbers[i]))
                    return false;
            }

            version = new Version(numbers[0], numbers[1], numbers[2], numbers[3]);
            return true;
        }
    }
}
