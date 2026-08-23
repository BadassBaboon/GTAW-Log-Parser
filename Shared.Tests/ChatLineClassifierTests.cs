using System;
using System.Collections.Generic;
using GTAWParser.Shared;
using Xunit;

namespace GTAWParser.Shared.Tests
{
    public class ChatLineClassifierTests
    {
        [Theory]
        [InlineData("[18:06:13] * Benjamin Buschetta nods.", ChatLineCategory.Emote)]
        [InlineData("* John Doe looks around.", ChatLineCategory.Emote)]
        [InlineData("* Hello (( Benjamin Buschetta ))*", ChatLineCategory.Action)]
        [InlineData("> The door is locked.", ChatLineCategory.Action)]
        [InlineData("Benjamin Buschetta says: Hello world.", ChatLineCategory.ICSpeech)]
        [InlineData("Benjamin Buschetta says (phone): Yes", ChatLineCategory.ICSpeech)]
        [InlineData("(Car) Driver says: Move along.", ChatLineCategory.ICSpeech)]
        [InlineData("Benjamin Buschetta says [low]: secret whisper", ChatLineCategory.ICWhisper)]
        [InlineData("Benjamin Buschetta whispers [low]: secret whisper", ChatLineCategory.ICWhisper)]
        [InlineData("Benjamin Buschetta shouts: Stop right there!", ChatLineCategory.ICShout)]
        [InlineData("(( (334) Benjamin Buschetta: What's up ))", ChatLineCategory.OOC)]
        [InlineData("(( Global OOC: (6) eloque: Please despawn any vehicles you’re not actively using. ))", ChatLineCategory.Default)]
        [InlineData("(( PM to (12) John Doe: See you at the pier ))", ChatLineCategory.PM)]
        [InlineData("(( PM from (12) John Doe: Sounds good ))", ChatLineCategory.PM)]
        [InlineData("**[S: 1 CH: 2] Benjamin Buschetta: Patrol unit responding.", ChatLineCategory.Radio)]
        [InlineData("[Faction] Dispatcher: All units respond.", ChatLineCategory.Radio)]
        [InlineData("[Advertisement] Selling Ubermacht Zion, call 555-1234", ChatLineCategory.Ads)]
        [InlineData("[PHONE] MorsMutual Operator says: Insuring vehicle.", ChatLineCategory.Phone)]
        [InlineData("[INFO] Your player ID is 334.", ChatLineCategory.SystemInfo)]
        [InlineData("Your vehicle insurance has expired.", ChatLineCategory.Error)]
        [InlineData("Admin DimitriS Rockstar banned Luka Novak for reason: [Failed to roleplay multiple crashes] for 5 days", ChatLineCategory.Error)]
        [InlineData("Admin Noryx permanently Rockstar banned Julian Herne for reason: [Cheating]", ChatLineCategory.Error)]
        [InlineData("Your vehicle has been teleported to your location.", ChatLineCategory.Success)]
        [InlineData("Vehicle parked.", ChatLineCategory.Success)]
        [InlineData("You've used Slushy.", ChatLineCategory.Success)]
        [InlineData("Refilling 3.17 gallons, please wait... ((9 seconds))", ChatLineCategory.Success)]
        [InlineData("We've placed a blip on your map to help you locate your vehicle.", ChatLineCategory.Warning)]
        [InlineData("[DATE: 23/AUG/2026 | TIME: 18:06:05]", ChatLineCategory.SessionHeader)]
        public void Classify_CategorizesCorrectly(string line, ChatLineCategory expected)
        {
            var (timestamp, content) = ChatLineClassifier.SplitTimestamp(line);
            ChatLineCategory category = ChatLineClassifier.Classify(content);
            Assert.Equal(expected, category);
        }

        [Fact]
        public void SplitTimestamp_ExtractsPrefixAndContent()
        {
            string line = "[18:06:13] * Benjamin Buschetta nods.";
            var (ts, content) = ChatLineClassifier.SplitTimestamp(line);

            Assert.Equal("[18:06:13] ", ts);
            Assert.Equal("* Benjamin Buschetta nods.", content);
        }

