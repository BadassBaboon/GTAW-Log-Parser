using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace GTAWParser.Shared.Screenshot
{
    public class ChatStyledSegment
    {
        public string Text { get; set; } = string.Empty;
        public string ColorHex { get; set; } = "#FFFFFF";
        public bool IsBold { get; set; }
        public bool IsItalic { get; set; }
        public bool IsCensored { get; set; }

        public ChatStyledSegment() { }

        public ChatStyledSegment(string text, string colorHex, bool isBold = false, bool isItalic = false, bool isCensored = false)
        {
            Text = text;
            ColorHex = colorHex;
            IsBold = isBold;
            IsItalic = isItalic;
            IsCensored = isCensored;
        }
    }

    /// <summary>
    /// Screenshot-editor adapter over <see cref="ChatLineClassifier"/>.
    ///
    /// This type deliberately owns no palette and no classification rules of its own. Colours come
    /// from the same code path Live Tail and the HTML exporter use, so a screenshot renders the
    /// message in the colour the player actually saw in game. Where real NUI spans are available
    /// (a captured live session) they bypass classification entirely — see <see cref="FromSpans"/>.
    /// </summary>
    public static class RoleplayChatColorizer
    {
        public static string ColorWhite => ChatLineClassifier.GetHexColor(ChatLineCategory.ICSpeech);
        public static string ColorMePurple => ChatLineClassifier.GetHexColor(ChatLineCategory.Emote);
        public static string ColorOocGrey => ChatLineClassifier.GetHexColor(ChatLineCategory.OOC);
        public static string ColorRadioBlue => ChatLineClassifier.GetHexColor(ChatLineCategory.Radio);
        public static string ColorPhoneYellow => ChatLineClassifier.GetHexColor(ChatLineCategory.Phone);
        public static string ColorAdsGreen => ChatLineClassifier.GetHexColor(ChatLineCategory.Ads);
        public static string ColorSuccessGreen => ChatLineClassifier.GetHexColor(ChatLineCategory.Success);
        public static string ColorWarningYellow => ChatLineClassifier.GetHexColor(ChatLineCategory.Warning);
        public static string ColorErrorRed => ChatLineClassifier.GetHexColor(ChatLineCategory.Error);
        public static string ColorSystemBlue => ChatLineClassifier.GetHexColor(ChatLineCategory.SystemInfo);

        private static readonly Regex TimestampRegex = new Regex(
            @"^\s*\[\d{1,2}:\d{2}(?::\d{2})?\]\s*|^\s*\d{1,2}:\d{2}(?::\d{2})?\s+",
            RegexOptions.Compiled
        );

        /// <summary>
        /// Strips a leading timestamp prefix from a chat line.
        /// </summary>
        public static string StripTimestamp(string? rawLine)
        {
            if (string.IsNullOrWhiteSpace(rawLine))
                return string.Empty;

            string line = rawLine.Trim();
            line = TimestampRegex.Replace(line, string.Empty).Trim();
            return line;
        }

        /// <summary>
        /// The dominant colour of a line, i.e. the colour a single-colour renderer would pick.
        /// </summary>
        public static string DetectDefaultColor(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return ColorWhite;

            return ChatLineClassifier.GetHexColor(Classify(text));
        }

        /// <summary>
        /// The roleplay category of a line, after timestamp stripping.
        /// </summary>
        public static ChatLineCategory Classify(string? text)
        {
            string cleaned = StripTimestamp(text);
            if (string.IsNullOrWhiteSpace(cleaned))
                return ChatLineCategory.Default;

            return ChatLineClassifier.Classify(cleaned);
        }

        /// <summary>
        /// Parses a raw or formatted chat line into styled segments, stripping any timestamp prefix.
        /// </summary>
        public static List<ChatStyledSegment> ColorizeLine(string? rawLine)
        {
            string cleaned = StripTimestamp(rawLine);
            if (string.IsNullOrWhiteSpace(cleaned))
                return new List<ChatStyledSegment>();

            return FromSpans(ChatLineClassifier.ParseSpans(cleaned));
        }

        /// <summary>
        /// Converts spans captured straight from the FiveM NUI (or produced by the classifier) into
        /// renderer segments. This is the highest-fidelity path: the colours are the ones the game
        /// itself painted, so nothing is inferred.
        /// </summary>
        public static List<ChatStyledSegment> FromSpans(IEnumerable<CapturedChatSpan>? spans)
        {
            var segments = new List<ChatStyledSegment>();
            if (spans == null) return segments;

            foreach (var span in spans)
            {
                if (span == null || string.IsNullOrEmpty(span.Text)) continue;
                segments.Add(new ChatStyledSegment(
                    span.Text,
                    string.IsNullOrWhiteSpace(span.Color) ? ColorWhite : span.Color));
            }

            return segments;
        }

        /// <summary>
        /// Re-colours every segment of a line to a single colour, preserving the text runs.
        /// </summary>
        public static List<ChatStyledSegment> Recolor(IEnumerable<ChatStyledSegment> segments, string colorHex)
        {
            var result = new List<ChatStyledSegment>();
            foreach (var seg in segments)
            {
                result.Add(new ChatStyledSegment(seg.Text, colorHex, seg.IsBold, seg.IsItalic, false));
            }
            return result;
        }

        /// <summary>
        /// Marks every segment of a line as censored with a solid black spoiler bar.
        /// </summary>
        public static List<ChatStyledSegment> Censor(IEnumerable<ChatStyledSegment> segments)
        {
            var result = new List<ChatStyledSegment>();
            foreach (var seg in segments)
            {
                result.Add(new ChatStyledSegment(seg.Text, seg.ColorHex, seg.IsBold, seg.IsItalic, true));
            }
            return result;
        }

        /// <summary>
        /// Unified list of verified GTA World roleplay colors for the Screenshot Editor color picker.
        /// </summary>
        public static readonly IReadOnlyList<RoleplayPaletteSwatch> EditorSwatches = new List<RoleplayPaletteSwatch>
        {
            new RoleplayPaletteSwatch("/me & /do", "#C2A3DA", "/me emote and /do action (#C2A3DA)"),
            new RoleplayPaletteSwatch("Your Speech", "#FFFFFF", "Your character talking (#FFFFFF)"),
            new RoleplayPaletteSwatch("Other Speech", "#C8C8C8", "Other characters talking (#C8C8C8)"),
            new RoleplayPaletteSwatch("Whisper", "#EDA841", "Whisper / [low] speech (#EDA841)"),
            new RoleplayPaletteSwatch("Phone / SMS / PM", "#FFFF00", "Phone calls, SMS, PMs, inventory (#FFFF00)"),
            new RoleplayPaletteSwatch("Radio", "#1E90FF", "Radio channels (#1E90FF)"),
            new RoleplayPaletteSwatch("Item / Money / Success", "#32CD32", "Item given, payment, success (#32CD32)"),
            new RoleplayPaletteSwatch("CK Red / Admin / Error", "#FF0000", "CK red, admin actions, errors (#FF0000)"),
            new RoleplayPaletteSwatch("CK Blue / System", "#3896F3", "CK blue, system notices (#3896F3)"),
            new RoleplayPaletteSwatch("OOC (( ))", "#A6ACAF", "Out of character (( )) (#A6ACAF)"),
            new RoleplayPaletteSwatch("Advertisement", "#2ECC71", "Advertisement / news (#2ECC71)"),
        };
    }

    public record RoleplayPaletteSwatch(string Label, string Hex, string Tooltip);
}
