using System;
using System.Collections.Generic;
using System.IO;
using GTAWParser.Shared;
using Xunit;

namespace Shared.Tests
{
    public class FiveMChatCaptureServiceTests
    {
        [Fact]
        public void FindOverlap_IdentifiesExactAndPartialOverlaps()
        {
            // Case 1: Partial overlap (last 3 items of seen match first 3 of incoming)
            List<string> seen = new List<string> { "Line 1", "Line 2", "Line 3", "Line 4", "Line 5" };
            List<string> incoming = new List<string> { "Line 3", "Line 4", "Line 5", "Line 6", "Line 7" };

            int overlap = FiveMChatCaptureService.FindOverlap(seen, incoming);
            Assert.Equal(3, overlap);

            // Case 2: Complete overlap (all incoming are already seen)
            List<string> fullSeen = new List<string> { "Line 1", "Line 2", "Line 3", "Line 4" };
            List<string> fullIncoming = new List<string> { "Line 2", "Line 3", "Line 4" };

            int fullOverlap = FiveMChatCaptureService.FindOverlap(fullSeen, fullIncoming);
            Assert.Equal(3, fullOverlap);

            // Case 3: No overlap
            List<string> noSeen = new List<string> { "Line A", "Line B" };
            List<string> noIncoming = new List<string> { "Line C", "Line D" };

            int noOverlap = FiveMChatCaptureService.FindOverlap(noSeen, noIncoming);
            Assert.Equal(0, noOverlap);

            // Case 4: Overlap with timestamp prefixes vs un-prefixed incoming lines
            List<string> timestampedSeen = new List<string> { "[23:22:02] Line A", "[23:22:02] Line B", "[23:22:02] Line C" };
            List<string> rawIncoming = new List<string> { "Line B", "Line C", "Line D" };
            int tsOverlap = FiveMChatCaptureService.FindOverlap(timestampedSeen, rawIncoming);
            Assert.Equal(2, tsOverlap);

            // Case 5: Overlap with slight whitespace or formatting variation (fuzzy matching)
            List<string> seenWithSpaces = new List<string> { "(( (545) Oxygarum: hey ))", "Diego Sendez was kicked for: AFK" };
            List<string> incomingFuzzy = new List<string> { "((  (545)  Oxygarum: hey  ))", "Diego Sendez was kicked for: AFK", "Next Line" };
            int fuzzyOverlap = FiveMChatCaptureService.FindOverlap(seenWithSpaces, incomingFuzzy);
            Assert.Equal(2, fuzzyOverlap);

            // Case 6: Exact 100-line buffer overlap (nothing new typed)
            List<string> buffer100 = new List<string>();
            for (int i = 0; i < 100; i++) buffer100.Add($"[23:00:{i:D2}] Message number {i}");
            List<string> incoming100 = new List<string>();
            for (int i = 0; i < 100; i++) incoming100.Add($"Message number {i}");
            int full100Overlap = FiveMChatCaptureService.FindOverlap(buffer100, incoming100);
            Assert.Equal(100, full100Overlap);

            // Case 7: Empty inputs
            Assert.Equal(0, FiveMChatCaptureService.FindOverlap(new List<string>(), new List<string> { "Line 1" }));
            Assert.Equal(0, FiveMChatCaptureService.FindOverlap(new List<string> { "Line 1" }, new List<string>()));
        }

        [Fact]
        public void AddTimestamp_PrependsOrPreservesTimestamps()
        {
            DateTime now = new DateTime(2026, 8, 22, 14, 30, 45);

            // Already has timestamp
            string withTs = "[12:00:00] John Doe says: Hello world";
            Assert.Equal("[12:00:00] John Doe says: Hello world", FiveMChatCaptureService.AddTimestamp(withTs, now));

            // Missing timestamp
            string withoutTs = "John Doe says: Hello world";
            Assert.Equal("[14:30:45] John Doe says: Hello world", FiveMChatCaptureService.AddTimestamp(withoutTs, now));
        }

