using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace GTAWParser.Shared
{
    /// <summary>
    /// Generates standalone HTML documents from captured or parsed GTAW chat logs.
    /// Spans carry only foreground color attributes to allow clean copy-pasting
    /// into Invision Community (IPS) WYSIWYG forum editors without transferring background boxes.
    /// </summary>
    public static class ChatLogHtmlExporter
    {
        public static string GenerateHtml(IEnumerable<CapturedChatLine> lines, bool removeTimestamps = false, string title = "GTAW Chat Log")
        {
            if (lines == null)
                return GenerateEmptyHtml(title);

            var sb = new StringBuilder(4096);
            AppendHtmlHeader(sb, title);

            foreach (CapturedChatLine line in lines)
            {
                if (line == null || string.IsNullOrWhiteSpace(line.Text))
                    continue;

                sb.Append("      <div class=\"chat-line\">");

                var (timestamp, content) = ChatLineClassifier.SplitTimestamp(line.Text);

                if (!removeTimestamps && !string.IsNullOrEmpty(timestamp))
                {
                    sb.Append("<span class=\"timestamp\">");
                    sb.Append(WebUtility.HtmlEncode(timestamp.Trim()));
                    sb.Append("</span> ");
                }

                if (line.Spans != null && line.Spans.Count > 0)
                {
                    bool firstSpan = true;
                    foreach (CapturedChatSpan span in line.Spans)
                    {
                        if (string.IsNullOrEmpty(span.Text))
                            continue;

                        string spanText = span.Text;
                        if (firstSpan)
                        {
                            firstSpan = false;
                            if (!string.IsNullOrEmpty(timestamp) && spanText.StartsWith(timestamp, StringComparison.Ordinal))
                            {
                                spanText = spanText.Substring(timestamp.Length).TrimStart(' ');
                            }
                            else if (spanText.StartsWith("[") && spanText.IndexOf(']') > 0)
                            {
                                int endBracket = spanText.IndexOf(']');
                                string potentialTs = spanText.Substring(0, endBracket + 1);
                                if (ChatLineClassifier.SplitTimestamp(potentialTs + " ").Timestamp.Length > 0)
                                {
                                    spanText = spanText.Substring(endBracket + 1).TrimStart(' ');
                                }
                            }
                        }

                        if (string.IsNullOrEmpty(spanText))
                            continue;

                        AppendColoredSpan(sb, spanText, span.Color);
                    }
                }
                else
                {
                    // Fallback to pattern classifier
                    List<CapturedChatSpan> fallbackSpans = ChatLineClassifier.ParseSpans(content);
                    if (fallbackSpans != null && fallbackSpans.Count > 0)
                    {
                        foreach (CapturedChatSpan span in fallbackSpans)
                        {
                            if (!string.IsNullOrEmpty(span.Text))
                            {
                                AppendColoredSpan(sb, span.Text, span.Color);
                            }
                        }
                    }
                    else
                    {
                        AppendColoredSpan(sb, content, line.DominantColor);
                    }
                }

                sb.AppendLine("</div>");
            }

            AppendHtmlFooter(sb);
            return sb.ToString();
        }

        public static string GenerateHtmlFromText(string rawText, bool removeTimestamps = false, string title = "GTAW Chat Log")
        {
            if (string.IsNullOrWhiteSpace(rawText))
                return GenerateEmptyHtml(title);

            string[] lines = rawText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            var captured = new List<CapturedChatLine>(lines.Length);

            foreach (string l in lines)
            {
                string trimmed = l.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed))
                {
                    captured.Add(new CapturedChatLine(trimmed));
                }
            }

            return GenerateHtml(captured, removeTimestamps, title);
        }

        private static void AppendColoredSpan(StringBuilder sb, string text, string? hexColor)
        {
            string encoded = WebUtility.HtmlEncode(text);
            string color = string.IsNullOrWhiteSpace(hexColor) ? "#FFFFFF" : hexColor.Trim();

            if (string.Equals(color, "#FFFFFF", StringComparison.OrdinalIgnoreCase))
            {
                sb.Append("<span>");
                sb.Append(encoded);
                sb.Append("</span>");
            }
            else
            {
                sb.Append("<span style=\"color: ");
                sb.Append(color);
                sb.Append(";\">");
                sb.Append(encoded);
                sb.Append("</span>");
            }
        }

        private static void AppendHtmlHeader(StringBuilder sb, string title)
        {
            string encodedTitle = WebUtility.HtmlEncode(title);
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html lang=\"en\">");
            sb.AppendLine("<head>");
            sb.AppendLine("  <meta charset=\"UTF-8\">");
            sb.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
            sb.Append("  <title>").Append(encodedTitle).AppendLine("</title>");
            sb.AppendLine("  <style>");
            sb.AppendLine("    :root {");
            sb.AppendLine("      color-scheme: dark;");
            sb.AppendLine("    }");
            sb.AppendLine("    body {");
            sb.AppendLine("      background-color: #1a1a1a;");
            sb.AppendLine("      color: #ffffff;");
            sb.AppendLine("      font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif;");
            sb.AppendLine("      font-size: 14px;");
            sb.AppendLine("      line-height: 1.5;");
            sb.AppendLine("      margin: 0;");
            sb.AppendLine("      padding: 24px;");
            sb.AppendLine("    }");
            sb.AppendLine("    .chat-container {");
            sb.AppendLine("      max-width: 1400px;");
            sb.AppendLine("      margin: 0 auto;");
            sb.AppendLine("      background: #141414;");
            sb.AppendLine("      border: 1px solid #2a2a2a;");
            sb.AppendLine("      border-radius: 8px;");
            sb.AppendLine("      padding: 16px 20px;");
            sb.AppendLine("      box-shadow: 0 4px 16px rgba(0,0,0,0.5);");
            sb.AppendLine("    }");
            sb.AppendLine("    .chat-line {");
            sb.AppendLine("      margin: 3px 0;");
            sb.AppendLine("      word-break: break-word;");
            sb.AppendLine("      white-space: pre-wrap;");
            sb.AppendLine("    }");
            sb.AppendLine("    .timestamp {");
            sb.AppendLine("      color: #7f8c8d;");
            sb.AppendLine("      user-select: text;");
            sb.AppendLine("      font-variant-numeric: tabular-nums;");
            sb.AppendLine("    }");
            sb.AppendLine("  </style>");
            sb.AppendLine("</head>");
            sb.AppendLine("<body>");
            sb.AppendLine("  <div class=\"chat-container\">");
        }

        private static void AppendHtmlFooter(StringBuilder sb)
        {
            sb.AppendLine("  </div>");
            sb.AppendLine("</body>");
            sb.AppendLine("</html>");
        }

        private static string GenerateEmptyHtml(string title)
        {
            var sb = new StringBuilder(512);
            AppendHtmlHeader(sb, title);
            sb.AppendLine("      <div class=\"chat-line\"><span style=\"color: #7f8c8d;\">No chat log entries available.</span></div>");
            AppendHtmlFooter(sb);
            return sb.ToString();
        }
    }
}
