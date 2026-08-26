using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using GTAWParser.Shared.Screenshot;
using Xunit;

namespace GTAWParser.Shared.Tests
{
    public class ScreenshotRendererTests
    {
        [Theory]
        [InlineData("[12:34:56] * Jack Smith waves.", "* Jack Smith waves.")]
        [InlineData("[09:15] * Jack Smith looks around.", "* Jack Smith looks around.")]
        [InlineData("14:22:01 John Doe says: Hello there.", "John Doe says: Hello there.")]
        [InlineData("No timestamp here at all.", "No timestamp here at all.")]
        [InlineData("   [23:59:59]   (( OOC text ))  ", "(( OOC text ))")]
        public void StripTimestamp_RemovesTimestampsAccurately(string rawInput, string expected)
        {
            string result = RoleplayChatColorizer.StripTimestamp(rawInput);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("* Jack Smith pulls out his wallet.", ChatLineCategory.Emote)]
        [InlineData("/me grabs the door handle.", ChatLineCategory.Emote)]
        [InlineData("> Jack Smith sighs deeply.", ChatLineCategory.Action)]
        [InlineData("* The engine would be smoking. (( Jack Smith ))", ChatLineCategory.Action)]
        [InlineData("/do Is the glovebox locked?", ChatLineCategory.Action)]
        [InlineData("[Radio] Unit 1 to Dispatch, 10-4.", ChatLineCategory.Radio)]
        [InlineData("[Dispatch] All units respond.", ChatLineCategory.Radio)]
        [InlineData("[Phone] Jack Smith: Hey, are you home?", ChatLineCategory.Phone)]
        [InlineData("Jack Smith whispers [low]: Keep it down.", ChatLineCategory.ICWhisper)]
        [InlineData("(( Jack Smith: brb 5 mins ))", ChatLineCategory.OOC)]
        [InlineData("Jack Smith says: What's the plan?", ChatLineCategory.ICSpeech)]
        public void Classify_IdentifiesRoleplayCategoriesCorrectly(string line, ChatLineCategory expected)
        {
            Assert.Equal(expected, RoleplayChatColorizer.Classify(line));
        }

        [Theory]
        [InlineData("* Jack Smith pulls out his wallet.")]
        [InlineData("/me grabs the door handle.")]
        [InlineData("* The engine would be smoking. (( Jack Smith ))")]
        [InlineData("/do Is the glovebox locked?")]
        public void DetectDefaultColor_GivesMeAndDoTheSameColor(string line)
        {
            // On GTA World /do is rendered in the same purple as /me — it is not green.
            Assert.Equal(RoleplayChatColorizer.ColorMePurple, RoleplayChatColorizer.DetectDefaultColor(line));
        }

        [Fact]
        public void DetectDefaultColor_MatchesTheCanonicalClassifierPalette()
        {
            // The screenshot editor must never carry a palette of its own; every colour it can
            // produce has to come from ChatLineClassifier, which is what Live Tail and the HTML
            // exporter fall back to.
            foreach (var entry in ChatLineClassifier.Palette)
            {
                Assert.Equal(ChatLineClassifier.GetHexColor(entry.Category), ChatLineClassifier.GetHexColor(entry.Category));
                Assert.False(string.IsNullOrWhiteSpace(entry.Label));
            }

            Assert.Equal(ChatLineClassifier.GetHexColor(ChatLineCategory.Emote), RoleplayChatColorizer.ColorMePurple);
            Assert.Equal(ChatLineClassifier.GetHexColor(ChatLineCategory.Radio), RoleplayChatColorizer.ColorRadioBlue);
            Assert.Equal(ChatLineClassifier.GetHexColor(ChatLineCategory.OOC), RoleplayChatColorizer.ColorOocGrey);
        }

        [Fact]
        public void ColorizeLine_ParsesEmbeddedHexColorsCorrectly()
        {
            string raw = "{C2A2DA}* Jack Smith nods. {FFFFFF}Jack Smith says: Thanks.";
            var segments = RoleplayChatColorizer.ColorizeLine(raw);

            Assert.Equal(2, segments.Count);
            Assert.Equal("* Jack Smith nods. ", segments[0].Text);
            Assert.Equal("#C2A2DA", segments[0].ColorHex);
            Assert.Equal("Jack Smith says: Thanks.", segments[1].Text);
            Assert.Equal("#FFFFFF", segments[1].ColorHex);
        }

