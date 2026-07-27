using System;
using System.Windows;
using Assistant.Controllers;

namespace Assistant.UI
{
    public partial class GenerateProfileWindow
    {
        public CustomAccentProfile? GeneratedProfile { get; private set; }

        public GenerateProfileWindow()
        {
            InitializeComponent();
        }

        private async void GenerateBtn_Click(object sender, RoutedEventArgs e)
        {
            string backstory = BackstoryInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(backstory))
            {
                MessageBox.Show(this, "Please describe your character's backstory first.", "Input Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            GenerateBtn.IsEnabled = false;
            StatusLabel.Visibility = Visibility.Visible;

            try
            {
                var profile = await AiAssistantController.GenerateProfileFromBackstoryAsync(backstory);
                GeneratedProfile = profile;

                ResultNameInput.Text = profile.TargetAccent;
                ResultDirectivesInput.Text = profile.CustomDirectives;

                ApplyBtn.IsEnabled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Failed to generate profile: {ex.Message}", "Generation Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                GenerateBtn.IsEnabled = true;
                StatusLabel.Visibility = Visibility.Collapsed;
            }
        }

        private void ApplyBtn_Click(object sender, RoutedEventArgs e)
        {
            string name = ResultNameInput.Text.Trim();
            string directives = ResultDirectivesInput.Text.Trim();

            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show(this, "Profile name cannot be empty.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            GeneratedProfile = new CustomAccentProfile
            {
                TargetAccent = name,
                CustomDirectives = directives
            };

            DialogResult = true;
            Close();
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
