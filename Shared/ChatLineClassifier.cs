using System;
using System.Text.RegularExpressions;

namespace GTAWParser.Shared
{
    public enum ChatLineCategory
    {
        Emote,
        Action,
        ICSpeech,
        ICWhisper,
        ICShout,
        OOC,
        PM,
        Radio,
        Ads,
        Phone,
        SystemInfo,
        Default
    }

    public static class ChatLineClassifier
    {
        private static readonly Regex TimestampPrefixRegex = new Regex(@"^\[\d{1,2}:\d{1,2}:\d{1,2}\]\s*", RegexOptions.Compiled);

        private static readonly Regex PmRegex = new Regex(@"^\(\(\s*PM\s+(to|from)\s+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex OocRegex = new Regex(@"^\(\(\s*(\(\d+\)\s*)?", RegexOptions.Compiled);
        private static readonly Regex ActionRegex = new Regex(@"(^\*\s+.*\(\([\p{L}0-9_ ]+\)\)\*?$)|(^\>\s+)", RegexOptions.Compiled);
        private static readonly Regex EmoteRegex = new Regex(@"^\*\s+[\p{L}0-9_]+", RegexOptions.Compiled);
        private static readonly Regex RadioRegex = new Regex(@"(^\*\*\[S:\s*.*\s*CH:\s*.*\])|(^\[(Faction|Dep|Department|HQ|Dispatch)\])", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex AdsRegex = new Regex(@"^\[.*(Advertisement|News|Ad).*\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex PhoneRegex = new Regex(@"^\[PHONE\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex IcWhisperRegex = new Regex(@"^(\(Car\)\s+)?[\p{L}0-9_ ]+\s+(says|whispers)\s+\[low\]:", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex IcShoutRegex = new Regex(@"^(\(Car\)\s+)?[\p{L}0-9_ ]+\s+shouts:", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex IcSpeechRegex = new Regex(@"^(\(Car\)\s+)?[\p{L}0-9_ ]+\s+says(\s*\((phone|radio)\))?:", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex SystemInfoRegex = new Regex(@"^(\[INFO\]|\[ERROR\]|\[WARNING\]|\[DATE:|\[XM Radio\]|\[SERVER\]|Weather forecast:|Phones:|Temperature:|Wind:|You have|Your vehicle|The number you are trying|Use F3|Use /|Welcome to)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static (string Timestamp, string Content) SplitTimestamp(string line)
        {
            if (string.IsNullOrEmpty(line)) return (string.Empty, string.Empty);
            Match match = TimestampPrefixRegex.Match(line);
            if (match.Success)
            {
                return (match.Value, line.Substring(match.Length));
            }
            return (string.Empty, line);
        }

        public static ChatLineCategory Classify(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return ChatLineCategory.Default;

            string trimmed = content.Trim();

            if (PmRegex.IsMatch(trimmed)) return ChatLineCategory.PM;
            if (PhoneRegex.IsMatch(trimmed)) return ChatLineCategory.Phone;
            if (AdsRegex.IsMatch(trimmed)) return ChatLineCategory.Ads;
            if (RadioRegex.IsMatch(trimmed)) return ChatLineCategory.Radio;
            if (ActionRegex.IsMatch(trimmed)) return ChatLineCategory.Action;
            if (EmoteRegex.IsMatch(trimmed)) return ChatLineCategory.Emote;
            if (IcWhisperRegex.IsMatch(trimmed)) return ChatLineCategory.ICWhisper;
            if (IcShoutRegex.IsMatch(trimmed)) return ChatLineCategory.ICShout;
            if (IcSpeechRegex.IsMatch(trimmed)) return ChatLineCategory.ICSpeech;
            if (OocRegex.IsMatch(trimmed)) return ChatLineCategory.OOC;
            if (SystemInfoRegex.IsMatch(trimmed)) return ChatLineCategory.SystemInfo;

            return ChatLineCategory.Default;
        }

        public static string GetHexColor(ChatLineCategory category)
        {
            return category switch
            {
                ChatLineCategory.Emote => "#C2A2DA",      // GTAW Me / Emote Purple
                ChatLineCategory.Action => "#48C9B0",     // GTAW Do / Action Teal
                ChatLineCategory.ICSpeech => "#F0F0F0",   // Standard White / Light Gray
                ChatLineCategory.ICWhisper => "#95A5A6",  // Slate Gray
                ChatLineCategory.ICShout => "#F39C12",    // Amber / Orange
                ChatLineCategory.OOC => "#A6ACAF",        // OOC Gray
                ChatLineCategory.PM => "#F1C40F",         // PM Yellow / Gold
                ChatLineCategory.Radio => "#3498DB",      // Radio Sky Blue
                ChatLineCategory.Ads => "#2ECC71",        // Ads Green
                ChatLineCategory.Phone => "#E67E22",      // Phone Warm Orange
                ChatLineCategory.SystemInfo => "#E74C3C", // Coral / System info
                _ => "#DCDCDC"                            // Default Foreground
            };
        }
    }
}