        [Fact]
        public void GetTimestamp_ExtractsOrFallsBackCorrectly()
        {
            DateTime fallback = new DateTime(2026, 8, 22, 10, 0, 0);

            // Valid line
            string lineWithTs = "[16:45:12] John Doe says: Hello";
            DateTime extracted = FiveMChatCaptureService.GetTimestamp(lineWithTs, fallback);
            Assert.Equal(16, extracted.Hour);
            Assert.Equal(45, extracted.Minute);
            Assert.Equal(12, extracted.Second);
            Assert.Equal(2026, extracted.Year);

            // Invalid line
            string lineNoTs = "Just some text";
            DateTime fallbackExtracted = FiveMChatCaptureService.GetTimestamp(lineNoTs, fallback);
            Assert.Equal(fallback, fallbackExtracted);
        }

        [Fact]
        public void CreateSessionHeader_FormatsProperly()
        {
            DateTime dt = new DateTime(2026, 8, 22, 9, 15, 30);
            string header = FiveMChatCaptureService.CreateSessionHeader(dt);

            Assert.Equal($"[DATE: {dt:dd/MMM/yyyy}".ToUpperInvariant() + $" | TIME: {dt:HH:mm:ss}]", header);
        }

        [Fact]
        public void SessionFile_StreamsAndReadsCorrectly()
        {
            string sessionFile = FiveMChatCaptureService.SessionFilePath;
            string? originalContent = File.Exists(sessionFile) ? File.ReadAllText(sessionFile) : null;
            string testLine1 = "[12:00:00] Test user says: First line";
            string testLine2 = "[12:00:05] Test user says: Second line";

            try
            {
                // Clear for test
                if (File.Exists(sessionFile)) File.Delete(sessionFile);

                FiveMChatCaptureService.AppendLinesToSession(new List<string> { testLine1, testLine2 });

                string readWithTs = FiveMChatCaptureService.ReadCapturedChat(false);
                Assert.Contains(testLine1, readWithTs);
                Assert.Contains(testLine2, readWithTs);

                string readWithoutTs = FiveMChatCaptureService.ReadCapturedChat(true);
                Assert.Contains("Test user says: First line", readWithoutTs);
                Assert.DoesNotContain("[12:00:00] ", readWithoutTs);
            }
            finally
            {
                if (originalContent != null)
                {
                    File.WriteAllText(sessionFile, originalContent);
                }
                else if (File.Exists(sessionFile))
                {
                    File.Delete(sessionFile);
                }
            }
        }

        [Fact]
        public void SessionFile_Normalization_EliminatesDoubleCarriageReturns()
        {
            string raw = "[12:00:00] First\r\n[12:00:01] Second\r\n";
            string normalized = raw.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n');

            Assert.DoesNotContain("\r\r\n", normalized);
            Assert.Equal("[12:00:00] First\n[12:00:01] Second", normalized);
        }

