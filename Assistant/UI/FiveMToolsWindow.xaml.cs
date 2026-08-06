using System;
using System.Windows;
using GTAWParser.Shared;
using MahApps.Metro.Controls;
using MessageBox = System.Windows.MessageBox;

namespace Assistant.UI
{
    public partial class FiveMToolsWindow : MetroWindow
    {
        public FiveMToolsWindow()
        {
            InitializeComponent();
            LogInitialPath();
        }

        private string GetFiveMDirectory()
        {
            string currentPath = Properties.Settings.Default.DirectoryPath;
            if (string.IsNullOrWhiteSpace(currentPath) || !System.IO.Directory.Exists(currentPath))
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

        private void LogInitialPath()
        {
            string path = GetFiveMDirectory();
            AppendLog($"FiveM Directory: {path}");
        }

        private void AppendLog(string message)
        {
            StatusLogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\n");
            StatusLogTextBox.ScrollToEnd();
        }

        private void Step1MoveFilesBtn_Click(object sender, RoutedEventArgs e)
        {
            string path = GetFiveMDirectory();
            if (string.IsNullOrWhiteSpace(path) || !System.IO.Directory.Exists(path))
            {
                MessageBox.Show(this, "Valid FiveM installation directory could not be found.", "FiveM Tools", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            AppendLog("Starting Step 1: Scanning FiveM root for ReShade files...");
            bool success = FiveMReShadeFixer.MoveReShadeFilesToPlugins(path, out int count, out string msg);
            AppendLog(msg);

            if (success)
            {
                MessageBox.Show(this, $"{msg}\n\nNext Step: Launch FiveM once until you reach the main menu, close FiveM, then click Step 2: Apply Bypass.", "Step 1 Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(this, msg, "Step 1 Notice", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void Step2ScanLogBtn_Click(object sender, RoutedEventArgs e)
        {
            string path = GetFiveMDirectory();
            if (string.IsNullOrWhiteSpace(path) || !System.IO.Directory.Exists(path))
            {
                MessageBox.Show(this, "Valid FiveM installation directory could not be found.", "FiveM Tools", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            AppendLog("Starting Step 2: Scanning newest FiveM log for ReShade bypass key...");
            bool logFound = FiveMReShadeFixer.ScanLogForReShadeBypass(path, out string bypassLine, out string logFileName, out string statusMsg);
            AppendLog(statusMsg);

            if (logFound && !string.IsNullOrEmpty(bypassLine))
            {
                AppendLog($"Found bypass string in {logFileName}: {bypassLine}");
                AppendLog("Applying bypass string to CitizenFX.ini...");

                bool iniSuccess = FiveMReShadeFixer.ApplyReShadeBypassToIni(path, bypassLine, out string iniMsg);
                AppendLog(iniMsg);

                if (iniSuccess)
                {
                    MessageBox.Show(this, "ReShade 5+ crash warning bypass successfully applied to CitizenFX.ini!\n\nYou can now launch FiveM with ReShade enabled.", "Step 2 Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show(this, iniMsg, "Step 2 Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                MessageBox.Show(this, statusMsg, "Step 2 Notice", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
