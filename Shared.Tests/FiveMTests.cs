using System;
using System.IO;
using GTAWParser.Shared;
using Xunit;

namespace Shared.Tests
{
    public class FiveMTests
    {
        [Fact]
        public void FiveMDetector_ResolveFiveMPaths_NormalizesRootAndAppPaths()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "FiveMTest_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(tempDir);
                string fivemApp = Path.Combine(tempDir, "FiveM.app");
                Directory.CreateDirectory(fivemApp);

                FiveMPaths pathsFromRoot = FiveMDetector.ResolveFiveMPaths(tempDir);
                Assert.Equal(tempDir, pathsFromRoot.RootDirectory);
                Assert.Equal(fivemApp, pathsFromRoot.AppDataDirectory);
                Assert.Equal(Path.Combine(fivemApp, "plugins"), pathsFromRoot.PluginsDirectory);
                Assert.Equal(Path.Combine(fivemApp, "logs"), pathsFromRoot.LogsDirectory);
                Assert.Equal(Path.Combine(fivemApp, "CitizenFX.ini"), pathsFromRoot.CitizenFXIniPath);

                FiveMPaths pathsFromApp = FiveMDetector.ResolveFiveMPaths(fivemApp);
                Assert.Equal(tempDir, pathsFromApp.RootDirectory);
                Assert.Equal(fivemApp, pathsFromApp.AppDataDirectory);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void FiveMReShadeFixer_MoveReShadeFilesToPlugins_MovesFiles()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "FiveMTest_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(tempDir);
                string fivemApp = Path.Combine(tempDir, "FiveM.app");
                Directory.CreateDirectory(fivemApp);

                string dxgi = Path.Combine(tempDir, "dxgi.dll");
                File.WriteAllText(dxgi, "fake dxgi dll content");

                string reshadeFolder = Path.Combine(tempDir, "reshade-shaders");
                Directory.CreateDirectory(reshadeFolder);
                File.WriteAllText(Path.Combine(reshadeFolder, "test.fx"), "// fx shader");

                bool success = FiveMReShadeFixer.MoveReShadeFilesToPlugins(tempDir, out int count, out string msg);

                Assert.True(success);
                Assert.Equal(2, count);
                Assert.False(File.Exists(dxgi));
                Assert.False(Directory.Exists(reshadeFolder));

                string plugins = Path.Combine(fivemApp, "plugins");
                Assert.True(File.Exists(Path.Combine(plugins, "dxgi.dll")));
                Assert.True(Directory.Exists(Path.Combine(plugins, "reshade-shaders")));
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void FiveMReShadeFixer_ScanLogForReShadeBypass_FindsWarning()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "FiveMTest_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(tempDir);
                string logsDir = Path.Combine(tempDir, "FiveM.app", "logs");
                Directory.CreateDirectory(logsDir);

                string logPath = Path.Combine(logsDir, "CitizenFX_log_2026-08-06T154438.log");
                string logContent = "[     76453] [b3751_GTAProce]             MainThrd/ Blocked load of ReShade version 5 or higher - it has a bug that will lead to game crashes in GPU drivers or d3d11.dll.\n" +
                                   "[     76453] [b3751_GTAProce]             MainThrd/ If you want to force it to load anyway, add the following section to ^2G:\\FiveM\\FiveM.app\\CitizenFX.ini:^7\n" +
                                   "[     76453] [b3751_GTAProce]             MainThrd/     [Addons]\n" +
                                   "[     76453] [b3751_GTAProce]             MainThrd/     ReShade5=ID:13981da3 acknowledged that ReShade 5.x has a bug that will lead to game crashes\n" +
                                   "[     76453] [b3751_GTAProce]             MainThrd/ \n";
                File.WriteAllText(logPath, logContent);

                bool found = FiveMReShadeFixer.ScanLogForReShadeBypass(tempDir, out string bypassLine, out string logFileName, out string statusMsg);

                Assert.True(found);
                Assert.Equal("ReShade5=ID:13981da3 acknowledged that ReShade 5.x has a bug that will lead to game crashes", bypassLine);
                Assert.Equal("CitizenFX_log_2026-08-06T154438.log", logFileName);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void FiveMReShadeFixer_ApplyReShadeBypassToIni_InjectsAddonsSection()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "FiveMTest_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(tempDir);
                string fivemApp = Path.Combine(tempDir, "FiveM.app");
                Directory.CreateDirectory(fivemApp);
                string iniPath = Path.Combine(fivemApp, "CitizenFX.ini");

