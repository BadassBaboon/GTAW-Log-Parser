using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using GTAWParser.Shared;
using MahApps.Metro.Controls;

namespace Assistant.UI
{
    public partial class FiveMToolsWindow : MetroWindow
    {
        private static readonly SolidColorBrush RedBrush = new SolidColorBrush(Color.FromRgb(255, 77, 77));      // #FF4D4D
        private static readonly SolidColorBrush GreenBrush = new SolidColorBrush(Color.FromRgb(0, 255, 102));    // #00FF66
        private static readonly SolidColorBrush YellowBrush = new SolidColorBrush(Color.FromRgb(255, 204, 0));   // #FFCC00

        public FiveMToolsWindow()
        {
            InitializeComponent();
            CheckReShadeStatus();
        }

        private string GetFiveMDirectory()
        {
            string currentPath = Properties.Settings.Default.DirectoryPath;
            if (string.IsNullOrWhiteSpace(currentPath) || !Directory.Exists(currentPath))
            {
                currentPath = FiveMDetector.DetectFiveMDirectory();
                if (!string.IsNullOrEmpty(currentPath))
                {
                    Properties.Settings.Default.DirectoryPath = currentPath;
                    Properties.Settings.Default.Save();
                }
            }
            return currentPath;
        }

        private void CheckReShadeStatus()
        {
            string path = GetFiveMDirectory();
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                SetStatus("⚠ FiveM installation directory could not be found.", RedBrush);
                Step1MoveFilesBtn.IsEnabled = false;
                Step2EnableReShadeBtn.IsEnabled = false;
                return;
            }

            FiveMPaths resolved = FiveMDetector.ResolveFiveMPaths(path);
            string root = resolved.RootDirectory;
            string plugins = resolved.PluginsDirectory;

            // Check if ReShade files exist in root directory (where FiveM.exe is)
            bool dxgiInRoot = File.Exists(Path.Combine(root, "dxgi.dll")) || File.Exists(Path.Combine(root, "d3d11.dll"));
            bool shadersInRoot = Directory.Exists(Path.Combine(root, "reshade-shaders"));
            bool iniInRoot = File.Exists(Path.Combine(root, "ReShade.ini"));
            bool hasReShadeInRoot = dxgiInRoot || shadersInRoot || iniInRoot;

            // Check if ReShade files were already moved to plugins directory
            bool dxgiInPlugins = File.Exists(Path.Combine(plugins, "dxgi.dll")) || File.Exists(Path.Combine(plugins, "d3d11.dll"));
            bool shadersInPlugins = Directory.Exists(Path.Combine(plugins, "reshade-shaders"));
            bool hasReShadeInPlugins = dxgiInPlugins || shadersInPlugins;

            if (hasReShadeInRoot)
            {
                Step1MoveFilesBtn.IsEnabled = true;
                Step2EnableReShadeBtn.IsEnabled = false;
                SetStatus("ReShade files detected in FiveM root. Click \"1. Move ReShade Files\" to proceed.", YellowBrush);
            }
            else if (hasReShadeInPlugins)
            {
                Step1MoveFilesBtn.IsEnabled = false;
                Step2EnableReShadeBtn.IsEnabled = true;
                SetStatus("✓ ReShade files are in FiveM.app\\plugins. Launch FiveM once, reach main menu, close game, then click \"2. Enable ReShade\" above.", GreenBrush);
            }
            else
            {
                Step1MoveFilesBtn.IsEnabled = false;
                Step2EnableReShadeBtn.IsEnabled = false;
                SetStatus("⚠ ReShade is not installed to FiveM.exe. Please run the ReShade installer, select FiveM.exe, and try again.", RedBrush);
            }
        }

        private void SetStatus(string message, Brush brush)
        {
            StatusMessageText.Text = message;
            StatusMessageText.Foreground = brush;
        }

        private void Step1MoveFilesBtn_Click(object sender, RoutedEventArgs e)
        {
            string path = GetFiveMDirectory();
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                SetStatus("⚠ Valid FiveM installation directory could not be found.", RedBrush);
                return;
            }

            bool success = FiveMReShadeFixer.MoveReShadeFilesToPlugins(path, out int count, out string msg);
            if (success)
            {
                Step1MoveFilesBtn.IsEnabled = false;
                Step2EnableReShadeBtn.IsEnabled = true;
                SetStatus("✓ ReShade files moved to FiveM.app\\plugins! Launch FiveM once, reach main menu, close game, then click \"2. Enable ReShade\" above.", GreenBrush);
            }
            else
            {
                SetStatus($"⚠ {msg}", RedBrush);
            }
        }

        private void Step2EnableReShadeBtn_Click(object sender, RoutedEventArgs e)
        {
            string path = GetFiveMDirectory();
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                SetStatus("⚠ Valid FiveM installation directory could not be found.", RedBrush);
                return;
            }

            bool logFound = FiveMReShadeFixer.ScanLogForReShadeBypass(path, out string bypassLine, out string logFileName, out string statusMsg);
            if (logFound && !string.IsNullOrEmpty(bypassLine))
            {
                bool iniSuccess = FiveMReShadeFixer.ApplyReShadeBypassToIni(path, bypassLine, out string iniMsg);
                if (iniSuccess)
                {
                    SetStatus("✓ Done! FiveM will now allow ReShade 5+ usage without crashing.", GreenBrush);
                }
                else
                {
                    SetStatus($"⚠ Error updating CitizenFX.ini: {iniMsg}", RedBrush);
                }
            }
            else
            {
                SetStatus("⚠ ReShade bypass key not found in logs yet. Please launch FiveM once until main menu, close FiveM, and try again.", RedBrush);
            }
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
