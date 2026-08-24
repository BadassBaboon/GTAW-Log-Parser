using System;
using GTAWParser.Shared;
using Xunit;

namespace GTAWParser.Shared.Tests
{
    public class AiTextSanitizerTests
    {
        [Fact]
        public void NormalizeTypography_ConvertsCurlyApostrophesAndQuotesToAscii()
        {
            string input = "I’m goin’ down to the “shop” ‘cause it’s open—really.";
            string normalized = AiTextSanitizer.NormalizeTypography(input);

            Assert.Equal("I'm goin' down to the \"shop\" 'cause it's open really.", normalized);
            Assert.DoesNotContain("’", normalized);
            Assert.DoesNotContain("‘", normalized);
            Assert.DoesNotContain("“", normalized);
            Assert.DoesNotContain("”", normalized);
            Assert.DoesNotContain("—", normalized);
        }

        [Fact]
        public void SanitizeResult_StripsThinkingTagsAndNormalizesQuotes()
        {
            string content = "<think>Analyzing tone and speech habits</think>“I’m headin’ out now.”";
            string sanitized = AiTextSanitizer.SanitizeResult(content, "", "I'm heading out now.");

            Assert.Equal("I'm headin' out now.", sanitized);
        }

        [Theory]
        [InlineData("He pulls out his wallet and checks the cash.", "pulls out his wallet and checks the cash.")]
        [InlineData("She carefully adjusts her rearview mirror.", "carefully adjusts her rearview mirror.")]
        [InlineData("They glance around nervously.", "glance around nervously.")]
        [InlineData("The man reaches under the driver seat.", "reaches under the driver seat.")]
        [InlineData("The figure steps from the shadows.", "steps from the shadows.")]
        [InlineData("The woman sighs and leans back.", "sighs and leans back.")]
        [InlineData("The person nods in agreement.", "nods in agreement.")]
        [InlineData("He takes a deep breath.", "takes a deep breath.")]
        [InlineData("pulls out his phone.", "pulls out his phone.")]
        [InlineData("* Tony Soprano slides a thick wad of cash from the inner pocket of his jacket.", "slides a thick wad of cash from the inner pocket of his jacket.")]
        [InlineData("Tony Soprano slides a thick wad of cash from the inner pocket of his jacket.", "slides a thick wad of cash from the inner pocket of his jacket.")]
        [InlineData("* slides a thick wad of cash from the inner pocket of his jacket.", "slides a thick wad of cash from the inner pocket of his jacket.")]
        [InlineData("* He slides a thick wad of cash from the inner pocket of his jacket.", "slides a thick wad of cash from the inner pocket of his jacket.")]
        [InlineData("John Doe reaches into the glovebox.", "reaches into the glovebox.")]
        public void SanitizeResult_MeCommand_StripsThirdPersonPronounsAndEnforcesVerbFirst(string input, string expected)
        {
            string result = AiTextSanitizer.SanitizeResult(input, "/me ", "pulls out wallet");
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("His hands tremble as he holds the steering wheel.", "hands tremble as he holds the steering wheel.")]
        [InlineData("Her eyes widen in shock.", "eyes widen in shock.")]
        [InlineData("Their gaze shifts toward the doorway.", "gaze shifts toward the doorway.")]
        [InlineData("The jaw clenches tightly.", "jaw clenches tightly.")]
        [InlineData("heart races uncontrollably.", "heart races uncontrollably.")]
        [InlineData("* Tony Soprano's eyes widen in shock.", "eyes widen in shock.")]
        [InlineData("Tony Soprano's hands tremble.", "hands tremble.")]
        public void SanitizeResult_MyCommand_StripsPossessivePronouns(string input, string expected)
        {
            string result = AiTextSanitizer.SanitizeResult(input, "/my ", "hands tremble");
            Assert.Equal(expected, result);
        }

        [Fact]
        public void SanitizeResult_ActionCommand_EnforcesProperFinishingPunctuation()
        {
            string result = AiTextSanitizer.SanitizeResult("pulls out a cigarette and lights it", "/me ", "pulls out a cigarette");
            Assert.Equal("pulls out a cigarette and lights it.", result);

            string questionResult = AiTextSanitizer.SanitizeResult("would there be any security cameras nearby", "/do ", "any cameras?");
            Assert.Equal("would there be any security cameras nearby?", questionResult);
        }

        [Fact]
        public void SanitizeResult_HandlesComplexGptOssOutputWithSlopAndPronoun()
        {
            // gpt-oss output with curly apostrophe, quotes, and "He " prefix
            string rawGptOutput = "“He pulls out his driver’s license and hands it over.”";
            string result = AiTextSanitizer.SanitizeResult(rawGptOutput, "/me ", "pulls out id");

            Assert.Equal("pulls out his driver's license and hands it over.", result);
        }

        [Fact]
        public void SanitizeResult_HandlesTonySopranoAsteriskOutput()
        {
            string rawOutput = "* Tony Soprano slides a thick wad of cash from the inner pocket of his jacket.";
            string result = AiTextSanitizer.SanitizeResult(rawOutput, "/me ", "pulls a large wad of cash out of his jacket’s inner pocket.");

            Assert.Equal("slides a thick wad of cash from the inner pocket of his jacket.", result);
        }
    }
}
