using System;
using System.IO;
using System.Reflection;
using GTAWParser.Shared;
using Xunit;

namespace GTAWParser.Shared.Tests
{
    public class ChatLogParserTests : IDisposable
    {
        private readonly string _tempRoot;
        private readonly string _resourceDir;

        public ChatLogParserTests()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "gtaw-tests-" + Guid.NewGuid().ToString("N"));
            _resourceDir = Path.Combine(_tempRoot, "client_resources", "play.gta.world_22005");
            Directory.CreateDirectory(_resourceDir);
            // Set the scanner to point at our temp directory.
            ChatLogScanner.InitializeServerIp(_tempRoot);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }

        private void WriteStorage(string chatLog)
        {
            string storagePath = Path.Combine(_resourceDir, ".storage");
            string json = "{\"server_version\":\"GTA World v1.0.0\",\"chat_log\":" +
                          System.Text.Json.JsonSerializer.Serialize(chatLog) +
                          ",\"rememberuser\":true}";
            File.WriteAllText(storagePath, json);
            ChatLogScanner.InitializeServerIp(_tempRoot);
        }

        [Fact]
        public void Parse_ValidStorage_ReturnsDecodedLog()
        {
            WriteStorage("[09:30:15] John_Doe says: Hello\n[09:31:00] Jane_Smith shouts: Hi back\n");

            string result = ChatLogParser.Parse(_tempRoot);

            Assert.Contains("John_Doe says: Hello", result);
            Assert.Contains("Jane_Smith shouts: Hi back", result);
            Assert.Contains("\n", result);
        }

        [Fact]
        public void Parse_HtmlEntitiesAreDecoded()
        {
            WriteStorage("[09:30:15] John_Doe says: A &amp; B &lt;3\n");

            string result = ChatLogParser.Parse(_tempRoot);

            Assert.Contains("A & B <3", result);
        }

        [Fact]
        public void Parse_TrailingNewlineIsStripped()
        {
            WriteStorage("line1\nline2\n");

            string result = ChatLogParser.Parse(_tempRoot);

            Assert.False(result.EndsWith("\n", StringComparison.Ordinal));
            Assert.EndsWith("line2", result);
        }

        [Fact]
        public void Parse_NonExistentFile_InvokesOnErrorAndReturnsEmpty()
        {
            // No .storage file at all
            string emptyRoot = Path.Combine(Path.GetTempPath(), "gtaw-empty-" + Guid.NewGuid().ToString("N"));
            try
            {
                Exception? captured = new Exception("placeholder");
                string result = ChatLogParser.Parse(emptyRoot, ex => captured = ex);

                Assert.Equal(string.Empty, result);
                // For a missing file we get an IOException-like, not a null.
                Assert.NotNull(captured);
                Assert.NotEqual("placeholder", captured!.Message);
            }
            finally
            {
                if (Directory.Exists(emptyRoot)) Directory.Delete(emptyRoot, true);
            }
        }

        [Fact]
        public void Parse_NoChatLogField_InvokesOnErrorWithNullAndReturnsEmpty()
        {
            string storagePath = Path.Combine(_resourceDir, ".storage");
            File.WriteAllText(storagePath, "{\"server_version\":\"GTA World v1.0.0\",\"rememberuser\":true}");
            ChatLogScanner.InitializeServerIp(_tempRoot);

            Exception? captured = new Exception("placeholder");
            string result = ChatLogParser.Parse(_tempRoot, ex => captured = ex);

            Assert.Equal(string.Empty, result);
            Assert.Null(captured);
        }

        [Fact]
        public void StripTimestamps_RemovesHmsBrackets()
        {
            string input = "[09:30:15] foo\n[10:00:00] bar";

            string result = ChatLogParser.StripTimestamps(input);

            Assert.Equal("foo\nbar", result);
        }

        [Fact]
        public void StripTimestamps_EmptyInput_ReturnsEmpty()
        {
            Assert.Equal(string.Empty, ChatLogParser.StripTimestamps(string.Empty));
        }

        [Fact]
        public void StripTimestamps_NoTimestamps_ReturnsUnchanged()
        {
            string input = "plain text without timestamps";

            string result = ChatLogParser.StripTimestamps(input);

            Assert.Equal(input, result);
        }

        [Fact]
        public void Parse_ConcurrentWriteLock_ReadsSuccessfully()
        {
            string storagePath = Path.Combine(_resourceDir, ".storage");
            string json = "{\"server_version\":\"GTA World v1.0.0\",\"chat_log\":\"[12:00:00] Concurrent message\\n\",\"rememberuser\":true}";
            File.WriteAllText(storagePath, json);
            ChatLogScanner.InitializeServerIp(_tempRoot);

            // Simulate the game process keeping the file open with write access and ReadWrite sharing
            using (FileStream activeGameLock = new FileStream(storagePath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
            {
                string result = ChatLogParser.Parse(_tempRoot);
                Assert.Contains("Concurrent message", result);
            }
        }

        [Fact]
        public void NormalizeLineEndings_EliminatesDegenerateDoubleCrLf()
        {
            // \r\r\n is the regression that caused blank lines in txt backups
            string corrupted = "Line 1\r\r\nLine 2\r\r\nLine 3";
            string result = ChatLogParser.NormalizeLineEndings(corrupted);

            Assert.Equal($"Line 1{Environment.NewLine}Line 2{Environment.NewLine}Line 3", result);
        }

        [Fact]
        public void NormalizeLineEndings_LeavesStandardCrLfClean()
        {
            string standard = $"Line 1{Environment.NewLine}Line 2{Environment.NewLine}Line 3";
            string result = ChatLogParser.NormalizeLineEndings(standard);

            Assert.Equal(standard, result);
        }

        [Fact]
        public void NormalizeLineEndings_ConvertsUnixLfToWindowsCrLf()
        {
            string unix = "Line 1\nLine 2\nLine 3";
            string result = ChatLogParser.NormalizeLineEndings(unix);

            Assert.Equal($"Line 1{Environment.NewLine}Line 2{Environment.NewLine}Line 3", result);
        }

        [Fact]
        public void NormalizeLineEndings_ConvertsLoneCrToCrLf()
        {
            string mac = "Line 1\rLine 2\rLine 3";
            string result = ChatLogParser.NormalizeLineEndings(mac);

            Assert.Equal($"Line 1{Environment.NewLine}Line 2{Environment.NewLine}Line 3", result);
        }

        [Fact]
        public void NormalizeLineEndings_PreservesIntentionalEmptyLines()
        {
            string withBlankLine = "Line 1\r\n\r\nLine 2";
            string result = ChatLogParser.NormalizeLineEndings(withBlankLine);

            Assert.Equal($"Line 1{Environment.NewLine}{Environment.NewLine}Line 2", result);
        }

        [Fact]
        public void NormalizeLineEndings_HandlesNullAndEmpty()
        {
            Assert.Equal(string.Empty, ChatLogParser.NormalizeLineEndings(null));
            Assert.Equal(string.Empty, ChatLogParser.NormalizeLineEndings(string.Empty));
        }

        [Fact]
        public void NormalizeLineEndings_PreventsRegressionOfOldReplaceHack()
        {
            // The old flawed code did: parsed.Replace("\n", Environment.NewLine)
            // On Windows this turned \r\n into \r\r\n, doubling line spacing.
            string logFromSession = "[12:00:00] Line 1\r\n[12:00:01] Line 2\r\n[12:00:02] Line 3\r\n";
            string normalized = ChatLogParser.NormalizeLineEndings(logFromSession);

            Assert.DoesNotContain("\r\r\n", normalized);
            var split = normalized.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
            Assert.Equal(4, split.Length);
            Assert.Equal("[12:00:00] Line 1", split[0]);
            Assert.Equal("[12:00:01] Line 2", split[1]);
            Assert.Equal("[12:00:02] Line 3", split[2]);
            Assert.Equal(string.Empty, split[3]);
        }

        [Theory]
        [InlineData("Line 1\r\r\nLine 2")]
        [InlineData("Line 1\r\nLine 2")]
        [InlineData("Line 1\nLine 2")]
        [InlineData("Line 1\rLine 2")]
        [InlineData("Line 1\r\n\r\nLine 2")]
        [InlineData("Line 1\r\r\r\nLine 2")]
        [InlineData("\r\n\r\nLine 1\r\n")]
        [InlineData("no line breaks at all")]
        public void NormalizeLineEndings_IsIdempotent(string input)
        {
            // A backup can be re-read, re-parsed and re-written. If normalising twice differed
            // from normalising once, spacing would drift further on every save.
            string once = ChatLogParser.NormalizeLineEndings(input);
            string twice = ChatLogParser.NormalizeLineEndings(once);

            Assert.Equal(once, twice);
        }

        [Theory]
        [InlineData("A\r\r\nB")]
        [InlineData("A\r\r\r\nB")]
        [InlineData("A\r\r\r\r\nB")]
        public void NormalizeLineEndings_CollapsesAnyRunOfCarriageReturns(string input)
        {
            // However many stray CRs accumulated, the break is still exactly one line break.
            string result = ChatLogParser.NormalizeLineEndings(input);

            Assert.Equal($"A{Environment.NewLine}B", result);
        }

        [Fact]
        public void NormalizeLineEndings_NeverEmitsABareCarriageReturn()
        {
            string messy = "A\r\r\nB\rC\nD\r\nE\r\r\r\nF";
            string result = ChatLogParser.NormalizeLineEndings(messy);

            // Every CR in the output must be part of a CRLF pair. A stray CR is exactly what an
            // editor renders as the phantom blank line this fix was about.
            for (int i = 0; i < result.Length; i++)
            {
                if (result[i] != '\r') continue;
                bool pairedWithLf = i + 1 < result.Length && result[i + 1] == '\n';
                Assert.True(pairedWithLf, "Found a bare carriage return at index " + i);
            }
        }

    }
}