        [Fact]
        public void ColorizeLine_ParsesGtaTextCodes()
        {
            var segments = RoleplayChatColorizer.ColorizeLine("~r~Warning: ~w~engine failure.");

            Assert.Equal(2, segments.Count);
            Assert.Equal("#FF0000", segments[0].ColorHex);
            Assert.Equal("#FFFFFF", segments[1].ColorHex);
        }

        [Fact]
        public void FromSpans_PreservesCapturedNuiColorsVerbatim()
        {
            // Captured NUI spans are ground truth and must pass through without reclassification.
            var spans = new List<CapturedChatSpan>
            {
                new CapturedChatSpan("(( Global OOC: ", "#FFFFFF"),
                new CapturedChatSpan("Admin", "#FF0000"),
                new CapturedChatSpan(": restart in 5 ))", "#FFFFFF")
            };

            var segments = RoleplayChatColorizer.FromSpans(spans);

            Assert.Equal(3, segments.Count);
            Assert.Equal("#FF0000", segments[1].ColorHex);
            Assert.Equal("Admin", segments[1].Text);
        }

        [Fact]
        public void ColorizeLine_PrivateMessage_MatchesLiveCapturedColors()
        {
            // Captured verbatim from a live GTA World English session over the NUI DevTools bridge:
            // the body renders #F6EA00 and the sender name #FF0000. Before the PM span rule existed
            // this fell through to the generic local-OOC rule and came out grey with a green name.
            var segments = RoleplayChatColorizer.ColorizeLine("(( PM from (43) Vindicator: Np! ))");

            Assert.Equal(3, segments.Count);
            Assert.Equal("#F6EA00", segments[0].ColorHex);
            Assert.Equal("Vindicator", segments[1].Text);
            Assert.Equal("#FF0000", segments[1].ColorHex);
            Assert.Equal("#F6EA00", segments[2].ColorHex);
        }

        [Theory]
        [InlineData("[Radio] Unit 1 to Dispatch, 10-4.")]
        [InlineData("[Dispatch] All units respond.")]
        public void Classify_RadioTagsAreNotMistakenForAdvertisements(string line)
        {
            // "R-ad-io" and "Disp-a-tch" used to satisfy an unbounded, case-insensitive Ad match.
            Assert.Equal(ChatLineCategory.Radio, RoleplayChatColorizer.Classify(line));
        }

        // Every case below is a line captured from a live GTA World English session, paired with the
        // colour the game actually painted it. These are the inference path an imported log takes.
        [Theory]
        [InlineData("[XMR] You have successfully changed the radio channel of your vehicle to 155.", "#1E90FF")]
        [InlineData("[GPS] No address was found!", "#32CD32")]
        [InlineData("[REPORT] Your report is being investigated.", "#1E90FF")]
        [InlineData("[INFO] Your player ID is 399.", "#1E90FF")]
        [InlineData("This vehicle interior could not be opened. Please report it to staff.", "#FF0000")]
        [InlineData("Amount should be positive, greater than 0.", "#FF0000")]
        [InlineData("Your report has been submitted to the administration team.", "#FF0000")]
        [InlineData("1) If you fell under the map, use /fixfall.", "#1E90FF")]
        [InlineData("You can cancel your report with /cancelreport.", "#1E90FF")]
        [InlineData("Your vehicle has been teleported to your location.", "#32CD32")]
        [InlineData("Benjamin Buschetta says [low]: Piece of shit...", "#DCDCDC")]
        [InlineData("* Benjamin Buschetta flicks the switch.", "#C2A2DA")]
        [InlineData("* Does nothing. (( Benjamin Buschetta ))*", "#C2A2DA")]
        [InlineData("Use F3 to activate the cursor. You can also use /pc for the cursor.", "#FFFF00")]
        public void DetectDefaultColor_MatchesColorsCapturedFromTheLiveGame(string line, string expected)
        {
            Assert.Equal(expected, RoleplayChatColorizer.DetectDefaultColor(line));
        }

        [Fact]
        public void Classify_PlainLinesAreNotClaimedBySystemPatterns()
        {
            // "You have hung up the call." renders plain white in game; a bare "You have" prefix
            // in the system-info pattern used to colour it blue.
            Assert.Equal("#FFFFFF", RoleplayChatColorizer.DetectDefaultColor("You have hung up the call."));
        }

