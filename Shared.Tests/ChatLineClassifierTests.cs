using System;
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
        [InlineData("(( PM to (12) John Doe: See you at the pier ))", ChatLineCategory.PM)]
        [InlineData("(( PM from (12) John Doe: Sounds good ))", ChatLineCategory.PM)]
        [InlineData("**[S: 1 CH: 2] Benjamin Buschetta: Patrol unit responding.", ChatLineCategory.Radio)]
        [InlineData("[Faction] Dispatcher: All units respond.", ChatLineCategory.Radio)]
        [InlineData("[Advertisement] Selling Ubermacht Zion, call 555-1234", ChatLineCategory.Ads)]
        [InlineData("[PHONE] MorsMutual Operator says: Insuring vehicle.", ChatLineCategory.Phone)]
        [InlineData("[INFO] Your player ID is 334.", ChatLineCategory.SystemInfo)]
        [InlineData("Your vehicle insurance has expired.", ChatLineCategory.SystemInfo)]
        [InlineData("[DATE: 23/AUG/2026 | TIME: 18:06:05]", ChatLineCategory.SystemInfo)]
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
    }
}