        [Fact]
        public void CapturedChatLine_SerializationRoundTrip()
        {
            string json = @"[
                {
                    ""t"": ""[18:36:46] Welcome to GTA World."",
                    ""c"": ""#FFFF00"",
                    ""s"": [
                        { ""t"": ""Welcome to "", ""c"": ""#FFFFFF"" },
                        { ""t"": ""GTA World"", ""c"": ""#FFFF00"" },
                        { ""t"": ""."", ""c"": ""#FFFFFF"" }
                    ]
                }
            ]";

            var list = System.Text.Json.JsonSerializer.Deserialize<List<CapturedChatLine>>(json);
            Assert.NotNull(list);
            Assert.Single(list);

            var line = list[0];
            Assert.Equal("[18:36:46] Welcome to GTA World.", line.Text);
            Assert.Equal("#FFFF00", line.DominantColor);
            Assert.Equal(3, line.Spans.Count);
            Assert.Equal("Welcome to ", line.Spans[0].Text);
            Assert.Equal("#FFFFFF", line.Spans[0].Color);
            Assert.Equal("GTA World", line.Spans[1].Text);
            Assert.Equal("#FFFF00", line.Spans[1].Color);
            Assert.Equal(".", line.Spans[2].Text);
            Assert.Equal("#FFFFFF", line.Spans[2].Color);
        }

        [Fact]
        public void CapturedLineReceived_FiresWhenLinesAppended()
        {
            string sessionFile = FiveMChatCaptureService.SessionFilePath;
            string? originalContent = File.Exists(sessionFile) ? File.ReadAllText(sessionFile) : null;
            CapturedChatLine? received = null;
            void Handler(CapturedChatLine line) => received = line;

            FiveMChatCaptureService.CapturedLineReceived += Handler;
            try
            {
                FiveMChatCaptureService.AppendLinesToSession(new List<string> { "Test line for event" });
                Assert.NotNull(received);
                Assert.Contains("Test line for event", received.Text);
            }
            finally
            {
                FiveMChatCaptureService.CapturedLineReceived -= Handler;
                if (originalContent != null)
                {
                    File.WriteAllText(sessionFile, originalContent);
                }
                else if (File.Exists(sessionFile))
                {
                    File.Delete(sessionFile);
                }
            }
        }

        [Fact]
        public void ServerTimezoneHelper_Utc_MatchesUtc()
        {
            DateTime utc = new DateTime(2026, 8, 24, 13, 30, 0, DateTimeKind.Utc);
            DateTime serverTime = ServerTimezoneHelper.GetServerTime("UTC", utc);

            Assert.Equal(13, serverTime.Hour);
            Assert.Equal(30, serverTime.Minute);
        }

        [Fact]
        public void ServerTimezoneHelper_TR_MatchesUtcPlusThree()
        {
            DateTime utc = new DateTime(2026, 8, 24, 13, 30, 0, DateTimeKind.Utc);
            DateTime serverTime = ServerTimezoneHelper.GetServerTime("TR", utc);

            Assert.Equal(16, serverTime.Hour);
            Assert.Equal(30, serverTime.Minute);
        }

        [Fact]
        public void ServerTimezoneHelper_KR_MatchesUtcPlusNine()
        {
            DateTime utc = new DateTime(2026, 8, 24, 13, 30, 0, DateTimeKind.Utc);
            DateTime serverTime = ServerTimezoneHelper.GetServerTime("KR", utc);

            Assert.Equal(22, serverTime.Hour);
            Assert.Equal(30, serverTime.Minute);
        }

        [Fact]
        public void ServerTimezoneHelper_RU_MatchesUtcPlusThree()
        {
            DateTime utc = new DateTime(2026, 8, 24, 13, 30, 0, DateTimeKind.Utc);
            DateTime serverTime = ServerTimezoneHelper.GetServerTime("RU", utc);

            Assert.Equal(16, serverTime.Hour);
            Assert.Equal(30, serverTime.Minute);
        }

        [Fact]
        public void ServerTimezoneHelper_Auto_CalibratesFromLoadingScreenClock()
        {
            DateTime utc = new DateTime(2026, 8, 24, 10, 5, 0, DateTimeKind.Utc);
            
            // GTAW English Clock "10:05" -> Offset 0
            ServerTimezoneHelper.UpdateAutoDetectedClock("10:05", utc);
            Assert.Equal(0, ServerTimezoneHelper.DetectedOffsetHours);
            DateTime serverTime = ServerTimezoneHelper.GetServerTime("Auto", utc);
            Assert.Equal(10, serverTime.Hour);
            Assert.Equal(5, serverTime.Minute);

            // GTAW TR Clock "13:05" -> Offset +3
            ServerTimezoneHelper.UpdateAutoDetectedClock("13:05", utc);
            Assert.Equal(3, ServerTimezoneHelper.DetectedOffsetHours);
            serverTime = ServerTimezoneHelper.GetServerTime("Auto", utc);
            Assert.Equal(13, serverTime.Hour);

            // GTAW KR Clock "19:05" -> Offset +9
            ServerTimezoneHelper.UpdateAutoDetectedClock("19:05", utc);
            Assert.Equal(9, ServerTimezoneHelper.DetectedOffsetHours);
            serverTime = ServerTimezoneHelper.GetServerTime("Auto", utc);
            Assert.Equal(19, serverTime.Hour);
        }

        [Fact]
        public void ServerTimezoneHelper_Auto_CalibratesFromMinimapHudClock()
        {
            DateTime utc = new DateTime(2026, 8, 24, 10, 56, 0, DateTimeKind.Utc);

            // In-game minimap HUD .wxTime "10:56" -> Offset 0
            ServerTimezoneHelper.UpdateAutoDetectedClock("10:56", utc);
            Assert.Equal(0, ServerTimezoneHelper.DetectedOffsetHours);
            DateTime serverTime = ServerTimezoneHelper.GetServerTime("Auto", utc);
            Assert.Equal(10, serverTime.Hour);
            Assert.Equal(56, serverTime.Minute);

            // Turkish server in-game HUD "13:56" -> Offset +3
            ServerTimezoneHelper.UpdateAutoDetectedClock("13:56", utc);
            Assert.Equal(3, ServerTimezoneHelper.DetectedOffsetHours);
            serverTime = ServerTimezoneHelper.GetServerTime("Auto", utc);
            Assert.Equal(13, serverTime.Hour);
            Assert.Equal(56, serverTime.Minute);
        }

        [Fact]
        public void ServerTimezoneHelper_Auto_HandlesDayWrapAround()
        {
            // 23:50 UTC, but Turkish server is 02:50 (+3 hours, next day)
            DateTime utc = new DateTime(2026, 8, 24, 23, 50, 0, DateTimeKind.Utc);
            ServerTimezoneHelper.UpdateAutoDetectedClock("02:50", utc);
            Assert.Equal(3, ServerTimezoneHelper.DetectedOffsetHours);

            DateTime serverTime = ServerTimezoneHelper.GetServerTime("Auto", utc);
            Assert.Equal(2, serverTime.Hour);
            Assert.Equal(25, serverTime.Day); // Advanced to next day!
        }

        [Fact]
        public void ServerTimezoneHelper_AddTimestamp_PreservesNativeTimestamp()
        {
            DateTime serverTime = new DateTime(2026, 8, 24, 18, 0, 0);
            string lineWithNative = "[17:18:02] John Doe says: Hello";
            string formatted = FiveMChatCaptureService.AddTimestamp(lineWithNative, serverTime);

            Assert.Equal("[17:18:02] John Doe says: Hello", formatted);
        }

        [Fact]
        public void ServerTimezoneHelper_AddTimestamp_InjectsServerTimeWhenMissing()
        {
            DateTime serverTime = new DateTime(2026, 8, 24, 18, 5, 23);
            string lineWithoutTs = "John Doe says: Hello";
            string formatted = FiveMChatCaptureService.AddTimestamp(lineWithoutTs, serverTime);

            Assert.Equal("[18:05:23] John Doe says: Hello", formatted);
        }

        [Fact]
        public void SessionStartedAt_ExtractsTimestampFromHeaderFile()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "GTAW_Test_Session_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            string tempFile = Path.Combine(tempDir, "test-session.txt");

            try
            {
                DateTime expectedTime = new DateTime(2026, 8, 29, 14, 15, 30);
                string header = FiveMChatCaptureService.CreateSessionHeader(expectedTime);
                File.WriteAllText(tempFile, header + "\n[14:15:30] Player says: test\n");

                // Verify CreateSessionHeader match
                var match = ChatLineClassifier.DateHeaderRegex.Match(header);
                Assert.True(match.Success);
                Assert.Equal("29/AUG/2026", match.Groups[1].Value);
                Assert.Equal("14:15:30", match.Groups[2].Value);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }
    }
}
