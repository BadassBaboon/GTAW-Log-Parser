using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Assistant.Controllers;
using GTAWParser.Shared;
using Serilog;

namespace Assistant.UI
{
    /// <summary>
    /// Displays live streaming chat lines received from FiveMChatCaptureService.
    /// Auto-scrolls to bottom.
    /// </summary>
    public partial class LiveTailWindow
    {
        private bool _isWatching;
        private string _previousFullLog = string.Empty;

        public LiveTailWindow()
        {
            InitializeComponent();
        }

        private void ToggleWatch_Click(object sender, RoutedEventArgs e)
        {
            if (!_isWatching)
                StartWatching();
            else
                StopWatching();
        }

        private void StartWatching()
        {
            FiveMChatCaptureService.Initialize();
            FiveMChatCaptureService.LineReceived += OnLineReceived;
            _isWatching = true;

            // Load existing chat from current session
            string existing = FiveMChatCaptureService.ReadCapturedChat(RemoveTimestamps.IsChecked == true);
            if (!string.IsNullOrWhiteSpace(existing))
            {
                _previousFullLog = existing;
                Tail.Text = existing.Replace("\n", Environment.NewLine) + Environment.NewLine;
                Tail.ScrollToEnd();
            }
            else
            {
                Tail.Text = string.Empty;
            }

            ToggleWatch.Content = "Stop";
            StatusLabel.Content = "Live Capture Active";
            StatusLabel.Foreground = System.Windows.Media.Brushes.Green;

            Log.Information("Live tail started on FiveMChatCaptureService");
        }

        private void StopWatching()
        {
            if (_isWatching)
            {
                FiveMChatCaptureService.LineReceived -= OnLineReceived;
                _isWatching = false;
            }

            ToggleWatch.Content = "Start";
            StatusLabel.Content = "Stopped";
            StatusLabel.Foreground = System.Windows.Media.Brushes.Gray;

            Log.Information("Live tail stopped");
        }

        private void OnLineReceived(string line)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (Tail.Text.Length > 200_000)
                {
                    int cut = Tail.Text.IndexOf('\n', 50_000);
                    if (cut > 0)
                        Tail.Text = Tail.Text.Substring(cut + 1);
                }

                string formattedLine = RemoveTimestamps.IsChecked == true
                    ? Regex.Replace(line, @"^\[\d{1,2}:\d{1,2}:\d{1,2}\] ", string.Empty)
                    : line;

                Tail.AppendText(formattedLine + Environment.NewLine);
                Tail.ScrollToEnd();
            }));
        }

        private void RemoveTimestamps_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(Tail.Text))
                return;

            if (RemoveTimestamps.IsChecked == true)
            {
                _previousFullLog = Tail.Text;
                Tail.Text = Regex.Replace(_previousFullLog, @"\[\d{1,2}:\d{1,2}:\d{1,2}\] ", string.Empty);
            }
            else if (!string.IsNullOrWhiteSpace(_previousFullLog))
            {
                Tail.Text = _previousFullLog;
            }
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            Tail.Text = string.Empty;
            _previousFullLog = string.Empty;
        }

        private void Tail_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (Counter == null)
                return;

            if (string.IsNullOrWhiteSpace(Tail.Text))
            {
                Counter.Text = "0 characters and 0 lines";
                return;
            }

            Counter.Text = $"{Tail.Text.Length} characters and {Tail.Text.Split('\n').Length} lines";
        }

        private void LiveTail_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            StopWatching();
        }
    }
}