        [Fact]
        public void GetHexColor_ReturnsValidHexStrings()
        {
            foreach (ChatLineCategory cat in Enum.GetValues(typeof(ChatLineCategory)))
            {
                string hex = ChatLineClassifier.GetHexColor(cat);
                Assert.StartsWith("#", hex);
                Assert.Equal(7, hex.Length);
            }
        }

        [Fact]
        public void ParseSpans_SessionHeader_ReturnsTimestampGray()
        {
            string line = "[DATE: 23/AUG/2026 | TIME: 21:58:59]";
            List<CapturedChatSpan> spans = ChatLineClassifier.ParseSpans(line);

            Assert.NotNull(spans);
            Assert.Single(spans);
            Assert.Equal("#7F8C8D", spans[0].Color);
        }

        [Fact]
        public void ParseSpans_BlipNotification_ReturnsYellow()
        {
            string line = "We've placed a blip on your map to help you locate your vehicle.";
            List<CapturedChatSpan> spans = ChatLineClassifier.ParseSpans(line);

            Assert.NotNull(spans);
            Assert.Single(spans);
            Assert.Equal("#FFFF00", spans[0].Color);
        }

        [Fact]
        public void ParseSpans_AdminBan_ReturnsRed()
        {
            string line = "Admin DimitriS Rockstar banned Luka Novak for reason: [Cheating] for 5 days";
            List<CapturedChatSpan> spans = ChatLineClassifier.ParseSpans(line);

            Assert.NotNull(spans);
            Assert.Single(spans);
            Assert.Equal("#FF0000", spans[0].Color);
        }

        [Fact]
        public void ParseSpans_GlobalOoc_ReturnsRedAdminName()
        {
            string line = "(( Global OOC: (6) eloque: Please despawn any vehicles you’re not actively using. ))";
            List<CapturedChatSpan> spans = ChatLineClassifier.ParseSpans(line);

            Assert.Equal(3, spans.Count);
            Assert.Equal("(( Global OOC: (6) ", spans[0].Text);
            Assert.Equal("#FFFFFF", spans[0].Color);
            Assert.Equal("eloque", spans[1].Text);
            Assert.Equal("#FF0000", spans[1].Color);
            Assert.Equal(": Please despawn any vehicles you’re not actively using. ))", spans[2].Text);
            Assert.Equal("#FFFFFF", spans[2].Color);
        }

        [Fact]
        public void ParseSpans_TeleportSuccessLine_ReturnsGreen()
        {
            string line = "Your vehicle has been teleported to your location. Please wait for a few seconds if the vehicle does not load in.";
            List<CapturedChatSpan> spans = ChatLineClassifier.ParseSpans(line);

            Assert.NotNull(spans);
            Assert.Single(spans);
            Assert.Equal("#31CB31", spans[0].Color);
            Assert.Equal(line, spans[0].Text);
        }

        [Fact]
        public void ParseSpans_WelcomeLine_ColorsGtaWorldYellow()
        {
            string line = "Welcome to GTA World.";
            List<CapturedChatSpan> spans = ChatLineClassifier.ParseSpans(line);

            Assert.Equal(3, spans.Count);
            Assert.Equal("Welcome to ", spans[0].Text);
            Assert.Equal("#FFFFFF", spans[0].Color);
            Assert.Equal("GTA World", spans[1].Text);
            Assert.Equal("#FFFF00", spans[1].Color);
            Assert.Equal(".", spans[2].Text);
            Assert.Equal("#FFFFFF", spans[2].Color);
        }

        [Fact]
        public void ParseSpans_WeatherTemperature_ColorsValuesGreen()
        {
            string line = "Temperature: 33.3°C (91.92F), it is currently Sunny.";
            List<CapturedChatSpan> spans = ChatLineClassifier.ParseSpans(line);

            Assert.Equal(7, spans.Count);
            Assert.Equal("Temperature: ", spans[0].Text);
            Assert.Equal("#FFFFFF", spans[0].Color);
            Assert.Equal("33.3°C", spans[1].Text);
            Assert.Equal("#31CB31", spans[1].Color);
            Assert.Equal("91.92F", spans[3].Text);
            Assert.Equal("#31CB31", spans[3].Color);
            Assert.Equal("Sunny", spans[5].Text);
            Assert.Equal("#31CB31", spans[5].Color);
        }

