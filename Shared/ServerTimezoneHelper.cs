using System;
using System.Collections.Generic;

namespace GTAWParser.Shared
{
    public static class ServerTimezoneHelper
    {
        public static string CurrentTimezoneSetting { get; set; } = "Auto";

        public static int? DetectedOffsetHours { get; set; }

        public static readonly (string Key, string DisplayName)[] SupportedTimezones = new[]
        {
            ("Auto", "Auto-Detect Server Timezone (Recommended)"),
            ("UTC", "GTA World English (UTC / Server Time)"),
            ("TR", "GTA World Türkiye (Istanbul / UTC+3)"),
            ("KR", "GTA World Korea (Seoul / UTC+9)"),
            ("RU", "GTA World Russia (St. Petersburg / UTC+3)"),
            ("FR", "GTA World France (Paris / CET)"),
            ("ES", "GTA World Spain (Madrid / CET)"),
            ("Local", "Local PC Time (System Clock)")
        };

        /// <summary>
        /// Calibrates the server timezone offset from an extracted clock string (e.g. "10:05", "13:05", or "[17:18:02]").
        /// </summary>
        public static bool UpdateAutoDetectedClock(string? clockTimeStr, DateTime? utcReference = null)
        {
            if (string.IsNullOrWhiteSpace(clockTimeStr))
                return false;

            string clean = clockTimeStr.Trim('[', ']', ' ');
            if (!TimeSpan.TryParse(clean, out TimeSpan serverTimeOfDay))
            {
                var (ts, _) = ChatLineClassifier.SplitTimestamp(clockTimeStr);
                if (string.IsNullOrEmpty(ts) || !TimeSpan.TryParse(ts.Trim('[', ']', ' '), out serverTimeOfDay))
                {
                    return false;
                }
            }

            DateTime utc = utcReference ?? DateTime.UtcNow;
            TimeSpan utcTimeOfDay = utc.TimeOfDay;

            double diffHours = (serverTimeOfDay - utcTimeOfDay).TotalHours;

            // Normalize day wrap around [-12, +12]
            while (diffHours > 12) diffHours -= 24;
            while (diffHours < -12) diffHours += 24;

            DetectedOffsetHours = (int)Math.Round(diffHours);
            return true;
        }

        public static DateTime GetServerTime(string? timezoneKey = null, DateTime? utcBase = null)
        {
            string key = timezoneKey ?? CurrentTimezoneSetting;
            DateTime utc = utcBase ?? DateTime.UtcNow;

            if (string.Equals(key, "Auto", StringComparison.OrdinalIgnoreCase))
            {
                if (DetectedOffsetHours.HasValue)
                {
                    return utc.AddHours(DetectedOffsetHours.Value);
                }
                return utc; // Default to UTC until clock is sampled
            }

            return key switch
            {
                "TR" => ConvertToTimezone(utc, "Turkey Standard Time", 3),
                "KR" => ConvertToTimezone(utc, "Korea Standard Time", 9),
                "RU" => ConvertToTimezone(utc, "Russian Standard Time", 3),
                "FR" => ConvertToTimezone(utc, "W. Europe Standard Time", 1, "Romance Standard Time", "Central Europe Standard Time"),
                "ES" => ConvertToTimezone(utc, "Romance Standard Time", 1, "W. Europe Standard Time", "Central Europe Standard Time"),
                "Local" => TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.Local),
                _ => utc // Default: UTC (GTA World English / Main)
            };
        }

        private static DateTime ConvertToTimezone(DateTime utc, string primaryTzId, int fallbackHourOffset, params string[] alternativeTzIds)
        {
            try
            {
                TimeZoneInfo tz = TimeZoneInfo.FindSystemTimeZoneById(primaryTzId);
                return TimeZoneInfo.ConvertTimeFromUtc(utc, tz);
            }
            catch
            {
                foreach (string alt in alternativeTzIds)
                {
                    try
                    {
                        TimeZoneInfo tz = TimeZoneInfo.FindSystemTimeZoneById(alt);
                        return TimeZoneInfo.ConvertTimeFromUtc(utc, tz);
                    }
                    catch { }
                }
                return utc.AddHours(fallbackHourOffset);
            }
        }
    }
}