                string initialIni = "[Game]\nIVPath=G:\\Grand Theft Auto V\nSavedBuildNumber=3751\nUpdateChannel=canary\nDefaultBuild=3258\n";
                File.WriteAllText(iniPath, initialIni);

                string bypassLine = "ReShade5=ID:13981da3 acknowledged that ReShade 5.x has a bug that will lead to game crashes";
                bool success = FiveMReShadeFixer.ApplyReShadeBypassToIni(tempDir, bypassLine, out string msg);

                Assert.True(success);
                string updatedIni = File.ReadAllText(iniPath);
                Assert.Contains("[Game]", updatedIni);
                Assert.Contains("SavedBuildNumber=3751", updatedIni);
                Assert.Contains("[Addons]", updatedIni);
                Assert.Contains(bypassLine, updatedIni);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void FiveMConfigManager_GetAndSetUpdateChannel_UpdatesIniPreservingGameSection()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "FiveMTest_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(tempDir);
                string fivemApp = Path.Combine(tempDir, "FiveM.app");
                Directory.CreateDirectory(fivemApp);
                string iniPath = Path.Combine(fivemApp, "CitizenFX.ini");

                string initialIni = "[Game]\nIVPath=G:\\Grand Theft Auto V\nUpdateChannel=canary\n";
                File.WriteAllText(iniPath, initialIni);

                string channel = FiveMConfigManager.GetUpdateChannel(tempDir);
                Assert.Equal("Latest (Unstable)", channel);

                bool success = FiveMConfigManager.SetUpdateChannel(tempDir, "Release", out string msg);
                Assert.True(success);

                string updatedIni = File.ReadAllText(iniPath);
                Assert.Contains("UpdateChannel=production", updatedIni);
                Assert.Contains("IVPath=G:\\Grand Theft Auto V", updatedIni);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void FiveMConfigManager_SetGtaVPath_ValidatesExecutablesAndUpdateIni()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "FiveMTest_" + Guid.NewGuid().ToString("N"));
            string gtaDir = Path.Combine(Path.GetTempPath(), "GtaTest_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(tempDir);
                Directory.CreateDirectory(gtaDir);
                string fivemApp = Path.Combine(tempDir, "FiveM.app");
                Directory.CreateDirectory(fivemApp);

                // Validation fails without executable
                bool failNoExe = FiveMConfigManager.SetGtaVPath(tempDir, gtaDir, out string failMsg);
                Assert.False(failNoExe);

                // Create GTA5.exe
                File.WriteAllText(Path.Combine(gtaDir, "GTA5.exe"), "fake");

                bool success = FiveMConfigManager.SetGtaVPath(tempDir, gtaDir, out string msg);
                Assert.True(success);

                string iniPath = Path.Combine(fivemApp, "CitizenFX.ini");
                string iniContent = File.ReadAllText(iniPath);
                Assert.Contains($"IVPath={gtaDir}", iniContent);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
                if (Directory.Exists(gtaDir))
                    Directory.Delete(gtaDir, true);
            }
        }

        [Fact]
        public void FiveMConfigManager_ClearCitizenAndServerCache_DeletesTargetDirectories()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "FiveMTest_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(tempDir);
                string fivemApp = Path.Combine(tempDir, "FiveM.app");
                string citizenDir = Path.Combine(fivemApp, "citizen");
                string serverCacheDir = Path.Combine(fivemApp, "data", "server-cache-priv");

                Directory.CreateDirectory(citizenDir);
                Directory.CreateDirectory(serverCacheDir);
                File.WriteAllText(Path.Combine(citizenDir, "test.txt"), "dummy");
                File.WriteAllText(Path.Combine(serverCacheDir, "cache.dat"), "dummy");

                bool citSuccess = FiveMConfigManager.ClearCitizenFolder(tempDir, out string citMsg);
                bool cacheSuccess = FiveMConfigManager.ClearServerCache(tempDir, out string cacheMsg);

                Assert.True(citSuccess);
                Assert.True(cacheSuccess);
                Assert.False(Directory.Exists(citizenDir));
                Assert.False(Directory.Exists(serverCacheDir));
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }
    }
}
