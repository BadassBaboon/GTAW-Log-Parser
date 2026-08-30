using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using GTAWParser.Shared;
using Xunit;

namespace Shared.Tests
{
    public class AppSettingsManagerTests
    {
        [Fact]
        public void ParseLegacyUserConfigXml_ExtractsSettingsCorrectly()
        {
            string xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<configuration>
    <userSettings>
        <Assistant.Properties.Settings>
            <setting name=""BackupPath"" serializeAs=""String"">
                <value>D:\MyCustomBackup\</value>
            </setting>
            <setting name=""BackupChatLogAutomatically"" serializeAs=""String"">
                <value>True</value>
            </setting>
            <setting name=""IntervalTime"" serializeAs=""String"">
                <value>15</value>
            </setting>
            <setting name=""ScreenshotFontSize"" serializeAs=""String"">
                <value>16.5</value>
            </setting>
            <setting name=""Theme"" serializeAs=""String"">
                <value>Dark</value>
            </setting>
        </Assistant.Properties.Settings>
    </userSettings>
</configuration>";

            var dict = AppSettingsManager.ParseLegacyUserConfigXml(xml);

            Assert.Equal(5, dict.Count);
            Assert.Equal(@"D:\MyCustomBackup\", dict["BackupPath"]);
            Assert.Equal("True", dict["BackupChatLogAutomatically"]);
            Assert.Equal("15", dict["IntervalTime"]);
            Assert.Equal("16.5", dict["ScreenshotFontSize"]);
            Assert.Equal("Dark", dict["Theme"]);
        }

        [Fact]
        public void ParseLegacyUserConfigXml_HandlesEmptyOrInvalidXmlGracefully()
        {
            Assert.Empty(AppSettingsManager.ParseLegacyUserConfigXml(string.Empty));
            Assert.Empty(AppSettingsManager.ParseLegacyUserConfigXml("not valid xml"));
            Assert.Empty(AppSettingsManager.ParseLegacyUserConfigXml("<root></root>"));
        }

        [Theory]
        [InlineData("True", typeof(bool), true)]
        [InlineData("true", typeof(bool), true)]
        [InlineData("1", typeof(bool), true)]
        [InlineData("False", typeof(bool), false)]
        [InlineData("false", typeof(bool), false)]
        [InlineData("0", typeof(bool), false)]
        [InlineData("42", typeof(int), 42)]
        [InlineData("-10", typeof(int), -10)]
        [InlineData("14.5", typeof(double), 14.5)]
        [InlineData("Hello World", typeof(string), "Hello World")]
        public void ConvertStringToType_ConvertsBasicTypesCorrectly(string input, Type targetType, object expected)
        {
            object? result = AppSettingsManager.ConvertStringToType(input, targetType);
            Assert.NotNull(result);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void ConvertJsonElementToType_ConvertsTypesCorrectly()
        {
            string json = "{\"strVal\":\"Test\",\"intVal\":123,\"boolVal\":true,\"doubleVal\":45.67}";
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.Equal("Test", AppSettingsManager.ConvertJsonElementToType(root.GetProperty("strVal"), typeof(string)));
            Assert.Equal(123, AppSettingsManager.ConvertJsonElementToType(root.GetProperty("intVal"), typeof(int)));
            Assert.Equal(true, AppSettingsManager.ConvertJsonElementToType(root.GetProperty("boolVal"), typeof(bool)));
            Assert.Equal(45.67, AppSettingsManager.ConvertJsonElementToType(root.GetProperty("doubleVal"), typeof(double)));
        }

        [Fact]
        public void SaveAndLoad_RoundTripsJsonFileProperly()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "GTAW_Settings_Test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            string configFile = Path.Combine(tempDir, "test_settings.json");

            try
            {
                var dict = new Dictionary<string, object?>
                {
                    { "BackupPath", @"C:\GTAW\Backups" },
                    { "BackupChatLogAutomatically", true },
                    { "IntervalTime", 20 },
                    { "ScreenshotFontSize", 18.0 }
                };

                string json = JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(configFile, json);

                Assert.True(File.Exists(configFile));
                string readJson = File.ReadAllText(configFile);

                using var doc = JsonDocument.Parse(readJson);
                Assert.Equal(@"C:\GTAW\Backups", doc.RootElement.GetProperty("BackupPath").GetString());
                Assert.True(doc.RootElement.GetProperty("BackupChatLogAutomatically").GetBoolean());
                Assert.Equal(20, doc.RootElement.GetProperty("IntervalTime").GetInt32());
                Assert.Equal(18.0, doc.RootElement.GetProperty("ScreenshotFontSize").GetDouble());
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }
    }
}
