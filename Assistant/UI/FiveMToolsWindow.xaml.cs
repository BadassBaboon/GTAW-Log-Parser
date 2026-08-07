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
            string iniPath = resolved.CitizenFXIniPath;

            bool dxgiInRoot = File.Exists(Path.Combine(root, "dxgi.dll")) || File.Exists(Path.Combine(root, "d3d11.dll"));
            bool shadersInRoot = Directory.Exists(Path.Combine(root, "reshade-shaders"));
            bool iniInRoot = File.Exists(Path.Combine(root, "ReShade.ini"));
            bool hasReShadeInRoot = dxgiInRoot || shadersInRoot || iniInRoot;

            bool dxgiInPlugins = File.Exists(Path.Combine(plugins, "dxgi.dll")) || File.Exists(Path.Combine(plugins, "d3d11.dll"));
            bool shadersInPlugins = Directory.Exists(Path.Combine(plugins, "reshade-shaders"));
            bool hasReShadeInPlugins = dxgiInPlugins || shadersInPlugins;

            bool hasIniKey = false;
            if (File.Exists(iniPath))
            {
                string iniContent = File.ReadAllText(iniPath);
                hasIniKey = iniContent.Contains("ReShade5=", StringComparison.OrdinalIgnoreCase);
            }

            if (hasReShadeInRoot)
            {
                Step1MoveFilesBtn.IsEnabled = true;
                Step2EnableReShadeBtn.IsEnabled = true;
                SetStatus("ReShade files detected in FiveM root. Click \"1. Move ReShade Files\" or \"2. Enable ReShade\" to complete setup.", YellowBrush);
            }
            else if (hasReShadeInPlugins)
            {
                Step1MoveFilesBtn.IsEnabled = false;
                Step2EnableReShadeBtn.IsEnabled = true;
                if (hasIniKey)
                {
                    SetStatus("✓ Done! ReShade files are in FiveM.app\\plugins and ReShade key is active in CitizenFX.ini.", GreenBrush);
                }
                else
                {
                    SetStatus("✓ ReShade files are in FiveM.app\\plugins. Click \"2. Enable ReShade\" to apply the ReShade key.", GreenBrush);
                }
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
                string ackLine = FiveMReShadeFixer.GenerateReShadeAckLine();
                FiveMReShadeFixer.ApplyReShadeKeyToIni(path, ackLine, out _);

                Step1MoveFilesBtn.IsEnabled = false;
                Step2EnableReShadeBtn.IsEnabled = true;
                SetStatus("✓ ReShade files moved to FiveM.app\\plugins & ReShade key configured in CitizenFX.ini! You can now launch FiveM.", GreenBrush);
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

            string ackLine = FiveMReShadeFixer.GenerateReShadeAckLine();
            bool iniSuccess = FiveMReShadeFixer.ApplyReShadeKeyToIni(path, ackLine, out string iniMsg);
            if (iniSuccess)
            {
                SetStatus("✓ Done! FiveM will now allow ReShade 5+ usage without issues.", GreenBrush);
            }
            else
            {
                SetStatus($"⚠ Error updating CitizenFX.ini: {iniMsg}", RedBrush);
            }
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