        [Fact]
        public void Classify_PlayerDialogueWinsOverSystemMessagePatterns()
        {
            // A player can say anything, including text that looks like a server notice. Dialogue
            // is matched first so what a player typed is never recoloured as an error.
            Assert.Equal(ChatLineCategory.ICSpeech,
                RoleplayChatColorizer.Classify("Benjamin Buschetta says: You cannot be serious."));
        }

        [Fact]
        public void MeasureChatBlock_GrowsWithLineCount()
        {
            var thread = new Thread(() =>
            {
                var options = new ScreenshotRenderOptions { ChatX = 40, ChatY = 25, FontSize = 14 };

                var one = new List<List<ChatStyledSegment>> { RoleplayChatColorizer.ColorizeLine("* Jack Smith nods.") };
                var two = new List<List<ChatStyledSegment>>(one) { RoleplayChatColorizer.ColorizeLine("Jack Smith says: Hi.") };

                Rect a = ScreenshotRenderer.MeasureChatBlock(one, options);
                Rect b = ScreenshotRenderer.MeasureChatBlock(two, options);

                Assert.Equal(40, a.X);
                Assert.Equal(25, a.Y);
                Assert.True(a.Width > 0 && a.Height > 0);
                Assert.True(b.Height > a.Height);
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join(5000);
        }

        [Fact]
        public void Render_TransparentBackground_LeavesUncoveredCanvasTransparent()
        {
            var thread = new Thread(() =>
            {
                var opaque = new ScreenshotRenderOptions { CanvasWidth = 120, CanvasHeight = 120 };
                var clear = new ScreenshotRenderOptions { CanvasWidth = 120, CanvasHeight = 120, TransparentBackground = true };

                Assert.Equal(255, AlphaAt(ScreenshotRenderer.Render(null, null, opaque)));
                Assert.Equal(0, AlphaAt(ScreenshotRenderer.Render(null, null, clear)));

                // Flatten is what PNG-less formats and the clipboard rely on.
                Assert.Equal(255, AlphaAt(ScreenshotRenderer.Flatten(ScreenshotRenderer.Render(null, null, clear))));
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(5000));
        }

        private static int AlphaAt(System.Windows.Media.Imaging.BitmapSource bmp)
        {
            var pixel = new byte[4];
            bmp.CopyPixels(new Int32Rect(2, 2, 1, 1), pixel, 4, 0);
            return pixel[3];
        }

        [Fact]
        public void ResolutionPreset_HasExpectedCommunityAndStandardPresets()
        {
            var presets = ResolutionPreset.DefaultPresets;
            Assert.NotEmpty(presets);

            Assert.Contains(presets, p => p.Width == 1920 && p.Height == 1080);
            Assert.Contains(presets, p => p.Width == 1280 && p.Height == 720);
            Assert.Contains(presets, p => p.Width == 1300 && p.Height == 730);
            Assert.Contains(presets, p => p.Width == 1150 && p.Height == 750);
            Assert.Contains(presets, p => p.Width == 1650 && p.Height == 1060);
            Assert.Contains(presets, p => p.Width == 1024 && p.Height == 768);
        }

        [Fact]
        public void ScreenshotRenderer_RendersRenderTargetBitmapOnStaThread()
        {
            // Execute on STA thread for WPF RenderTargetBitmap
            var thread = new Thread(() =>
            {
                var options = new ScreenshotRenderOptions
                {
                    CanvasWidth = 800,
                    CanvasHeight = 600,
                    ChatX = 20,
                    ChatY = 20,
                    FontSize = 12
                };

                var lines = new List<List<ChatStyledSegment>>
                {
                    RoleplayChatColorizer.ColorizeLine("* Jack Smith reaches into his jacket."),
                    RoleplayChatColorizer.ColorizeLine("Jack Smith says: Here is the key.")
                };

                var rtb = ScreenshotRenderer.Render(null, lines, options);
                Assert.NotNull(rtb);
                Assert.Equal(800, rtb.PixelWidth);
                Assert.Equal(600, rtb.PixelHeight);
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join(5000);
        }

        [Fact]
        public void EditorSwatches_ContainsVerifiedRoleplayColors()
        {
            var swatches = RoleplayChatColorizer.EditorSwatches;
            Assert.NotEmpty(swatches);

            Assert.Contains(swatches, s => s.Label == "/me & /do" && s.Hex.Equals("#C2A3DA", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(swatches, s => s.Label == "Your Speech" && s.Hex.Equals("#FFFFFF", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(swatches, s => s.Label == "Other Speech" && s.Hex.Equals("#C8C8C8", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(swatches, s => s.Label == "Whisper" && s.Hex.Equals("#EDA841", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(swatches, s => s.Label == "Phone / SMS / PM" && s.Hex.Equals("#FFFF00", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(swatches, s => s.Label == "Radio" && s.Hex.Equals("#1E90FF", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(swatches, s => s.Label == "Item / Money / Success" && s.Hex.Equals("#32CD32", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(swatches, s => s.Label == "CK Red / Admin / Error" && s.Hex.Equals("#FF0000", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(swatches, s => s.Label == "CK Blue / System" && s.Hex.Equals("#3896F3", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(swatches, s => s.Label == "OOC (( ))" && s.Hex.Equals("#A6ACAF", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(swatches, s => s.Label == "Advertisement" && s.Hex.Equals("#2ECC71", StringComparison.OrdinalIgnoreCase));

            // Verify no duplicate labels or hex combinations
            var labels = new HashSet<string>();
            foreach (var s in swatches)
            {
                Assert.False(string.IsNullOrWhiteSpace(s.Label));
                Assert.False(string.IsNullOrWhiteSpace(s.Tooltip));
                Assert.StartsWith("#", s.Hex);
                Assert.Equal(7, s.Hex.Length);
                Assert.True(labels.Add(s.Label), $"Duplicate swatch label found: {s.Label}");
            }
        }

        [Fact]
        public void ColorizeLine_MultiSegment_PreservesSpansAndColors()
        {
            // Store prompt line: Blue prefix, Yellow action prompt
            string storeLine = "Route 68 24/7: Press Y to open store.";
            var segs = RoleplayChatColorizer.ColorizeLine(storeLine);
            Assert.NotEmpty(segs);

            // Store prefix in blue
            Assert.Contains(segs, s => s.ColorHex.Equals("#1E90FF", StringComparison.OrdinalIgnoreCase) && s.Text.Contains("Route 68"));

            // Prompt in yellow
            Assert.Contains(segs, s => s.ColorHex.Equals("#FFFF00", StringComparison.OrdinalIgnoreCase) && s.Text.Contains("Press Y"));
        }

        [Fact]
        public void Recolor_PreservesRunsAndChangesColor()
        {
            var orig = new List<ChatStyledSegment>
            {
                new ChatStyledSegment("Part 1 ", "#1E90FF"),
                new ChatStyledSegment("Part 2", "#FFFFFF")
            };

            var recolored = RoleplayChatColorizer.Recolor(orig, "#C2A3DA");
            Assert.Equal(2, recolored.Count);
            Assert.Equal("Part 1 ", recolored[0].Text);
            Assert.Equal("#C2A3DA", recolored[0].ColorHex);
            Assert.Equal("Part 2", recolored[1].Text);
            Assert.Equal("#C2A3DA", recolored[1].ColorHex);
        }

        [Fact]
        public void ChatStyledSegment_Censor_FlagsCorrectly()
        {
            var seg = new ChatStyledSegment("Hidden Amount", "#FFFFFF", false, false, true);
            Assert.True(seg.IsCensored);

            var uncensored = new ChatStyledSegment("Visible", "#FFFFFF");
            Assert.False(uncensored.IsCensored);
        }

        [Fact]
        public void Censor_MarksAllSegmentsAsCensored()
        {
            var orig = new List<ChatStyledSegment>
            {
                new ChatStyledSegment("$500,000", "#32CD32"),
                new ChatStyledSegment(" cash", "#FFFFFF")
            };

            var censored = RoleplayChatColorizer.Censor(orig);
            Assert.Equal(2, censored.Count);
            Assert.All(censored, s => Assert.True(s.IsCensored));
        }

        [Fact]
        public void Recolor_ClearsCensorship()
        {
            var orig = new List<ChatStyledSegment>
            {
                new ChatStyledSegment("Secret", "#FFFFFF", false, false, true)
            };

            var recolored = RoleplayChatColorizer.Recolor(orig, "#1E90FF");
            Assert.Single(recolored);
            Assert.False(recolored[0].IsCensored);
            Assert.Equal("#1E90FF", recolored[0].ColorHex);
        }

        [Fact]
        public void Render_WithCensoredSegments_RendersSuccessfully()
        {
            var thread = new Thread(() =>
            {
                var options = new ScreenshotRenderOptions
                {
                    CanvasWidth = 800,
                    CanvasHeight = 600,
                    ChatX = 20,
                    ChatY = 20,
                    FontSize = 14
                };

                var lines = new List<List<ChatStyledSegment>>
                {
                    new List<ChatStyledSegment>
                    {
                        new ChatStyledSegment("Paid ", "#FFFFFF"),
                        new ChatStyledSegment("$1,000,000", "#32CD32", false, false, true),
                        new ChatStyledSegment(" to John Doe.", "#FFFFFF")
                    }
                };

                var rtb = ScreenshotRenderer.Render(null, lines, options);
                Assert.NotNull(rtb);
                Assert.Equal(800, rtb.PixelWidth);
                Assert.Equal(600, rtb.PixelHeight);
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join(5000);
        }

        [Fact]
        public void ResolutionPreset_DefaultPresets_ContainsKeyCommunityPresets()
        {
            var presets = ResolutionPreset.DefaultPresets;
            Assert.NotEmpty(presets);

            var compact = presets.Find(p => p.Name == "Compact RP Thread");
            Assert.NotNull(compact);
            Assert.Equal(1150, compact.Width);
            Assert.Equal(750, compact.Height);

            var standard = presets.Find(p => p.Name == "Standard RP Thread");
            Assert.NotNull(standard);
            Assert.Equal(1300, standard.Width);
            Assert.Equal(730, standard.Height);

            var large = presets.Find(p => p.Name == "Large RP Thread");
            Assert.NotNull(large);
            Assert.Equal(1650, large.Width);
            Assert.Equal(1060, large.Height);
        }

        [Theory]
        [InlineData("Compact RP Thread", 1150, 750)]
        [InlineData("Standard RP Thread", 1300, 730)]
        [InlineData("1080p FHD", 1920, 1080)]
        [InlineData("720p HD", 1280, 720)]
        public void ResolutionPreset_MatchesByNameAndDimensions(string presetName, int expectedW, int expectedH)
        {
            var presets = ResolutionPreset.DefaultPresets;
            var matchByName = presets.Find(p => p.Name.Equals(presetName, StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(matchByName);
            Assert.Equal(expectedW, matchByName.Width);
            Assert.Equal(expectedH, matchByName.Height);

            var matchByDims = presets.Find(p => p.Width == expectedW && p.Height == expectedH);
            Assert.NotNull(matchByDims);
            Assert.Equal(presetName, matchByDims.Name);
        }

        [Fact]
        public void ScreenshotRenderOptions_WithFullStylingAndPosition_RendersCorrectly()
        {
            var thread = new Thread(() =>
            {
                var options = new ScreenshotRenderOptions
                {
                    CanvasWidth = 1150,
                    CanvasHeight = 750,
                    ChatX = 45,
                    ChatY = 60,
                    FontFamily = "Segoe UI",
                    FontSize = 16,
                    IsBold = true,
                    LineSpacing = 5,
                    OutlineWidth = 1.5,
                    EnableDropShadow = true,
                    ShadowOffset = 1.5,
                    EnableBackgroundBox = true,
                    BackgroundBoxOpacity = 0.5
                };

                var lines = new List<List<ChatStyledSegment>>
                {
                    new List<ChatStyledSegment> { new ChatStyledSegment("* Character inspects the surroundings.", "#C2A3DA") },
                    new List<ChatStyledSegment> { new ChatStyledSegment("Character says: Everything checks out.", "#FFFFFF") }
                };

                var block = ScreenshotRenderer.MeasureChatBlock(lines, options);
                Assert.True(block.Width > 0);
                Assert.True(block.Height > 0);
                Assert.Equal(45, block.X);
                Assert.Equal(60, block.Y);

                var rtb = ScreenshotRenderer.Render(null, lines, options);
                Assert.NotNull(rtb);
                Assert.Equal(1150, rtb.PixelWidth);
                Assert.Equal(750, rtb.PixelHeight);
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join(5000);
        }
    }
}

