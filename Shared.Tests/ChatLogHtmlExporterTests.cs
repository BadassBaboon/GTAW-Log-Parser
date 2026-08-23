using System.Collections.Generic;
using GTAWParser.Shared;
using Xunit;

namespace GTAWParser.Shared.Tests
{
    public class ChatLogHtmlExporterTests
    {
        [Fact]
        public void GenerateHtml_WithCapturedLines_PreservesColors()
        {
            var line = new CapturedChatLine
            {
                Text = "[18:06:13] Benjamin Buschetta nods.",
                DominantColor = "#C2A2DA",
                Spans = new List<CapturedChatSpan>
                {
                    new CapturedChatSpan("Benjamin Buschetta nods.", "#C2A2DA")
                }
            };

            string html = ChatLogHtmlExporter.GenerateHtml(new[] { line });

            Assert.Contains("<span class=\"timestamp\">[18:06:13]</span>", html);
            Assert.Contains("<span style=\"color: #C2A2DA;\">Benjamin Buschetta nods.</span>", html);
            Assert.DoesNotContain("background-color:", html.Substring(html.IndexOf("<div class=\"chat-container\">")));
        }

        [Fact]
        public void GenerateHtmlFromText_UsesClassifierFallback()
        {
            string raw = "[18:36:46] Weather forecast:\n[18:36:46] Temperature: 33.3°C (91.92F), it is currently Sunny.";
            string html = ChatLogHtmlExporter.GenerateHtmlFromText(raw);

            Assert.Contains("style=\"color: #1E90FF;\"", html); // Weather forecast
            Assert.Contains("style=\"color: #31CB31;\"", html); // Temperature / Sunny
            Assert.Contains("33.3", html);
        }

        [Fact]
        public void GenerateHtml_RemoveTimestamps_OmitsTimestampSpans()
        {
            string raw = "[18:36:46] Welcome to GTA World.";
            string html = ChatLogHtmlExporter.GenerateHtmlFromText(raw, removeTimestamps: true);

            Assert.DoesNotContain("<span class=\"timestamp\">", html);
            Assert.Contains("Welcome to", html);
            Assert.Contains("GTA World", html);
        }

        [Fact]
        public void GenerateHtml_HtmlEncodesSpecialCharacters()
        {
            var line = new CapturedChatLine
            {
                Text = "(( John & Jane <script>alert('xss')</script> > Me ))",
                Spans = new List<CapturedChatSpan>
                {
                    new CapturedChatSpan("(( John & Jane <script>alert('xss')</script> > Me ))", "#A6ACAF")
                }
            };

            string html = ChatLogHtmlExporter.GenerateHtml(new[] { line });

            Assert.Contains("&lt;script&gt;alert(&#39;xss&#39;)&lt;/script&gt;", html);
            Assert.Contains("John &amp; Jane", html);
            Assert.DoesNotContain("<script>", html);
        }

        [Fact]
        public void GenerateHtml_EmptyInput_ReturnsEmptyState()
        {
            string html = ChatLogHtmlExporter.GenerateHtml(null!);
            Assert.Contains("No chat log entries available.", html);
        }
    }
}
