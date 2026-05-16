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
    }
}
