using System.Collections.Generic;
using GTAWParser.Shared;
using Xunit;

namespace GTAWParser.Shared.Tests
{
    public class ChatLogHtmlParserTests
    {
        [Fact]
        public void IsHtml_DetectsHtmlSnippetsAndDocuments()
        {
            Assert.True(ChatLogHtmlParser.IsHtml("<!DOCTYPE html><html><body>Test</body></html>"));
            Assert.True(ChatLogHtmlParser.IsHtml("<div class=\"chat-line\"><span>Test</span></div>"));
            Assert.True(ChatLogHtmlParser.IsHtml("<p>Hello<br>World</p>"));
            Assert.False(ChatLogHtmlParser.IsHtml("[12:34:56] Normal chat text without tags."));
            Assert.False(ChatLogHtmlParser.IsHtml("Player says: I love you <3"));
            Assert.False(ChatLogHtmlParser.IsHtml(null));
            Assert.False(ChatLogHtmlParser.IsHtml("   "));
        }

        [Fact]
        public void Parse_FiltersOutStyleAndScriptBlobs()
        {
            string html = @"<!DOCTYPE html>
<html>
<head>
  <style>
    body { background: #141414; color: #fff; }
    .chat-container { max-width: 1400px; }
  </style>
  <script>console.log('test');</script>
</head>
<body>
  <div class=""chat-container"">
    <div class=""chat-line""><span class=""timestamp"">[12:00:00]</span> <span style=""color: #C2A2DA;"">* Character smiles.</span></div>
  </div>
</body>
</html>";

            var lines = ChatLogHtmlParser.Parse(html);

            Assert.Single(lines);
            Assert.Equal("* Character smiles.", lines[0].RawText);
            Assert.True(lines[0].HasExplicitColors);
            Assert.Single(lines[0].Segments);
            Assert.Equal("#C2A2DA", lines[0].Segments[0].ColorHex);
            Assert.Equal("* Character smiles.", lines[0].Segments[0].Text);
        }

        [Fact]
        public void Parse_HandlesMultiColorSpansInSingleLine()
        {
            string html = @"<div class=""chat-line"">
  <span class=""timestamp"">[14:22:01]</span>
  <span style=""color: #1E90FF;"">[Radio] Officer says: </span>
  <span style=""color: #FFFFFF;"">10-4, heading to the scene.</span>
</div>";

            var lines = ChatLogHtmlParser.Parse(html);

            Assert.Single(lines);
            Assert.Equal("[Radio] Officer says: 10-4, heading to the scene.", lines[0].RawText);
            Assert.Equal(2, lines[0].Segments.Count);
            Assert.Equal("[Radio] Officer says: ", lines[0].Segments[0].Text);
            Assert.Equal("#1E90FF", lines[0].Segments[0].ColorHex);
            Assert.Equal("10-4, heading to the scene.", lines[0].Segments[1].Text);
            Assert.Equal("#FFFFFF", lines[0].Segments[1].ColorHex);
        }

        [Fact]
        public void Parse_ParsesRgbAndNamedColors()
        {
            string html = @"<p><span style=""color: rgb(194, 163, 218);"">* John waves.</span></p>
<p><span style=""color: yellow;"">[SMS] Hey there!</span></p>";

            var lines = ChatLogHtmlParser.Parse(html);

            Assert.Equal(2, lines.Count);
            Assert.Equal("* John waves.", lines[0].RawText);
            Assert.Equal("#C2A3DA", lines[0].Segments[0].ColorHex);

            Assert.Equal("[SMS] Hey there!", lines[1].RawText);
            Assert.Equal("#FFFF00", lines[1].Segments[0].ColorHex);
        }

        [Fact]
        public void Parse_DecodesHtmlEntitiesAndStripsResidualTags()
        {
            string html = @"<div class=""chat-line"">
  <span style=""color: #A6ACAF;"">(( John &amp; Jane &quot;test&quot; &#39;quotes&#39; &lt;3 ))</span>
</div>";

            var lines = ChatLogHtmlParser.Parse(html);

            Assert.Single(lines);
            Assert.Equal("(( John & Jane \"test\" 'quotes' <3 ))", lines[0].RawText);
            Assert.Equal("#A6ACAF", lines[0].Segments[0].ColorHex);
        }

        [Fact]
        public void Parse_EmptyAndWhitespace_ReturnsEmptyList()
        {
            Assert.Empty(ChatLogHtmlParser.Parse(null));
            Assert.Empty(ChatLogHtmlParser.Parse(""));
            Assert.Empty(ChatLogHtmlParser.Parse("   "));
            Assert.Empty(ChatLogHtmlParser.Parse("<style>body{color:red;}</style>"));
        }

        [Fact]
        public void Parse_RoundTripFromChatLogHtmlExporter()
        {
            var originalLines = new[]
            {
                new CapturedChatLine
                {
                    Text = "[12:00:00] * John Doe takes a deep breath.",
                    DominantColor = "#C2A2DA",
                    Spans = new List<CapturedChatSpan>
                    {
                        new CapturedChatSpan("* John Doe takes a deep breath.", "#C2A2DA")
                    }
                },
                new CapturedChatLine
                {
                    Text = "[12:00:05] John Doe says: Good evening.",
                    DominantColor = "#FFFFFF",
                    Spans = new List<CapturedChatSpan>
                    {
                        new CapturedChatSpan("John Doe says: Good evening.", "#FFFFFF")
                    }
                }
            };

            string exportedHtml = ChatLogHtmlExporter.GenerateHtml(originalLines);
            var parsed = ChatLogHtmlParser.Parse(exportedHtml);

            Assert.Equal(2, parsed.Count);
            Assert.Equal("* John Doe takes a deep breath.", parsed[0].RawText);
            Assert.Equal("#C2A2DA", parsed[0].Segments[0].ColorHex);
            Assert.Equal("John Doe says: Good evening.", parsed[1].RawText);
            Assert.Equal("#FFFFFF", parsed[1].Segments[0].ColorHex);
        }

        [Fact]
        public void Parse_InvisionForumRichTextSnippet()
        {
            string forumHtml = @"<div class=""ipsType_normal ipsType_richText"">
  <p><span style=""color:#c2a3da;"">* Michael Vance lights a cigarette.</span></p>
  <p><span style=""color:#32cd32;"">You have paid $50 to the bartender.</span></p>
  <p>Michael Vance says: Keep the change.</p>
</div>";

            var parsed = ChatLogHtmlParser.Parse(forumHtml);

            Assert.Equal(3, parsed.Count);
            Assert.Equal("* Michael Vance lights a cigarette.", parsed[0].RawText);
            Assert.Equal("#C2A3DA", parsed[0].Segments[0].ColorHex);
            Assert.True(parsed[0].HasExplicitColors);

            Assert.Equal("You have paid $50 to the bartender.", parsed[1].RawText);
            Assert.Equal("#32CD32", parsed[1].Segments[0].ColorHex);
            Assert.True(parsed[1].HasExplicitColors);

            Assert.Equal("Michael Vance says: Keep the change.", parsed[2].RawText);
            Assert.False(parsed[2].HasExplicitColors);
        }

        [Fact]
        public void Parse_BrTagsSplitLines()
        {
            string html = @"* Line one<br>* Line two<br/>* Line three";
            var parsed = ChatLogHtmlParser.Parse(html);

            Assert.Equal(3, parsed.Count);
            Assert.Equal("* Line one", parsed[0].RawText);
            Assert.Equal("* Line two", parsed[1].RawText);
            Assert.Equal("* Line three", parsed[2].RawText);
        }
    }
}
