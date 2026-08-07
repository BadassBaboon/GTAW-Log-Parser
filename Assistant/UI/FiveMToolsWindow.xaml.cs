using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Media;
using GTAWParser.Shared;
using MahApps.Metro.Controls;
using MessageBox = System.Windows.MessageBox;

namespace Assistant.UI
{
    public partial class FiveMToolsWindow : MetroWindow
    {
        private static readonly SolidColorBrush RedBrush = new SolidColorBrush(Color.FromRgb(255, 77, 77));      // #FF4D4D
        private static readonly SolidColorBrush GreenBrush = new SolidColorBrush(Color.FromRgb(0, 255, 102));    // #00FF66
        private static readonly SolidColorBrush YellowBrush = new SolidColorBrush(Color.FromRgb(255, 204, 0));   // #FFCC00

        private bool _isInitializing = true;

        public FiveMToolsWindow()
        {
            InitializeComponent();
            LoadFiveMToolsData();
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

        private void LoadFiveMToolsData()
        {
            _isInitializing = true;

            string path = GetFiveMDirectory();
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
            {
                // 1. Load Update Channel
                string currentChannel = FiveMConfigManager.GetUpdateChannel(path);
                foreach (ComboBoxItem item in UpdateChannelComboBox.Items)
                {
                    if (item.Content != null && item.Content.ToString()!.Equals(currentChannel, StringComparison.OrdinalIgnoreCase))
                    {
                        UpdateChannelComboBox.SelectedItem = item;
                        break;
                    }
                }

                // 2. Load GTA V Path
                string gtaPath = FiveMConfigManager.GetGtaVPath(path);
                GtaVPathTextBox.Text = gtaPath;
            }

            _isInitializing = false;

            // 3. Check ReShade Status
            CheckReShadeStatus();
        }

        private void CheckReShadeStatus()
        {
            string path = GetFiveMDirectory();
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                SetStatus("⚠ FiveM installation directory could not be found.", RedBrush);
                SetupReShadeBtn.IsEnabled = false;
                SetupReShadeBtn.Content = "Setup & Enable ReShade";
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

            if (hasReShadeInPlugins && hasIniKey)
            {
                SetupReShadeBtn.IsEnabled = false;
                SetupReShadeBtn.Content = "ReShade Enabled";
                SetStatus("✓ ReShade is already enabled on your FiveM installation!", GreenBrush);
            }
            else if (hasReShadeInRoot)
            {
                SetupReShadeBtn.IsEnabled = true;
                SetupReShadeBtn.Content = "Setup & Enable ReShade";
                SetStatus("ReShade installation detected in FiveM root. Click \"Setup & Enable ReShade\" above to automate setup.", YellowBrush);
            }
            else if (hasReShadeInPlugins)
            {
                SetupReShadeBtn.IsEnabled = true;
                SetupReShadeBtn.Content = "Setup & Enable ReShade";
                SetStatus("✓ ReShade files are in FiveM.app\\plugins. Click \"Setup & Enable ReShade\" to write the CitizenFX.ini configuration.", YellowBrush);
            }
            else
            {
                SetupReShadeBtn.IsEnabled = false;
                SetupReShadeBtn.Content = "Setup & Enable ReShade";
                SetStatus("⚠ ReShade is not installed to FiveM.exe. Please run the ReShade installer, select FiveM.exe, and try again.", RedBrush);
            }
        }

        private void SetStatus(string message, Brush brush)
        {
            StatusMessageText.Text = message;
            StatusMessageText.Foreground = brush;
        }

        private void UpdateChannelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;

            if (UpdateChannelComboBox.SelectedItem is ComboBoxItem item && item.Content != null)
            {
                string channel = item.Content.ToString()!;
                string path = GetFiveMDirectory();

                if (!string.IsNullOrEmpty(path))
                {
                    bool success = FiveMConfigManager.SetUpdateChannel(path, channel, out string statusMsg);
                    SetStatus(statusMsg, success ? GreenBrush : RedBrush);
                }
            }
        }

