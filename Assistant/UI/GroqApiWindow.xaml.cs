using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Assistant.Controllers;

namespace Assistant.UI
{
    public partial class GroqApiWindow
    {
        public GroqApiWindow()
        {
            InitializeComponent();
            RefreshList();
        }

        private void RefreshList()
        {
            AiAssistantController.ResetQuotasIfNeeded();
            KeysList.ItemsSource = null;
            KeysList.ItemsSource = AiAssistantController.Settings.ApiKeys;
        }

        private void GroqConsoleLink_Click(object sender, MouseButtonEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://console.groq.com/keys") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Failed to open Groq Console link.");
            }
        }

        private void AddKeyButton_Click(object sender, RoutedEventArgs e)
        {
            string key = NewKeyInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                MessageBox.Show("Please enter or paste a valid API key.", "Input Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!key.StartsWith("gsk_"))
            {
                if (MessageBox.Show("Groq API keys usually start with 'gsk_'. Are you sure this is a valid key?", "Validation Warning", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.No)
                {
                    return;
                }
            }

            if (AiAssistantController.Settings.ApiKeys.Any(k => k.ApiKey == key))
            {
                MessageBox.Show("This API key has already been added.", "Duplicate Key", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var keyInfo = new GroqApiKeyInfo
            {
                ApiKey = key,
                RequestCount = 0,
                LastUsedDate = DateTime.Today,
                IsActive = true,
                IsRateLimited = false
            };

            AiAssistantController.Settings.ApiKeys.Add(keyInfo);
            AiAssistantController.SaveSettings();

            NewKeyInput.Text = string.Empty;
            RefreshList();
        }

        private void DeleteKeyButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is GroqApiKeyInfo keyInfo)
            {
                if (MessageBox.Show("Are you sure you want to remove this API key?", "Confirm Deletion", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    AiAssistantController.Settings.ApiKeys.Remove(keyInfo);
                    AiAssistantController.SaveSettings();
                    RefreshList();
                }
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
