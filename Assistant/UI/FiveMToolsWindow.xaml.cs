using System;
using System.Globalization;
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

                // 3. Load First-Person Driving FOV
                float currentFov = FiveMConfigManager.GetVehicleFirstPersonFov();
                if (currentFov < 0)
                {
                    FovPresetComboBox.SelectedIndex = 2; // -1 (Game Default)
                    CustomFovNumericUpDown.Visibility = Visibility.Collapsed;
                }
                else
                {
                    int fovInt = (int)Math.Round(currentFov);
                    if (fovInt == 60)
                    {
                        FovPresetComboBox.SelectedIndex = 0; // 60° (Recommended)
                        CustomFovNumericUpDown.Visibility = Visibility.Collapsed;
                        CustomFovNumericUpDown.Value = 60;
                    }
                    else if (fovInt == 0)
                    {
                        FovPresetComboBox.SelectedIndex = 1; // 0° (FiveM Default)
                        CustomFovNumericUpDown.Visibility = Visibility.Collapsed;
                        CustomFovNumericUpDown.Value = 0;
                    }
                    else
                    {
                        FovPresetComboBox.SelectedIndex = 3; // Custom...
                        CustomFovNumericUpDown.Value = Math.Clamp(fovInt, 0, 130);
                        CustomFovNumericUpDown.Visibility = Visibility.Visible;
                    }
                }
            }

            _isInitializing = false;

            // 4. Check ReShade Status
            CheckReShadeStatus();
        }

        private void CheckReShadeStatus()
        {
            string path = GetFiveMDirectory();
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                Step1Text.Text = "Unknown";
                Step1Text.Foreground = RedBrush;
                Step2Text.Text = "Unknown";
                Step2Text.Foreground = RedBrush;

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

            // Update Step 1 Text
            if (hasReShadeInPlugins)
            {
                Step1Text.Text = "✓ Relocated to Plugins";
                Step1Text.Foreground = GreenBrush;
            }
            else if (hasReShadeInRoot)
            {
                Step1Text.Text = "In FiveM Root (Move Needed)";
                Step1Text.Foreground = YellowBrush;
            }
            else
            {
                Step1Text.Text = "Install ReShade to FiveM.exe first";
                Step1Text.Foreground = RedBrush;
            }

            // Update Step 2 Text
            if (hasIniKey)
            {
                Step2Text.Text = "✓ Key Configured";
                Step2Text.Foreground = GreenBrush;
            }
            else if (hasReShadeInRoot || hasReShadeInPlugins)
            {
                Step2Text.Text = "Pending Key Injection";
                Step2Text.Foreground = YellowBrush;
            }
            else
            {
                Step2Text.Text = "Not Configured";
                Step2Text.Foreground = RedBrush;
            }

            // Update Action Button
            if (hasReShadeInPlugins && hasIniKey)
            {
                SetupReShadeBtn.IsEnabled = false;
                SetupReShadeBtn.Content = "ReShade Enabled";
            }
            else if (hasReShadeInRoot || hasReShadeInPlugins)
            {
                SetupReShadeBtn.IsEnabled = true;
                SetupReShadeBtn.Content = "Setup & Enable ReShade";
            }
            else
            {
                SetupReShadeBtn.IsEnabled = false;
                SetupReShadeBtn.Content = "Setup & Enable ReShade";
            }
        }

        private bool ValidateFiveMPath(out string path)
        {
            path = GetFiveMDirectory();
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                MessageBox.Show(this, "FiveM installation directory could not be found.", "FiveM Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            return true;
        }

        private void UpdateChannelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;

            if (UpdateChannelComboBox.SelectedItem is ComboBoxItem item && item.Content != null)
            {
                string channel = item.Content.ToString()!;
                if (ValidateFiveMPath(out string path))
                {
                    FiveMConfigManager.SetUpdateChannel(path, channel, out _);
                }
            }
        }

        private void BrowseGtaVPathBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateFiveMPath(out string path)) return;

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
                    }
                    else
                    {
                        MessageBox.Show(this, statusMsg, "Invalid GTA V Folder", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
        }

        private void FovPresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;

            if (FovPresetComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tagStr)
            {
                if (tagStr == "custom")
                {
                    CustomFovNumericUpDown.Visibility = Visibility.Visible;
                    if (CustomFovNumericUpDown.Value == null || CustomFovNumericUpDown.Value < 0)
                        CustomFovNumericUpDown.Value = 60;

                    float customVal = (float)(CustomFovNumericUpDown.Value ?? 60.0);
                    customVal = Math.Clamp(customVal, 0.0f, 130.0f);
                    FiveMConfigManager.SetVehicleFirstPersonFov(customVal, out _);
                }
                else
                {
                    CustomFovNumericUpDown.Visibility = Visibility.Collapsed;
                    if (tagStr == "-1")
                    {
                        FiveMConfigManager.SetVehicleFirstPersonFov(-1.0f, out _);
                    }
                    else if (float.TryParse(tagStr, CultureInfo.InvariantCulture, out float presetFov))
                    {
                        CustomFovNumericUpDown.Value = presetFov;
                        FiveMConfigManager.SetVehicleFirstPersonFov(presetFov, out _);
                    }
                }
            }
        }

        private void CustomFovNumericUpDown_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double?> e)
        {
            if (_isInitializing) return;

            if (FovPresetComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tagStr && tagStr == "custom")
            {
                if (CustomFovNumericUpDown.Value.HasValue)
                {
                    float customVal = (float)Math.Clamp(CustomFovNumericUpDown.Value.Value, 0.0, 130.0);
                    FiveMConfigManager.SetVehicleFirstPersonFov(customVal, out _);
                }
            }
        }

        private void ClearCitizenBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateFiveMPath(out string path)) return;

            MessageBoxResult confirm = MessageBox.Show(this,
                "Deleting the citizen folder will force FiveM to redownload clean system files on launch.\n\nAre you sure you want to clear the citizen folder?",
                "Clear Citizen Folder", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (confirm == MessageBoxResult.Yes)
            {
                bool success = FiveMConfigManager.ClearCitizenFolder(path, out string statusMsg);
                MessageBox.Show(this, statusMsg, "Clear Citizen", MessageBoxButton.OK, success ? MessageBoxImage.Information : MessageBoxImage.Error);
            }
        }

        private void ClearServerCacheBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateFiveMPath(out string path)) return;

            MessageBoxResult confirm = MessageBox.Show(this,
                "Deleting server cache files will force FiveM to redownload fresh assets when connecting to servers.\n\nAre you sure you want to clear the server cache?",
                "Clear Server Cache", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (confirm == MessageBoxResult.Yes)
            {
                bool success = FiveMConfigManager.ClearServerCache(path, out string statusMsg);
                MessageBox.Show(this, statusMsg, "Clear Server Cache", MessageBoxButton.OK, success ? MessageBoxImage.Information : MessageBoxImage.Error);
            }
        }

        private void SetupReShadeBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateFiveMPath(out string path)) return;

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
                    MessageBox.Show(this, moveMsg, "ReShade Setup Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            string ackLine = FiveMReShadeFixer.GenerateReShadeAckLine();
            bool iniSuccess = FiveMReShadeFixer.ApplyReShadeKeyToIni(path, ackLine, out string iniMsg);

            if (iniSuccess)
            {
                CheckReShadeStatus();
                MessageBox.Show(this, "✓ ReShade is now fully enabled on your FiveM installation!\n\nFiles relocated to plugins & hardware key written to CitizenFX.ini.", "ReShade Setup Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(this, $"Error writing CitizenFX.ini: {iniMsg}", "CitizenFX.ini Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
