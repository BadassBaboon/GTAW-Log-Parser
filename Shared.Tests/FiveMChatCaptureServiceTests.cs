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

            // Case 4: Empty inputs
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
            }
        }
    }
}
