using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using GTAWParser.Shared.Screenshot;

namespace GTAWParser.Shared
{
    public sealed class ParsedHtmlChatLine
    {
        public string RawText { get; set; } = string.Empty;
        public List<ChatStyledSegment> Segments { get; set; } = new();
        public bool HasExplicitColors { get; set; }
        public bool IsFromStructuredChatLine { get; set; }
    }

    /// <summary>
    /// Parses HTML-formatted chat logs (from ChatLogHtmlExporter, web browsers, or forum posts),
    /// stripping HTML boilerplate, style sheets, and tags, while extracting clean text and
    /// preserving roleplay color styles.
    /// </summary>
    public static class ChatLogHtmlParser
    {
        private static readonly Regex DoctypeRegex = new Regex(@"<!DOCTYPE[^>]*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex HeadRegex = new Regex(@"<head\b[^>]*>[\s\S]*?</head>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex StyleRegex = new Regex(@"<style\b[^>]*>[\s\S]*?</style>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex ScriptRegex = new Regex(@"<script\b[^>]*>[\s\S]*?</script>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex CommentRegex = new Regex(@"<!--[\s\S]*?-->", RegexOptions.Compiled);

        private static readonly Regex ChatLineDivRegex = new Regex(
            @"<div\b[^>]*class=[""'][^""']*\bchat-line\b[^""']*[""'][^>]*>([\s\S]*?)</div>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase
        );

        private static readonly Regex TimestampSpanRegex = new Regex(
            @"<span\b[^>]*class=[""'][^""']*\btimestamp\b[^""']*[""'][^>]*>[\s\S]*?</span>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase
        );

        private static readonly Regex TokenRegex = new Regex(
            @"(<(?<close>/)?(?<tag>[a-zA-Z][a-zA-Z0-9]*)(?<attrs>[^>]*)>)|(?<text>[^<]+)|(?<misc><)",
            RegexOptions.Compiled
        );

        private static readonly Regex StyleColorRegex = new Regex(
            @"color\s*:\s*([^;""']+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase
        );

        private static readonly Regex ColorAttrRegex = new Regex(
            @"\bcolor\s*=\s*[""']?([^""'\s>]+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase
        );

        private static readonly Regex RgbColorRegex = new Regex(
            @"rgba?\(\s*(\d{1,3})\s*,\s*(\d{1,3})\s*,\s*(\d{1,3})",
            RegexOptions.Compiled | RegexOptions.IgnoreCase
        );

        private static readonly Regex TimestampRegex = new Regex(
            @"^\s*\[\d{1,2}:\d{2}(?::\d{2})?\]\s*|^\s*\d{1,2}:\d{2}(?::\d{2})?\s+",
            RegexOptions.Compiled
        );

        /// <summary>
        /// Detects whether text contains HTML markup or an HTML chatlog document.
        /// </summary>
        public static bool IsHtml(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return false;

            if (input.IndexOf("<!DOCTYPE", StringComparison.OrdinalIgnoreCase) >= 0 ||
                input.IndexOf("<html", StringComparison.OrdinalIgnoreCase) >= 0 ||
                input.IndexOf("<head", StringComparison.OrdinalIgnoreCase) >= 0 ||
                input.IndexOf("<body", StringComparison.OrdinalIgnoreCase) >= 0 ||
                input.IndexOf("class=\"chat-line\"", StringComparison.OrdinalIgnoreCase) >= 0 ||
                input.IndexOf("class='chat-line'", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return Regex.IsMatch(input, @"<(div|p|span|table|tr|td|style|script)\b[^>]*>[\s\S]*?<\/\1>", RegexOptions.IgnoreCase)
                || Regex.IsMatch(input, @"<br\s*\/?>", RegexOptions.IgnoreCase);
        }

        /// <summary>
        /// Parses an HTML document or snippet into clean chat lines with styled color segments.
        /// </summary>
        public static List<ParsedHtmlChatLine> Parse(string? html)
        {
            var result = new List<ParsedHtmlChatLine>();
            if (string.IsNullOrWhiteSpace(html)) return result;

            // 1. Strip document wrapper blocks (head, style, script, comments, doctype)
            string cleaned = DoctypeRegex.Replace(html, string.Empty);
            cleaned = CommentRegex.Replace(cleaned, string.Empty);
            cleaned = HeadRegex.Replace(cleaned, string.Empty);
            cleaned = StyleRegex.Replace(cleaned, string.Empty);
            cleaned = ScriptRegex.Replace(cleaned, string.Empty);

            // 2. Extract line blocks
            var chatLineMatches = ChatLineDivRegex.Matches(cleaned);
            if (chatLineMatches.Count > 0)
            {
                foreach (Match m in chatLineMatches)
                {
                    string inner = m.Groups[1].Value;
                    if (!string.IsNullOrWhiteSpace(inner))
                    {
                        var parsed = ParseLineBlock(inner);
                        if (parsed != null && !string.IsNullOrWhiteSpace(parsed.RawText))
                        {
                            parsed.IsFromStructuredChatLine = true;
                            result.Add(parsed);
                        }
                    }
                }
            }
            else
            {
                // Fallback for arbitrary HTML: break on block tags and <br>
                string normalized = Regex.Replace(cleaned, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
                normalized = Regex.Replace(normalized, @"</(div|p|li|tr|h[1-6])>", "\n", RegexOptions.IgnoreCase);
                normalized = Regex.Replace(normalized, @"<(div|p|li|tr|h[1-6])\b[^>]*>", "\n", RegexOptions.IgnoreCase);

                var splits = normalized.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var s in splits)
                {
                    if (!string.IsNullOrWhiteSpace(s))
                    {
                        var parsed = ParseLineBlock(s);
                        if (parsed != null && !string.IsNullOrWhiteSpace(parsed.RawText))
                        {
                            result.Add(parsed);
                        }
                    }
                }
            }

            return result;
        }

        private static ParsedHtmlChatLine? ParseLineBlock(string blockHtml)
        {
            // Collapse whitespace between tags (HTML formatting indentation/newlines)
            string lineHtml = Regex.Replace(blockHtml, @">\s+<", "><").Trim();

            // Remove timestamp span if present (<span class="timestamp">...</span>)
            lineHtml = TimestampSpanRegex.Replace(lineHtml, string.Empty).Trim();
            lineHtml = Regex.Replace(lineHtml, @">\s+<", "><").Trim();

            var matches = TokenRegex.Matches(lineHtml);
            if (matches.Count == 0) return null;

            var segments = new List<ChatStyledSegment>();
            var colorStack = new Stack<string?>();
            var boldStack = new Stack<bool>();
            var italicStack = new Stack<bool>();
            bool hasExplicitColor = false;

            foreach (Match m in matches)
            {
                if (m.Groups["tag"].Success)
                {
                    string tag = m.Groups["tag"].Value.ToLowerInvariant();
                    bool isClosing = m.Groups["close"].Success;
                    string attrs = m.Groups["attrs"].Value;

                    if (isClosing)
                    {
                        if (tag == "span" || tag == "font")
                        {
                            if (colorStack.Count > 0) colorStack.Pop();
                            if (boldStack.Count > 0) boldStack.Pop();
                            if (italicStack.Count > 0) italicStack.Pop();
                        }
                        else if (tag == "b" || tag == "strong")
                        {
                            if (boldStack.Count > 0) boldStack.Pop();
                        }
                        else if (tag == "i" || tag == "em")
                        {
                            if (italicStack.Count > 0) italicStack.Pop();
                        }
                    }
                    else
                    {
                        if (tag == "span" || tag == "font")
                        {
                            string? color = null;
                            var styleMatch = StyleColorRegex.Match(attrs);
                            if (styleMatch.Success)
                            {
                                color = ParseCssColor(styleMatch.Groups[1].Value);
                            }
                            else
                            {
                                var colorAttrMatch = ColorAttrRegex.Match(attrs);
                                if (colorAttrMatch.Success)
                                {
                                    color = ParseCssColor(colorAttrMatch.Groups[1].Value);
                                }
                            }

                            if (!string.IsNullOrEmpty(color))
                            {
                                hasExplicitColor = true;
                            }

                            colorStack.Push(color);
                            boldStack.Push(attrs.IndexOf("font-weight", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                           attrs.IndexOf("bold", StringComparison.OrdinalIgnoreCase) >= 0);
                            italicStack.Push(attrs.IndexOf("font-style", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                            attrs.IndexOf("italic", StringComparison.OrdinalIgnoreCase) >= 0);
                        }
                        else if (tag == "b" || tag == "strong")
                        {
                            boldStack.Push(true);
                        }
                        else if (tag == "i" || tag == "em")
                        {
                            italicStack.Push(true);
                        }
                    }
                }
                else if (m.Groups["text"].Success || m.Groups["misc"].Success)
                {
                    string rawText = m.Groups["text"].Success ? m.Groups["text"].Value : m.Groups["misc"].Value;
                    string text = WebUtility.HtmlDecode(rawText).Replace('\u00A0', ' ');
                    if (string.IsNullOrEmpty(text)) continue;

                    // Skip leading whitespace before any content
                    if (segments.Count == 0 && string.IsNullOrWhiteSpace(text)) continue;

                    string activeColor = colorStack.FirstOrDefault(c => !string.IsNullOrEmpty(c)) ?? "#FFFFFF";
                    bool isBold = boldStack.Any(b => b);
                    bool isItalic = italicStack.Any(i => i);

                    if (segments.Count > 0 &&
                        segments[^1].ColorHex == activeColor &&
                        segments[^1].IsBold == isBold &&
                        segments[^1].IsItalic == isItalic)
                    {
                        segments[^1].Text += text;
                    }
                    else
                    {
                        segments.Add(new ChatStyledSegment(text, activeColor, isBold, isItalic));
                    }
                }
            }

            if (segments.Count == 0) return null;

            // Strip leading timestamps if any plain text timestamp prefix remains
            string fullRawText = string.Concat(segments.Select(s => s.Text));
            var tsMatch = TimestampRegex.Match(fullRawText);
            if (tsMatch.Success && tsMatch.Index == 0)
            {
                int toRemove = tsMatch.Length;
                while (toRemove > 0 && segments.Count > 0)
                {
                    if (segments[0].Text.Length <= toRemove)
                    {
                        toRemove -= segments[0].Text.Length;
                        segments.RemoveAt(0);
                    }
                    else
                    {
                        segments[0].Text = segments[0].Text.Substring(toRemove);
                        toRemove = 0;
                    }
                }
            }

            // Trim leading/trailing whitespace across segments
            TrimSegments(segments);

            if (segments.Count == 0) return null;

            string finalRawText = string.Concat(segments.Select(s => s.Text));
            if (string.IsNullOrWhiteSpace(finalRawText)) return null;

            // If metadata message, ignore
            if (finalRawText.Equals("No chat log entries available.", StringComparison.OrdinalIgnoreCase))
                return null;

            return new ParsedHtmlChatLine
            {
                RawText = finalRawText,
                Segments = segments,
                HasExplicitColors = hasExplicitColor
            };
        }

        private static void TrimSegments(List<ChatStyledSegment> segments)
        {
            // Trim start
            while (segments.Count > 0)
            {
                string trimmed = segments[0].Text.TrimStart();
                if (string.IsNullOrEmpty(trimmed))
                {
                    segments.RemoveAt(0);
                }
                else
                {
                    segments[0].Text = trimmed;
                    break;
                }
            }

            // Trim end
            while (segments.Count > 0)
            {
                string trimmed = segments[^1].Text.TrimEnd();
                if (string.IsNullOrEmpty(trimmed))
                {
                    segments.RemoveAt(segments.Count - 1);
                }
                else
                {
                    segments[^1].Text = trimmed;
                    break;
                }
            }
        }

        /// <summary>
        /// Parses CSS hex, rgb, or named color into a canonical #RRGGBB hex string.
        /// </summary>
        public static string? ParseCssColor(string? cssColor)
        {
            if (string.IsNullOrWhiteSpace(cssColor)) return null;

            string trimmed = cssColor.Trim().Trim('\'', '"', ';');

            // 1. Hex: #RGB, #RRGGBB, #RRGGBBAA
            if (trimmed.StartsWith("#", StringComparison.Ordinal))
            {
                string hex = trimmed.Substring(1);
                if (hex.Length == 3)
                {
                    return $"#{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}".ToUpperInvariant();
                }
                if (hex.Length >= 6)
                {
                    return $"#{hex.Substring(0, 6)}".ToUpperInvariant();
                }
                return null;
            }

            // 2. RGB/RGBA format: rgb(r, g, b) or rgba(r, g, b, a)
            var match = RgbColorRegex.Match(trimmed);
            if (match.Success)
            {
                if (int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int r) &&
                    int.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int g) &&
                    int.TryParse(match.Groups[3].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int b))
                {
                    r = Math.Clamp(r, 0, 255);
                    g = Math.Clamp(g, 0, 255);
                    b = Math.Clamp(b, 0, 255);
                    return $"#{r:X2}{g:X2}{b:X2}";
                }
            }

            // 3. Named colors commonly found in chat logs
            return trimmed.ToLowerInvariant() switch
            {
                "white" => "#FFFFFF",
                "yellow" => "#FFFF00",
                "red" => "#FF0000",
                "green" => "#32CD32",
                "blue" => "#1E90FF",
                "purple" => "#C2A3DA",
                "grey" or "gray" => "#A6ACAF",
                "black" => "#000000",
                _ => null
            };
        }
    }
}