        [Fact]
        public void ParseSpans_InfoPrefix_ColorsTagBlue()
        {
            string line = "[INFO] Your player ID is 361.";
            List<CapturedChatSpan> spans = ChatLineClassifier.ParseSpans(line);

            Assert.Equal(2, spans.Count);
            Assert.Equal("[INFO]", spans[0].Text);
            Assert.Equal("#1E90FF", spans[0].Color);
            Assert.Equal(" Your player ID is 361.", spans[1].Text);
            Assert.Equal("#FFFFFF", spans[1].Color);
        }

        [Fact]
        public void ParseSpans_StorePrompt_ColorsNameBlueAndPromptYellow()
        {
            string line = "Route 68 24/7: Press Y to open store.";
            List<CapturedChatSpan> spans = ChatLineClassifier.ParseSpans(line);

            Assert.Equal(2, spans.Count);
            Assert.Equal("Route 68 24/7: ", spans[0].Text);
            Assert.Equal("#1E90FF", spans[0].Color);
            Assert.Equal("Press Y to open store.", spans[1].Text);
            Assert.Equal("#FFFF00", spans[1].Color);
        }

        [Fact]
        public void ParseSpans_ItemPurchase_ColorsAmountsCorrectly()
        {
            string line = "You bought a total of 1 item(s) for $150.";
            List<CapturedChatSpan> spans = ChatLineClassifier.ParseSpans(line);

            Assert.Equal(5, spans.Count);
            Assert.Equal("1", spans[1].Text);
            Assert.Equal("#1E90FF", spans[1].Color);
            Assert.Equal("$150", spans[3].Text);
            Assert.Equal("#31CB31", spans[3].Color);
        }

        [Fact]
        public void ParseSpans_RefillGallons_ReturnsGreenWithWhiteNumber()
        {
            string line = "Refilling 3.17 gallons, please wait... ((9 seconds))";
            List<CapturedChatSpan> spans = ChatLineClassifier.ParseSpans(line);

            Assert.Equal(3, spans.Count);
            Assert.Equal("Refilling ", spans[0].Text);
            Assert.Equal("#31CB31", spans[0].Color);
            Assert.Equal("3.17", spans[1].Text);
            Assert.Equal("#FFFFFF", spans[1].Color);
            Assert.Equal(" gallons, please wait... ((9 seconds))", spans[2].Text);
            Assert.Equal("#31CB31", spans[2].Color);
        }

        [Fact]
        public void ParseSpans_GasFillReceipt_ReturnsPriceGreen()
        {
            string line = "[San Chianski Gas Station]: Filled 3.17 gallons for $72!";
            List<CapturedChatSpan> spans = ChatLineClassifier.ParseSpans(line);

            Assert.Equal(2, spans.Count);
            Assert.Equal("[San Chianski Gas Station]: Filled 3.17 gallons for ", spans[0].Text);
            Assert.Equal("#FFFFFF", spans[0].Color);
            Assert.Equal("$72!", spans[1].Text);
            Assert.Equal("#31CB31", spans[1].Color);
        }

        [Fact]
        public void ParseSpans_EmbeddedTildeCodes_ParsesAccurately()
        {
            string line = "~g~Green text ~r~Red text ~w~White text";
            List<CapturedChatSpan> spans = ChatLineClassifier.ParseSpans(line);

            Assert.Equal(3, spans.Count);
            Assert.Equal("Green text ", spans[0].Text);
            Assert.Equal("#31CB31", spans[0].Color);
            Assert.Equal("Red text ", spans[1].Text);
            Assert.Equal("#FF0000", spans[1].Color);
            Assert.Equal("White text", spans[2].Text);
            Assert.Equal("#FFFFFF", spans[2].Color);
        }
    }
}
