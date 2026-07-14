using System.Windows;
using System.Windows.Controls;
using Assistant.Controllers;
using System.Collections.ObjectModel;
using System.Linq;

namespace Assistant.UI
{
    public partial class CustomAccentProfilesWindow
    {
        private ObservableCollection<CustomAccentProfile> _profiles = new ObservableCollection<CustomAccentProfile>();
        private CustomAccentProfile? _selectedProfile;

        public CustomAccentProfilesWindow()
        {
            InitializeComponent();
            LoadProfiles();
        }

        private void LoadProfiles()
        {
            _profiles.Clear();
            if (AiAssistantController.Settings.CustomProfiles != null)
            {
                foreach (var profile in AiAssistantController.Settings.CustomProfiles)
                {
                    _profiles.Add(profile);
                }
            }
            ProfilesList.ItemsSource = _profiles;
        }

        private void ProfilesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedProfile = ProfilesList.SelectedItem as CustomAccentProfile;
            if (_selectedProfile != null)
            {
                DetailsGrid.IsEnabled = true;
                ProfileNameInput.Text = _selectedProfile.TargetAccent;
                ProfileDirectivesInput.Text = _selectedProfile.CustomDirectives;
            }
            else
            {
                DetailsGrid.IsEnabled = false;
                ProfileNameInput.Text = string.Empty;
                ProfileDirectivesInput.Text = string.Empty;
            }
        }

        private void NewProfileBtn_Click(object sender, RoutedEventArgs e)
        {
            var newProfile = new CustomAccentProfile
            {
                TargetAccent = "New Accent Profile",
                CustomDirectives = "Guideline directives..."
            };

            if (AiAssistantController.Settings.CustomProfiles == null)
            {
                AiAssistantController.Settings.CustomProfiles = new System.Collections.Generic.List<CustomAccentProfile>();
            }

            AiAssistantController.Settings.CustomProfiles.Add(newProfile);
            AiAssistantController.SaveSettings();

            _profiles.Add(newProfile);
            ProfilesList.SelectedItem = newProfile;
        }

        private void DeleteProfileBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedProfile == null) return;

            var result = MessageBox.Show(this, 
                $"Are you sure you want to delete the profile for '{_selectedProfile.TargetAccent}'?", 
                "Confirm Delete", 
                MessageBoxButton.YesNo, 
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                AiAssistantController.Settings.CustomProfiles.Remove(_selectedProfile);
                AiAssistantController.SaveSettings();
                _profiles.Remove(_selectedProfile);
                ProfilesList.SelectedItem = null;
            }
        }

        private void SaveProfileBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedProfile == null) return;

            string name = ProfileNameInput.Text.Trim();
            string directives = ProfileDirectivesInput.Text;

            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show(this, "Profile name cannot be empty.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _selectedProfile.TargetAccent = name;
            _selectedProfile.CustomDirectives = directives;
            AiAssistantController.SaveSettings();

            // Refresh ListBox member display
            ProfilesList.Items.Refresh();

            MessageBox.Show(this, "Profile settings saved successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
