using System;
using System.IO;
using GTAWParser.Shared;
using Xunit;

namespace GTAWParser.Shared.Tests
{
    public class ChatLogScannerTests : IDisposable
    {
        private readonly string _tempRoot;

        public ChatLogScannerTests()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "gtaw-scanner-" + Guid.NewGuid().ToString("N"));
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }

        private void WriteResource(string serverFolderName, string serverTag)
        {
            string resourceDir = Path.Combine(_tempRoot, "client_resources", serverFolderName);
            Directory.CreateDirectory(resourceDir);
            File.WriteAllText(
                Path.Combine(resourceDir, ".storage"),
                $"{{\"server_version\":\"{serverTag}\",\"chat_log\":\"\"}}");
        }

        [Fact]
        public void InitializeServerIp_EmptyPath_KeepsDefaults()
        {
            ChatLogScanner.InitializeServerIp("");

            Assert.Equal("Not Found", ChatLogScanner.ResourceDirectory);
            Assert.Contains("play.gta.world_22005", ChatLogScanner.LogLocation);
        }

        [Fact]
        public void InitializeServerIp_GtaWorldStorage_FindsResourceDirectory()
        {
            WriteResource("play.gta.world_22005", "GTA World v1.2");
            Directory.CreateDirectory(_tempRoot);

            ChatLogScanner.InitializeServerIp(_tempRoot);

            Assert.Equal("play.gta.world_22005", ChatLogScanner.ResourceDirectory);
            Assert.Contains("play.gta.world_22005", ChatLogScanner.LogLocation);
        }

        [Fact]
        public void InitializeServerIp_NonGtaWorldStorage_KeepsDefaults()
        {
            WriteResource("some.other.server", "Some Other Server v1.0");

            ChatLogScanner.InitializeServerIp(_tempRoot);

            Assert.Equal("Not Found", ChatLogScanner.ResourceDirectory);
        }

        [Fact]
        public void InitializeServerIp_MultipleStorages_PicksNewest()
        {
            WriteResource("old.server", "GTA World v1.0");
            WriteResource("new.server", "GTA World v1.1");

            string newStoragePath = Path.Combine(_tempRoot, "client_resources", "new.server", ".storage");
            File.SetLastWriteTimeUtc(newStoragePath, DateTime.UtcNow);
            string oldStoragePath = Path.Combine(_tempRoot, "client_resources", "old.server", ".storage");
            File.SetLastWriteTimeUtc(oldStoragePath, DateTime.UtcNow.AddDays(-1));

            ChatLogScanner.InitializeServerIp(_tempRoot);

            Assert.Equal("new.server", ChatLogScanner.ResourceDirectory);
        }
    }
}