        private void BrowseGtaVPathBtn_Click(object sender, RoutedEventArgs e)
        {
            string path = GetFiveMDirectory();
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select GTA V Installation Folder (containing GTA5.exe)";
                dialog.SelectedPath = GtaVPathTextBox.Text;

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    bool success = FiveMConfigManager.SetGtaVPath(path, dialog.SelectedPath, out string statusMsg);
                    if (success)
                    {
                        GtaVPathTextBox.Text = dialog.SelectedPath;
                        SetStatus(statusMsg, GreenBrush);
                    }
                    else
                    {
                        MessageBox.Show(this, statusMsg, "Invalid GTA V Folder", MessageBoxButton.OK, MessageBoxImage.Warning);
                        SetStatus($"⚠ {statusMsg}", RedBrush);
                    }
                }
            }
        }

        private void ClearCitizenBtn_Click(object sender, RoutedEventArgs e)
        {
            MessageBoxResult confirm = MessageBox.Show(this,
                "Deleting the citizen folder will force FiveM to redownload clean system files on launch.\n\nAre you sure you want to clear the citizen folder?",
                "Clear Citizen Folder", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (confirm == MessageBoxResult.Yes)
            {
                string path = GetFiveMDirectory();
                bool success = FiveMConfigManager.ClearCitizenFolder(path, out string statusMsg);
                SetStatus(statusMsg, success ? GreenBrush : RedBrush);
            }
        }

        private void ClearServerCacheBtn_Click(object sender, RoutedEventArgs e)
        {
            MessageBoxResult confirm = MessageBox.Show(this,
                "Deleting server cache files will force FiveM to redownload fresh assets when connecting to servers.\n\nAre you sure you want to clear the server cache?",
                "Clear Server Cache", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (confirm == MessageBoxResult.Yes)
            {
                string path = GetFiveMDirectory();
                bool success = FiveMConfigManager.ClearServerCache(path, out string statusMsg);
                SetStatus(statusMsg, success ? GreenBrush : RedBrush);
            }
        }

        private void SetupReShadeBtn_Click(object sender, RoutedEventArgs e)
        {
            string path = GetFiveMDirectory();
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                SetStatus("⚠ Valid FiveM installation directory could not be found.", RedBrush);
                return;
            }

            FiveMPaths resolved = FiveMDetector.ResolveFiveMPaths(path);
            string root = resolved.RootDirectory;

            bool dxgiInRoot = File.Exists(Path.Combine(root, "dxgi.dll")) || File.Exists(Path.Combine(root, "d3d11.dll"));
            bool shadersInRoot = Directory.Exists(Path.Combine(root, "reshade-shaders"));
            bool iniInRoot = File.Exists(Path.Combine(root, "ReShade.ini"));
            bool hasReShadeInRoot = dxgiInRoot || shadersInRoot || iniInRoot;

            if (hasReShadeInRoot)
            {
                bool moveSuccess = FiveMReShadeFixer.MoveReShadeFilesToPlugins(path, out int count, out string moveMsg);
                if (!moveSuccess)
                {
                    SetStatus($"⚠ {moveMsg}", RedBrush);
                    return;
                }
            }

            string ackLine = FiveMReShadeFixer.GenerateReShadeAckLine();
            bool iniSuccess = FiveMReShadeFixer.ApplyReShadeKeyToIni(path, ackLine, out string iniMsg);

            if (iniSuccess)
            {
                SetupReShadeBtn.IsEnabled = false;
                SetupReShadeBtn.Content = "ReShade Enabled";
                SetStatus("✓ ReShade is now fully enabled! Files relocated to plugins & key written to CitizenFX.ini.", GreenBrush);
            }
            else
            {
                SetStatus($"⚠ Error writing CitizenFX.ini: {iniMsg}", RedBrush);
            }
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
