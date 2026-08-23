using System;
using System.Collections.Generic;
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
        Success,
        Warning,
        Error,
        SessionHeader,
        Default
    }

    public static class ChatLineClassifier
    {
        private static readonly Regex TimestampPrefixRegex = new Regex(@"^\[\d{1,2}:\d{1,2}:\d{1,2}\]\s*", RegexOptions.Compiled);

        public static readonly Regex DateHeaderRegex = new Regex(@"^\[DATE:\s*(\d{1,2}/[A-Za-z]{3}/\d{4})\s*\|\s*TIME:\s*(\d{1,2}:\d{2}:\d{2})\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex GlobalOocRegex = new Regex(@"^\(\(\s*Global OOC:\s*", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex PmRegex = new Regex(@"^\(\(\s*PM\s+(to|from)\s+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex OocRegex = new Regex(@"^\(\(\s*(\(\d+\)\s*)?", RegexOptions.Compiled);
        private static readonly Regex ActionRegex = new Regex(@"(^\*\s+.*\(\([\p{L}0-9_ ]+\)\)\*?$)|(^\>\s+)", RegexOptions.Compiled);
        private static readonly Regex EmoteRegex = new Regex(@"^\*\s+[\p{L}0-9_]+", RegexOptions.Compiled);
        private static readonly Regex RadioRegex = new Regex(@"(^\*\*\[S:\s*.*\s*CH:\s*.*\])|(^\[(Faction|Dep|Department|HQ|Dispatch)\])|(^\[XM Radio\])", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex AdsRegex = new Regex(@"^\[.*(Advertisement|News|Ad).*\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex PhoneRegex = new Regex(@"^\[PHONE\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex IcWhisperRegex = new Regex(@"^(\(Car\)\s+)?[\p{L}0-9_ ]+\s+(says|whispers)\s+\[low\]:", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex IcShoutRegex = new Regex(@"^(\(Car\)\s+)?[\p{L}0-9_ ]+\s+shouts:", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex IcSpeechRegex = new Regex(@"^(\(Car\)\s+)?[\p{L}0-9_ ]+\s+says(\s*\((phone|radio)\))?:", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex GreenSuccessRegex = new Regex(
            @"^(Your vehicle has been teleported|Vehicle parked\.|You've used\s+|You have successfully\s+|Successfully\s+|Refilling\s+[\d\.]+\s+gallons)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex RedErrorRegex = new Regex(@"^(Admin\s+.*\s+(banned|jailed|muted|kicked|warned|prisoned|punished)|Server:\s+.*\s+(banned|jailed|muted|kicked|warned)|\[ADMIN\]|Your vehicle insurance has expired|\[ERROR\]|You do not have\s+|You cannot\s+|You don't have\s+|.*was\s+(kicked|banned|ajailed)\s+for:|You were kicked)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex YellowWarningRegex = new Regex(@"^(Your vehicle insurance expires in|Use F3 to activate|\[WARNING\]|We've placed a blip|A blip has been|A waypoint has been|GPS set to|GPS location|A checkpoint has been|The blip for your vehicle)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex BlueInfoRegex = new Regex(@"^(Weather forecast:|\[SERVER\])", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex SystemInfoRegex = new Regex(@"^(\[INFO\]|\[ERROR\]|\[WARNING\]|\[XM Radio\]|\[SERVER\]|Weather forecast:|Phones:|Temperature:|Wind:|You have|Your vehicle|The number you are trying|Use F3|Use /|Welcome to)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Multi-color line pattern matchers
        private static readonly Regex WelcomeRegex = new Regex(@"^(Welcome to\s+)(GTA World)(\.?.*)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex InfoPrefixRegex = new Regex(@"^(\[INFO\])(.*)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex WeatherTempRegex = new Regex(@"^(Temperature:\s*)([\d\.\-]+°C)\s*\(([\d\.\-]+F)\)(,\s*it is currently\s*)([A-Za-z]+)(\.?)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex WeatherWindRegex = new Regex(@"^(Wind:\s*)([\d\.\-]+\s*km/h)\s*\(([\d\.\-]+\s*mph)\)(,\s*humidity:\s*)(\d+%)(,\s*rain precipitation:\s*)(\d+\s*mm)(\.?)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex StorePromptRegex = new Regex(@"^([^:]+:\s*)(Press\s+[A-Za-z0-9]+\s+to\s+[^.]+\.?)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex ItemBoughtRegex = new Regex(@"^(You bought a total of\s*)(\d+)(\s*item\(s\)\s*for\s*)(\$[\d,]+)(\.?)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        // "[San Chianski Gas Station]: Filled 3.17 gallons for $72!"
        private static readonly Regex GasFillReceiptRegex = new Regex(@"^(\[[^\]]+\]:\s*Filled\s+[\d\.]+\s+gallons\s+for\s+)(\$[\d,]+[!.]?)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        // "Refilling 3.17 gallons, please wait... ((9 seconds))"
        private static readonly Regex RefillProgressRegex = new Regex(@"^(Refilling\s+)([\d\.]+)(\s+gallons.*)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex GlobalOocSpanRegex = new Regex(@"^(\(\(\s*Global OOC:\s*(?:\(\d+\)\s*)?)([^:]+)(:.*)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex LocalOocSpanRegex = new Regex(@"^(\(\(\s*(?:\(\d+\)\s*)?)([^:]+)(:.*)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex EmbeddedCodesRegex = new Regex(@"(~[rgbypqocmuws]~)|(\{!?(?:#)?([0-9a-fA-F]{6})\})", RegexOptions.Compiled);

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

            if (DateHeaderRegex.IsMatch(trimmed)) return ChatLineCategory.SessionHeader;
            if (GlobalOocRegex.IsMatch(trimmed)) return ChatLineCategory.Default;
            if (PmRegex.IsMatch(trimmed)) return ChatLineCategory.PM;
            if (PhoneRegex.IsMatch(trimmed)) return ChatLineCategory.Phone;
            if (AdsRegex.IsMatch(trimmed)) return ChatLineCategory.Ads;
            if (RadioRegex.IsMatch(trimmed)) return ChatLineCategory.Radio;
            if (ActionRegex.IsMatch(trimmed)) return ChatLineCategory.Action;
            if (EmoteRegex.IsMatch(trimmed)) return ChatLineCategory.Emote;
            if (RedErrorRegex.IsMatch(trimmed)) return ChatLineCategory.Error;
            if (GreenSuccessRegex.IsMatch(trimmed)) return ChatLineCategory.Success;
            if (YellowWarningRegex.IsMatch(trimmed)) return ChatLineCategory.Warning;
            if (BlueInfoRegex.IsMatch(trimmed)) return ChatLineCategory.SystemInfo;
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
                ChatLineCategory.Emote => "#C2A2DA",         // GTAW Me / Emote Purple
                ChatLineCategory.Action => "#48C9B0",        // GTAW Do / Action Teal
                ChatLineCategory.ICSpeech => "#FFFFFF",      // Standard White
                ChatLineCategory.ICWhisper => "#FFFFFF",     // Standard White
                ChatLineCategory.ICShout => "#FFFFFF",       // Standard White
                ChatLineCategory.OOC => "#A6ACAF",           // OOC Gray
                ChatLineCategory.PM => "#FFFF00",            // PM Yellow / Gold
                ChatLineCategory.Radio => "#1E90FF",         // Radio Dodger Blue
                ChatLineCategory.Ads => "#2ECC71",           // Ads Green
                ChatLineCategory.Phone => "#FFFF00",         // Phone Yellow
                ChatLineCategory.Success => "#31CB31",       // GTAW Success Green
                ChatLineCategory.Error => "#FF0000",         // GTAW Error Red
                ChatLineCategory.Warning => "#FFFF00",       // GTAW Warning Yellow
                ChatLineCategory.SystemInfo => "#1E90FF",    // System Info Dodger Blue
                ChatLineCategory.SessionHeader => "#7F8C8D", // Timestamp / Header Gray
                _ => "#FFFFFF"                               // Default White
            };
        }

        /// <summary>
        /// Parses a chat line's content into rich colored segments for multi-colored GTA World messages.
        /// </summary>
        public static List<CapturedChatSpan> ParseSpans(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return new List<CapturedChatSpan> { new CapturedChatSpan(content, "#FFFFFF") };

            string trimmed = content.Trim();

            // 1. Session header
            if (DateHeaderRegex.IsMatch(trimmed))
            {
                return new List<CapturedChatSpan> { new CapturedChatSpan(content, "#7F8C8D") };
            }

            // 2. Embedded color codes (~g~ / {RRGGBB})
            if (EmbeddedCodesRegex.IsMatch(trimmed))
            {
                return ParseEmbeddedCodes(trimmed);
            }

            // 4. Global OOC
            Match mGlobal = GlobalOocSpanRegex.Match(trimmed);
            if (mGlobal.Success)
            {
                return new List<CapturedChatSpan>
                {
                    new CapturedChatSpan(mGlobal.Groups[1].Value, "#FFFFFF"),
                    new CapturedChatSpan(mGlobal.Groups[2].Value, "#FF0000"),
                    new CapturedChatSpan(mGlobal.Groups[3].Value, "#FFFFFF")
                };
            }

            // 5. Local OOC
            Match mLocal = LocalOocSpanRegex.Match(trimmed);
            if (mLocal.Success)
            {
                return new List<CapturedChatSpan>
                {
                    new CapturedChatSpan(mLocal.Groups[1].Value, "#A6ACAF"),
                    new CapturedChatSpan(mLocal.Groups[2].Value, "#31CB31"),
                    new CapturedChatSpan(mLocal.Groups[3].Value, "#A6ACAF")
                };
            }

            // 6. Welcome to GTA World
            Match mWelcome = WelcomeRegex.Match(trimmed);
            if (mWelcome.Success)
            {
                return new List<CapturedChatSpan>
                {
                    new CapturedChatSpan(mWelcome.Groups[1].Value, "#FFFFFF"),
                    new CapturedChatSpan(mWelcome.Groups[2].Value, "#FFFF00"),
                    new CapturedChatSpan(mWelcome.Groups[3].Value, "#FFFFFF")
                };
            }

            // 4. [INFO] prefix
            Match mInfo = InfoPrefixRegex.Match(trimmed);
            if (mInfo.Success)
            {
                return new List<CapturedChatSpan>
                {
                    new CapturedChatSpan(mInfo.Groups[1].Value, "#1E90FF"),
                    new CapturedChatSpan(mInfo.Groups[2].Value, "#FFFFFF")
                };
            }

            // 5. Weather Temperature
            Match mTemp = WeatherTempRegex.Match(trimmed);
            if (mTemp.Success)
            {
                return new List<CapturedChatSpan>
                {
                    new CapturedChatSpan(mTemp.Groups[1].Value, "#FFFFFF"),
                    new CapturedChatSpan(mTemp.Groups[2].Value, "#31CB31"),
                    new CapturedChatSpan(" (", "#FFFFFF"),
                    new CapturedChatSpan(mTemp.Groups[3].Value, "#31CB31"),
                    new CapturedChatSpan(")" + mTemp.Groups[4].Value, "#FFFFFF"),
                    new CapturedChatSpan(mTemp.Groups[5].Value, "#31CB31"),
                    new CapturedChatSpan(mTemp.Groups[6].Value, "#FFFFFF")
                };
            }

            // 6. Weather Wind
            Match mWind = WeatherWindRegex.Match(trimmed);
            if (mWind.Success)
            {
                return new List<CapturedChatSpan>
                {
                    new CapturedChatSpan(mWind.Groups[1].Value, "#FFFFFF"),
                    new CapturedChatSpan(mWind.Groups[2].Value, "#31CB31"),
                    new CapturedChatSpan(" (", "#FFFFFF"),
                    new CapturedChatSpan(mWind.Groups[3].Value, "#31CB31"),
                    new CapturedChatSpan(")" + mWind.Groups[4].Value, "#FFFFFF"),
                    new CapturedChatSpan(mWind.Groups[5].Value, "#31CB31"),
                    new CapturedChatSpan(mWind.Groups[6].Value, "#FFFFFF"),
                    new CapturedChatSpan(mWind.Groups[7].Value, "#31CB31"),
                    new CapturedChatSpan(mWind.Groups[8].Value, "#FFFFFF")
                };
            }

            // 7. Store prompt: "Route 68 24/7: Press Y to open store."
            Match mStore = StorePromptRegex.Match(trimmed);
            if (mStore.Success && !mStore.Groups[1].Value.Contains("says", StringComparison.OrdinalIgnoreCase))
            {
                return new List<CapturedChatSpan>
                {
                    new CapturedChatSpan(mStore.Groups[1].Value, "#1E90FF"),
                    new CapturedChatSpan(mStore.Groups[2].Value, "#FFFF00")
                };
            }

            // 8. Item purchase notice: "You bought a total of 1 item(s) for $150."
            Match mBought = ItemBoughtRegex.Match(trimmed);
            if (mBought.Success)
            {
                return new List<CapturedChatSpan>
                {
                    new CapturedChatSpan(mBought.Groups[1].Value, "#FFFFFF"),
                    new CapturedChatSpan(mBought.Groups[2].Value, "#1E90FF"),
                    new CapturedChatSpan(mBought.Groups[3].Value, "#FFFFFF"),
                    new CapturedChatSpan(mBought.Groups[4].Value, "#31CB31"),
                    new CapturedChatSpan(mBought.Groups[5].Value, "#FFFFFF")
                };
            }

            // 9. Gas station fill receipt: "[San Chianski Gas Station]: Filled 3.17 gallons for $72!"
            Match mGas = GasFillReceiptRegex.Match(trimmed);
            if (mGas.Success)
            {
                return new List<CapturedChatSpan>
                {
                    new CapturedChatSpan(mGas.Groups[1].Value, "#FFFFFF"),
                    new CapturedChatSpan(mGas.Groups[2].Value, "#31CB31")
                };
            }

            // 10. Refilling gallons progress: "Refilling 3.17 gallons, please wait... ((9 seconds))"
            Match mRefill = RefillProgressRegex.Match(trimmed);
            if (mRefill.Success)
            {
                return new List<CapturedChatSpan>
                {
                    new CapturedChatSpan(mRefill.Groups[1].Value, "#31CB31"),
                    new CapturedChatSpan(mRefill.Groups[2].Value, "#FFFFFF"),
                    new CapturedChatSpan(mRefill.Groups[3].Value, "#31CB31")
                };
            }

            // 11. Global OOC: white body, red admin name
            Match mGlobalOoc = GlobalOocSpanRegex.Match(trimmed);
            if (mGlobalOoc.Success)
            {
                return new List<CapturedChatSpan>
                {
                    new CapturedChatSpan(mGlobalOoc.Groups[1].Value, "#FFFFFF"),
                    new CapturedChatSpan(mGlobalOoc.Groups[2].Value, "#FF0000"), // Admin name in red
                    new CapturedChatSpan(mGlobalOoc.Groups[3].Value, "#FFFFFF")
                };
            }

            // 12. Single-color roleplay and system categories
            ChatLineCategory category = Classify(trimmed);
            string hexColor = GetHexColor(category);
            return new List<CapturedChatSpan> { new CapturedChatSpan(content, hexColor) };
        }

        private static List<CapturedChatSpan> ParseEmbeddedCodes(string rawText)
        {
            var result = new List<CapturedChatSpan>();
            int lastIndex = 0;
            string currentColor = "#FFFFFF";

            var matches = EmbeddedCodesRegex.Matches(rawText);
            foreach (Match match in matches)
            {
                if (match.Index > lastIndex)
                {
                    string textPart = rawText.Substring(lastIndex, match.Index - lastIndex);
                    if (!string.IsNullOrEmpty(textPart))
                    {
                        result.Add(new CapturedChatSpan(textPart, currentColor));
                    }
                }

                if (!string.IsNullOrEmpty(match.Groups[1].Value))
                {
                    currentColor = match.Groups[1].Value.ToLowerInvariant() switch
                    {
                        "~r~" => "#FF0000",
                        "~g~" => "#31CB31",
                        "~b~" => "#1E90FF",
                        "~y~" => "#FFFF00",
                        "~p~" => "#C2A2DA",
                        "~q~" => "#FF69B4",
                        "~o~" => "#FFA500",
                        "~c~" => "#A6ACAF",
                        "~m~" => "#666666",
                        "~u~" => "#000000",
                        _ => "#FFFFFF"
                    };
                }
                else if (!string.IsNullOrEmpty(match.Groups[3].Value))
                {
                    currentColor = "#" + match.Groups[3].Value.ToUpperInvariant();
                }

                lastIndex = match.Index + match.Length;
            }

            if (lastIndex < rawText.Length)
            {
                string remaining = rawText.Substring(lastIndex);
                if (!string.IsNullOrEmpty(remaining))
                {
                    result.Add(new CapturedChatSpan(remaining, currentColor));
                }
            }

            return result.Count > 0 ? result : new List<CapturedChatSpan> { new CapturedChatSpan(rawText, "#FFFFFF") };
        }
    }
}
