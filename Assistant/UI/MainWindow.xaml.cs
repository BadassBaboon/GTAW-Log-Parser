using System;
using Octokit;
using System.IO;
using System.Windows;
using System.Threading;
using System.Diagnostics;
using System.Windows.Input;
using System.Globalization;
using Assistant.Controllers;
using Assistant.Localization;
using Assistant.Utilities;
using GTAWParser.Shared;
using Serilog;
using System.Windows.Media;
using System.Windows.Controls;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Win32;

namespace Assistant.UI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        private System.Windows.Forms.NotifyIcon _trayIcon = null!;

        private GitHubClient _client;
        private bool _isUpdateCheckRunning;
        private bool _isUpdateCheckManual;
        private readonly bool _isLoading;

        private static bool isRestarting;

        /// <summary>
        /// Initializes the main window
        /// </summary>
        /// <param name="startMinimized"></param>
        public MainWindow(bool startMinimized)
        {
            _isLoading = true;
            _client = new GitHubClient(new ProductHeaderValue(AppController.ProductHeader));
            _client.SetRequestTimeout(new TimeSpan(0, 0, 0, Properties.Settings.Default.UpdateCheckTimeout));
            StartupController.InitializeShortcut();

            InitializeComponent();
            InitializeTrayIcon();

            if (startMinimized)
                _trayIcon.Visible = true;

            // Also checks for the RAGEMP directory on the first start
            LoadSettings();
            InitializeAiAssistant();

            SetupServerList();
            BackupController.Initialize();
            _isLoading = false;
        }

        /// <summary>
        /// Adds menu options under "Server" on the menu
        /// strip for each Language in LocalizationController
        /// </summary>
        private void SetupServerList()
        {
            string currentLanguage = LocalizationController.GetLanguageFromCode(LocalizationController.GetLanguage());
            for (int i = 0; i < ((LocalizationController.Language[])Enum.GetValues(typeof(LocalizationController.Language))).Length; ++i)
            {
                LocalizationController.Language language = (LocalizationController.Language)i;

                MenuItem menuItem = new MenuItem
                {
                    Header = language.ToString()
                };

                LanguageToolStripMenuItem.Items.Add(menuItem);
                menuItem.Click += (s, e) =>
                {
                    if (menuItem.IsChecked)
                        return;

                    CultureInfo cultureInfo = new CultureInfo(LocalizationController.GetCodeFromLanguage(language));
                    if (MessageBox.Show(Strings.ResourceManager.GetString("SwitchServer", cultureInfo),
                        Strings.ResourceManager.GetString("Restart", cultureInfo), MessageBoxButton.YesNo,
                        MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                    LocalizationController.SetLanguage(language, code =>
                    {
                        Properties.Settings.Default.LanguageCode = code;
                        Properties.Settings.Default.Save();
                    });

                    isRestarting = true;

                    Process.Start(new ProcessStartInfo
                    {
                        FileName = AppController.ExecutablePath,
                        Arguments = $"{AppController.ParameterPrefix}restart",
                        UseShellExecute = true
                    });

                    System.Windows.Application.Current.Shutdown();
                };

                if (currentLanguage == language.ToString())
                    menuItem.IsChecked = true;
            }
        }

        /// <summary>
        /// Saves the main settings
        /// </summary>
        private void SaveSettings()
        {
            Properties.Settings.Default.DirectoryPath = DirectoryPath.Text;
            Properties.Settings.Default.RemoveTimestamps = RemoveTimestamps.IsChecked == true;
            Properties.Settings.Default.CheckForUpdatesAutomatically = CheckForUpdatesOnStartup.IsChecked == true;

            Properties.Settings.Default.Save();
            AppController.InitializeServerIp();
        }

        /// <summary>
        /// Loads the main settings
        /// </summary>
        private void LoadSettings()
        {
            OpenForums.Visibility = Properties.Settings.Default.DisableForumsButton ? Visibility.Collapsed : Visibility.Visible;
            OpenFacebrowser.Visibility = Properties.Settings.Default.DisableFacebrowserButton ? Visibility.Collapsed : Visibility.Visible;
            OpenUCP.Visibility = Properties.Settings.Default.DisableUCPButton ? Visibility.Collapsed : Visibility.Visible;
            OpenGithubReleases.Visibility = Properties.Settings.Default.DisableReleasesButton ? Visibility.Collapsed : Visibility.Visible;
            OpenGithubProject.Visibility = Properties.Settings.Default.DisableProjectButton ? Visibility.Collapsed : Visibility.Visible;
            UpdateCheckProgress.Foreground = StyleController.DarkMode ? System.Windows.Media.Brushes.White : System.Windows.Media.Brushes.Black;

            Version.Text = string.Format(Strings.VersionInfo, AppController.Version, AppController.IsBetaVersion ? Strings.BetaShort : string.Empty);
            StatusLabel.Content = string.Format(Strings.BackupStatus, Properties.Settings.Default.BackupChatLogAutomatically ? Strings.Enabled : Strings.Disabled);
            Counter.Text = string.Format(Strings.CharacterCounter, 0, 0);

            RemoveTimestamps.IsChecked = Properties.Settings.Default.RemoveTimestamps;
            CheckForUpdatesOnStartup.IsChecked = Properties.Settings.Default.CheckForUpdatesAutomatically;

            if (Properties.Settings.Default.FirstStart)
            {
                Properties.Settings.Default.FirstStart = false;
                Properties.Settings.Default.Save();

                LookForMainDirectory();
                SaveSettings();
            }
            else
                DirectoryPath.Text = Properties.Settings.Default.DirectoryPath;
        }

        /// <summary>
        /// Looks for the main RAGEMP directory
        /// path on the first start
        /// </summary>
        private void LookForMainDirectory()
        {
            try
            {
                var keyValue = Registry.GetValue(@"HKEY_CURRENT_USER\Software\RAGE-MP", "rage_path", null);
                if (keyValue != null)
                {
                    DirectoryPath.Text = keyValue + @"\";
                    MessageBox.Show(string.Format(Strings.DirectoryFinder, DirectoryPath.Text), Strings.DirectoryFinderTitle, MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    throw new IOException();
                }
            }
            catch
            {
                MessageBox.Show(Strings.DirectoryFinderNotFound, Strings.DirectoryFinderTitle, MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        /// <summary>
        /// Saves the settings when the
        /// value of the text box changes
        /// and disables automatic backup
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void DirectoryPath_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isLoading)
                return;

            if (Properties.Settings.Default.BackupChatLogAutomatically)
            {
                BackupSettingsWindow.ResetSettings();

                StatusLabel.Content = string.Format(Strings.BackupStatus, Strings.Disabled);
                MessageBox.Show(Strings.BackupTurnedOff, Strings.Information, MessageBoxButton.OK, MessageBoxImage.Information);
            }

            SaveSettings();
        }

        /// <summary>
        /// Opens the directory picker
        /// when the text box is clicked on
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void DirectoryPath_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(DirectoryPath.Text))
                Browse_Click(this, null!);
        }

        /// <summary>
        /// Displays a directory picker until
        /// a non-root directory is selected
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.Forms.FolderBrowserDialog directoryBrowserDialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = @"RAGEMP Directory Path",
                RootFolder = Environment.SpecialFolder.MyComputer,
                SelectedPath = string.IsNullOrWhiteSpace(DirectoryPath.Text) || !Directory.Exists(DirectoryPath.Text) ? (Path.GetPathRoot(Environment.SystemDirectory) ?? string.Empty) : DirectoryPath.Text,
                ShowNewFolderButton = false
            };

            bool validLocation = false;
            while (!validLocation)
            {
                if (directoryBrowserDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    if (directoryBrowserDialog.SelectedPath[directoryBrowserDialog.SelectedPath.Length - 1] != '\\')
                    {
                        DirectoryPath.Text = directoryBrowserDialog.SelectedPath + "\\";
                        validLocation = true;
                    }
                    else
                        MessageBox.Show(Strings.BadDirectoryPath, Strings.Error, MessageBoxButton.OK, MessageBoxImage.Error);
                }
                else
                    validLocation = true;
            }
        }

        /// <summary>
        /// Parses the current chat log and sets
        /// the text of the main text box to it
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Parse_Click(object sender, RoutedEventArgs e)
        {
            // The paths may have changed since the program has
            // started, we need to initialize the locations again
            AppController.InitializeServerIp();

            if (string.IsNullOrWhiteSpace(DirectoryPath.Text) || !Directory.Exists(DirectoryPath.Text + "client_resources\\"))
            {
                MessageBox.Show(Strings.InvalidDirectoryPath, Strings.Error, MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!File.Exists(DirectoryPath.Text + AppController.LogLocation))
            {
                MessageBox.Show(Strings.NoChatLog, Strings.Error, MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            Parsed.Text = AppController.ParseChatLog(DirectoryPath.Text, RemoveTimestamps.IsChecked == true, true);
        }

        /// <summary>
        /// Displays a save file dialog to save the
        /// contents of the main text box to the disk
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Parsed_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (Counter == null)
                return;

            if (string.IsNullOrWhiteSpace(Parsed.Text))
            {
                Counter.Text = string.Format(Strings.CharacterCounter, 0, 0);
                return;
            }

            Counter.Text = string.Format(Strings.CharacterCounter, Parsed.Text.Length, Parsed.Text.Split('\n').Length);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SaveParsed_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(Parsed.Text))
                {
                    if (!Properties.Settings.Default.DisableErrorPopups)
                        MessageBox.Show(Strings.NothingParsed, Strings.Error, MessageBoxButton.OK, MessageBoxImage.Error);
                    
                    return;
                }

                Microsoft.Win32.SaveFileDialog dialog = new Microsoft.Win32.SaveFileDialog
                {
                    FileName = "chatlog.txt",
                    Filter = "Text File | *.txt"
                };

                if (dialog.ShowDialog() != true) return;
                using (StreamWriter sw = new StreamWriter(dialog.OpenFile()))
                {
                    sw.Write(Parsed.Text.Replace("\n", Environment.NewLine));
                }
            }
            catch
            {
                MessageBox.Show(Strings.SaveError, Strings.Error, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Copies the contents of the
        /// main text box to the clipboard
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CopyParsedToClipboard_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(Parsed.Text) && !Properties.Settings.Default.DisableErrorPopups)
                MessageBox.Show(Strings.NothingParsed, Strings.Error, MessageBoxButton.OK, MessageBoxImage.Error);
            else
                Clipboard.SetText(Parsed.Text.Replace("\n", Environment.NewLine));
        }

        /// <summary>
        /// Toggles the "Check For Updates On Startup" option
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CheckForUpdatesOnStartup_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (CheckForUpdatesOnStartup.IsChecked == true)
                TryCheckingForUpdates();
        }

        /// <summary>
        /// Removes the timestamps from the parsed chat log
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void RemoveTimestamps_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(Parsed.Text) || string.IsNullOrWhiteSpace(DirectoryPath.Text) || !Directory.Exists(DirectoryPath.Text + "client_resources\\") || !File.Exists(DirectoryPath.Text+ AppController.LogLocation))
                return;

            if (RemoveTimestamps.IsChecked == true)
            {
                AppController.PreviousLog = Parsed.Text;
                Parsed.Text = Regex.Replace(AppController.PreviousLog, @"\[\d{1,2}:\d{1,2}:\d{1,2}\] ", string.Empty);
            }
            else if (!string.IsNullOrWhiteSpace(AppController.PreviousLog))
                Parsed.Text = AppController.PreviousLog;
        }

        /// <summary>
        /// Toggles the controls on the main window
        /// </summary>
        /// <param name="enable"></param>
        private void ToggleControls(bool enable = false)
        {
            Dispatcher?.Invoke(() =>
            {
                IsMinButtonEnabled = enable;
                //IsCloseButtonEnabled = enable;

                Parse.IsEnabled = enable;
                SaveParsed.IsEnabled = enable;
                CopyParsedToClipboard.IsEnabled = enable;
                DirectoryPath.IsEnabled = enable;
                Browse.IsEnabled = enable;
                Parsed.IsEnabled = enable;
                CheckForUpdatesOnStartup.IsEnabled = enable;
                RemoveTimestamps.IsEnabled = enable;
                Logo.IsEnabled = enable;

                foreach (MenuItem item in MenuStrip.Items)
                {
                    item.IsEnabled = enable;
                }

                OpenProgramSettings.IsEnabled = enable;
                OpenGithubProject.IsEnabled = enable;
                OpenGithubReleases.IsEnabled = enable;
                OpenUCP.IsEnabled = enable;
                OpenFacebrowser.IsEnabled = enable;
                OpenForums.IsEnabled = enable;
            });
        }

        /// <summary>
        /// Tries checking for updates
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CheckForUpdatesToolStripMenuItem_Click(object sender, RoutedEventArgs e)
        {
            TryCheckingForUpdates(true);
        }

        /// <summary>
        /// Disables the controls on the main window
        /// and checks for updates
        /// </summary>
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);
        private void TryCheckingForUpdates(bool manual = false)
        {
            if (!_isUpdateCheckRunning)
            {
                _isUpdateCheckRunning = true;
                _resetEvent.Reset();

                UpdateCheckProgress.Visibility = Visibility.Visible;
                UpdateCheckProgress.IsActive = true;

                if (manual)
                    ToggleControls();

                _isUpdateCheckManual = manual;
                ThreadPool.QueueUserWorkItem(_ => CheckForUpdates(ref _isUpdateCheckManual));
                ThreadPool.QueueUserWorkItem(_ => FinishUpdateCheck());
            }
            else if (manual && !_isUpdateCheckManual)
            {
                _isUpdateCheckManual = true;
                ToggleControls();
            }
        }

        /// <summary>
        /// Enables the controls on the main window
        /// and disables the progress ring
        /// </summary>
        private void FinishUpdateCheck()
        {
            _resetEvent.WaitOne();
            
            ToggleControls(true);
            StopUpdateIndicator();
            
            _isUpdateCheckRunning = false;
        }

        /// <summary>
        /// Disables the progress ring
        /// </summary>
        private void StopUpdateIndicator()
        {
            Dispatcher?.Invoke(() =>
            {
                UpdateCheckProgress.IsActive = false;
                UpdateCheckProgress.Visibility = Visibility.Collapsed;
            });
        }

        /// <summary>
        /// Displays a message box on the main UI thread; if the user clicks
        /// Yes we open the releases page in their browser.
        /// </summary>
        private void DisplayUpdateMessage(string text, string title, MessageBoxButton buttons, MessageBoxImage image)
        {
            ToggleControls(true);
            StopUpdateIndicator();

            Dispatcher?.Invoke(() =>
            {
                if (MessageBox.Show(text, title, buttons, image) == MessageBoxResult.Yes)
                    OpenUrl(Strings.ReleasesLink);
            });
        }

        /// <summary>
        /// Offers an in-app update for the given release. Yes = download and
        /// swap automatically; No = open the releases page; Cancel = nothing.
        /// </summary>
        private void OfferAutoUpdate(string text, string title, Release release)
        {
            ToggleControls(true);
            StopUpdateIndicator();

            Dispatcher?.Invoke(async () =>
            {
                MessageBoxResult choice = MessageBox.Show(
                    text + "\n\nYes = update now (download + restart)\nNo = open releases page in browser",
                    title,
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Information);

                if (choice == MessageBoxResult.Yes)
                {
                    ToggleControls();
                    UpdateCheckProgress.Visibility = Visibility.Visible;
                    UpdateCheckProgress.IsActive = true;

                    bool ok = await AutoUpdater.TryUpdateAsync(release).ConfigureAwait(true);
                    if (ok)
                    {
                        System.Windows.Application.Current.Shutdown();
                        return;
                    }

                    MessageBox.Show("Auto-update failed. Opening the releases page instead.",
                        title, MessageBoxButton.OK, MessageBoxImage.Warning);
                    OpenUrl(Strings.ReleasesLink);
                }
                else if (choice == MessageBoxResult.No)
                {
                    OpenUrl(Strings.ReleasesLink);
                }
            });
        }

        /// <summary>
        /// Checks for updates
        /// </summary>
        /// <param name="manual"></param>
        private void CheckForUpdates(ref bool manual)
        {
            try
            {
                string installedVersion = AppController.Version;
                IReadOnlyList<Release> releases = _client.Repository.Release.GetAll(AppController.GitHubOwner, AppController.GitHubRepo).Result;

                string newVersion = string.Empty;
                bool isNewVersionBeta = false;
                Release? matchedRelease = null;

                // Prereleases are a go
                if (!Properties.Settings.Default.IgnoreBetaVersions)
                {
                    matchedRelease = releases[0];
                    newVersion = matchedRelease.TagName;
                    isNewVersionBeta = matchedRelease.Prerelease;
                }
                else
                {
                    // If the user does not want to
                    // look for prereleases during
                    // the update check, ignore them
                    foreach (Release release in releases)
                    {
                        if (release.Prerelease)
                            continue;

                        matchedRelease = release;
                        newVersion = release.TagName;
                        isNewVersionBeta = release.Prerelease;
                        break;
                    }
                }

                if (matchedRelease != null && (AppController.IsBetaVersion && !isNewVersionBeta && string.CompareOrdinal(installedVersion, newVersion) == 0 || string.CompareOrdinal(installedVersion, newVersion) < 0))
                { // Update available
                    if (Visibility != Visibility.Visible)
                        ResumeTrayStripMenuItem_Click(this, EventArgs.Empty);

                    OfferAutoUpdate(
                        string.Format(Strings.UpdateAvailable,
                            installedVersion + (AppController.IsBetaVersion ? " Beta" : string.Empty),
                            newVersion + (isNewVersionBeta ? " Beta" : string.Empty)),
                        Strings.UpdateAvailableTitle,
                        matchedRelease);
                }
                else if (manual) // Latest version
                    DisplayUpdateMessage(string.Format(Strings.RunningLatest, installedVersion + (AppController.IsBetaVersion ? " Beta" : string.Empty)), Strings.Information, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "CheckForUpdates failed");
                if (manual)
                    DisplayUpdateMessage(string.Format(Strings.NoInternet, AppController.Version + (AppController.IsBetaVersion ? " Beta" : string.Empty)), Strings.Error, MessageBoxButton.OK, MessageBoxImage.Error);
            }

            _resetEvent.Set();
        }

        /// <summary>
        /// Opens the backup settings window
        /// </summary>
        private static BackupSettingsWindow? backupSettings;
        private void BackupSettingsToolStripMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(DirectoryPath.Text) || !Directory.Exists(DirectoryPath.Text + "client_resources\\"))
            {
                MessageBox.Show(Strings.InvalidDirectoryPathBackup, Strings.Error, MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (Properties.Settings.Default.BackupChatLogAutomatically)
            {
                if (!Properties.Settings.Default.DisableWarningPopups && MessageBox.Show(Strings.BackupWillBeOff, Strings.Warning, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.No)
                    return;

                StatusLabel.Content = string.Format(Strings.BackupStatus, Strings.Disabled);
            }
            else
                if (!Properties.Settings.Default.DisableInformationPopups)
                    MessageBox.Show(Strings.SettingsAfterClose, Strings.Information, MessageBoxButton.OK, MessageBoxImage.Information);

            BackupController.AbortAll();
            SaveSettings();

            if (backupSettings == null)
            {
                backupSettings = new BackupSettingsWindow(this);
                backupSettings.IsVisibleChanged += (s, args) =>
                {
                    if ((bool)args.NewValue) return;
                    BackupController.Initialize();
                    StatusLabel.Content = string.Format(Strings.BackupStatus,
                        Properties.Settings.Default.BackupChatLogAutomatically ? Strings.Enabled : Strings.Disabled);
                };
                backupSettings.Closed += (s, args) =>
                {
                    backupSettings = null;
                };
            }

            backupSettings.ShowDialog();
        }

        /// <summary>
        /// Opens the chat log filter window
        /// </summary>
        private static ChatLogFilterWindow? chatLogFilter;
        private void FilterChatLogToolStripMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(DirectoryPath.Text) || !Directory.Exists(DirectoryPath.Text + "client_resources\\"))
            {
                MessageBox.Show(Strings.InvalidDirectoryPathFilter, Strings.Error, MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            SaveSettings();

            if (chatLogFilter == null)
            {
                chatLogFilter = new ChatLogFilterWindow(this);
                chatLogFilter.Closed += (s, args) =>
                {
                    chatLogFilter = null;
                };
            }

            chatLogFilter.ShowDialog();
        }

        /// <summary>
        /// Opens the live tail window
        /// </summary>
        private static LiveTailWindow? liveTail;
        private void LiveTailToolStripMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (liveTail == null)
            {
                liveTail = new LiveTailWindow();
                liveTail.Closed += (s, args) => liveTail = null;
            }
            liveTail.Show();
            liveTail.Activate();
        }

        /// <summary>
        /// Displays some information about the application
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void AboutToolStripMenuItem_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(string.Format(Strings.About, AppController.Version, AppController.IsBetaVersion ? Strings.Beta : string.Empty, AppController.ResourceDirectory), Strings.Information, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// Quits the application from the tool strip
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ExitToolStripMenuItem_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.Application.Current.Shutdown();
        }

        /// <summary>
        /// Asks the user if they are sure they want to exit
        /// if automatic backup is enabled.
        /// Saves the settings before the main window closes
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Main_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!isRestarting)
            {
                if (Properties.Settings.Default.BackupChatLogAutomatically && _trayIcon.Visible == false)
                {
                    MessageBoxResult result = MessageBoxResult.Yes;
                    if (!Properties.Settings.Default.AlwaysCloseToTray)
                        result = MessageBox.Show(Strings.MinimizeInsteadOfClose, Strings.Warning, MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);

                    // ReSharper disable once ConvertIfStatementToSwitchStatement
                    if (result == MessageBoxResult.Yes)
                    {
                        e.Cancel = true;

                        Hide();
                        _trayIcon.Visible = true;

                        return;
                    }

                    if (result == MessageBoxResult.Cancel)
                    {
                        e.Cancel = true;
                        return;
                    }
                }
            }

            StyleController.StopWatchers();
            BackupController.Quitting = true;
            SaveSettings();
            KeyboardHookManager.Stop();

            System.Windows.Application.Current.Shutdown();
        }

        /// <summary>
        /// Resumes and shows the main window by double clicking the tray icon
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void TrayIcon_MouseDoubleClick(object? sender, System.Windows.Forms.MouseEventArgs e)
        {
            ResumeTrayStripMenuItem_Click(sender, EventArgs.Empty);
        }

        /// <summary>
        /// Resumes and shows the main window from the tray menu
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ResumeTrayStripMenuItem_Click(object? sender, EventArgs e)
        {
            if (isRestarting)
                return;

            Show();
            _trayIcon.Visible = false;

            if (CheckForUpdatesOnStartup.IsChecked == true)
                TryCheckingForUpdates();
        }

        /// <summary>
        /// Quits the application from the tray
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ExitTrayToolStripMenuItem_Click(object? sender, EventArgs e)
        {
            BackupController.Quitting = true;
            StyleController.StopWatchers();
            KeyboardHookManager.Stop();

            _trayIcon.Visible = false;
            isRestarting = true;
            System.Windows.Application.Current.Shutdown();
        }

        /// <summary>
        /// Initializes the tray icon
        /// </summary>
        private void InitializeTrayIcon()
        {
            _trayIcon = new System.Windows.Forms.NotifyIcon
            {
                Visible = false,
                Icon = Properties.Resources.AppIcon,
                Text= @"GTA World Chat Log Assistant"
            };

            _trayIcon.MouseDoubleClick += TrayIcon_MouseDoubleClick;

            _trayIcon.ContextMenuStrip = new System.Windows.Forms.ContextMenuStrip();
            _trayIcon.ContextMenuStrip.Items.Add(@"Open", null, ResumeTrayStripMenuItem_Click);
            _trayIcon.ContextMenuStrip.Items.Add(@"Exit", null, ExitTrayToolStripMenuItem_Click);
        }

        /// <summary>
        /// Opens the program settings window
        /// </summary>
        private static ProgramSettingsWindow? programSettings;
        private void OpenProgramSettings_Click(object sender, RoutedEventArgs e)
        {
            SaveSettings();

            if (programSettings == null)
            {
                programSettings = new ProgramSettingsWindow(this);
                programSettings.Closed += (s, args) =>
                {
                    _client = new GitHubClient(new ProductHeaderValue(AppController.ProductHeader));
                    _client.SetRequestTimeout(new TimeSpan(0, 0, 0, Properties.Settings.Default.UpdateCheckTimeout));

                    programSettings = null;
                };
            }

            programSettings.ShowDialog();
        }

        /// <summary>
        /// Opens the Github Project page in the default browser
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OpenGithubProject_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl(Strings.ProjectLink);
        }

        private static void OpenUrl(string url)
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }

        /// <summary>
        /// Opens the Github Releases page in the default browser
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OpenGithubReleases_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl(Strings.ReleasesLink);
        }

        /// <summary>
        /// Opens the UCP in the default browser
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OpenUCP_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl(Strings.UCPLink);
        }

        /// <summary>
        /// Opens Facebrowser in the default browser
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OpenFacebrowser_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl(Strings.FacebrowserLink);
        }

        /// <summary>
        /// Opens the Forums in the default browser
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OpenForums_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl(Strings.ForumsLink);
        }

        // ==========================================
        // AI ASSISTANT & KEYBOARD HOOK INTEGRATIONS
        // ==========================================

        private string? _recordingHotkeyMode;

        private void InitializeAiAssistant()
        {
            this.PreviewKeyDown += MainWindow_PreviewKeyDown;

            // Load settings
            AiAssistantController.LoadSettings();

            // Set up hotkey manager properties and start it
            KeyboardHookManager.BindTildeToT = AiAssistantController.Settings.BindTildeEnabled;
            KeyboardHookManager.HotkeyAccent = KeyboardHookManager.HotkeyConfig.Parse(AiAssistantController.Settings.ShortcutAccent);
            KeyboardHookManager.HotkeyTranslate = KeyboardHookManager.HotkeyConfig.Parse(AiAssistantController.Settings.ShortcutTranslate);
            KeyboardHookManager.HotkeyCorrect = KeyboardHookManager.HotkeyConfig.Parse(AiAssistantController.Settings.ShortcutCorrect);
            KeyboardHookManager.OnModeShortcutPressed = OnAiModeShortcutTriggered;
            KeyboardHookManager.Start();

            // Sync controls
            BindTilde.IsChecked = AiAssistantController.Settings.BindTildeEnabled;
            AiSoundEnabled.IsChecked = AiAssistantController.Settings.SoundEnabled;
            AiPhoneticEnabled.IsChecked = AiAssistantController.Settings.PhoneticEnabled;
            AiTemperature.Value = AiAssistantController.Settings.Temperature;
            AiTemperatureLabel.Content = AiAssistantController.Settings.Temperature.ToString("0.0");

            switch (AiAssistantController.Settings.LengthConstraint)
            {
                case "NoConstraint":
                    AiLength.SelectedIndex = 0;
                    break;
                case "Similar":
                    AiLength.SelectedIndex = 1;
                    break;
                case "Concise":
                    AiLength.SelectedIndex = 2;
                    break;
                default:
                    AiLength.SelectedIndex = 1;
                    break;
            }

            switch (AiAssistantController.Settings.Mode)
            {
                case "Accent":
                    AiMode.SelectedIndex = 0;
                    AiTargetInput.Text = AiAssistantController.Settings.TargetAccent;
                    break;
                case "Translate":
                    AiMode.SelectedIndex = 1;
                    AiTargetInput.Text = AiAssistantController.Settings.TargetLanguage;
                    break;
                case "Correct":
                    AiMode.SelectedIndex = 2;
                    AiTargetInput.Text = string.Empty;
                    AiTargetInput.IsEnabled = false;
                    AiTargetLabel.IsEnabled = false;
                    break;
            }

            AiModel.Text = AiAssistantController.Settings.ActiveModel;
            RecordAccentHotkeyBtn.Content = $"Accent Shortcut: {AiAssistantController.Settings.ShortcutAccent}";
            RecordTranslateHotkeyBtn.Content = $"Translate Shortcut: {AiAssistantController.Settings.ShortcutTranslate}";
            RecordCorrectHotkeyBtn.Content = $"Correct Shortcut: {AiAssistantController.Settings.ShortcutCorrect}";

            // Default to Log Parser view on startup
            SwitchToTab(true);
        }

        private void PlaySound(bool success)
        {
            if (!AiAssistantController.Settings.SoundEnabled)
                return;

            try
            {
                string uriString = success 
                    ? "pack://application:,,,/Resources/done.wav" 
                    : "pack://application:,,,/Resources/failed.wav";

                var resourceInfo = System.Windows.Application.GetResourceStream(new Uri(uriString));
                if (resourceInfo != null)
                {
                    using (var player = new System.Media.SoundPlayer(resourceInfo.Stream))
                    {
                        player.Play();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to play notification sound from resources.");
            }
        }

        private void OnAiModeShortcutTriggered(string mode)
        {
            Thread thread = new Thread(async () =>
            {
                try
                {
                    string oldClipboard = string.Empty;
                    Dispatcher.Invoke(() =>
                    {
                        try { oldClipboard = Clipboard.GetText(); } catch { }
                    });

                    Dispatcher.Invoke(() => { try { Clipboard.Clear(); } catch { } });

                    KeyboardHookManager.SimulateCopy();
                    Thread.Sleep(150);

                    string capturedText = string.Empty;
                    Dispatcher.Invoke(() =>
                    {
                        try { capturedText = Clipboard.GetText(); } catch { }
                    });

                    if (string.IsNullOrWhiteSpace(capturedText))
                    {
                        KeyboardHookManager.SimulateSelectAllAndCopy();
                        Thread.Sleep(150);
                        Dispatcher.Invoke(() =>
                        {
                            try { capturedText = Clipboard.GetText(); } catch { }
                        });
                    }

                    if (string.IsNullOrWhiteSpace(capturedText))
                    {
                        Dispatcher.Invoke(() =>
                        {
                            try { Clipboard.SetText(oldClipboard); } catch { }
                        });
                        PlaySound(false);
                        return;
                    }

                    // Process text via Groq using the specific mode triggered by the shortcut
                    string result = await AiAssistantController.ProcessTextAsync(capturedText, mode);

                    Dispatcher.Invoke(() =>
                    {
                        try { Clipboard.SetText(result); } catch { }
                    });

                    KeyboardHookManager.SimulatePaste();
                    Thread.Sleep(150);

                    Dispatcher.Invoke(() =>
                    {
                        try { Clipboard.SetText(oldClipboard); } catch { }
                    });

                    PlaySound(true);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "AI Assistant processing trigger failed.");
                    PlaySound(false);
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        private string GetShortcutStringForMode(string mode)
        {
            if (mode == "Accent") return AiAssistantController.Settings.ShortcutAccent;
            if (mode == "Translate") return AiAssistantController.Settings.ShortcutTranslate;
            return AiAssistantController.Settings.ShortcutCorrect;
        }

        private Button GetButtonForMode(string mode)
        {
            if (mode == "Accent") return RecordAccentHotkeyBtn;
            if (mode == "Translate") return RecordTranslateHotkeyBtn;
            return RecordCorrectHotkeyBtn;
        }

        private void SaveShortcutForMode(string mode, string hotkey)
        {
            if (mode == "Accent")
            {
                AiAssistantController.Settings.ShortcutAccent = hotkey;
                KeyboardHookManager.HotkeyAccent = KeyboardHookManager.HotkeyConfig.Parse(hotkey);
            }
            else if (mode == "Translate")
            {
                AiAssistantController.Settings.ShortcutTranslate = hotkey;
                KeyboardHookManager.HotkeyTranslate = KeyboardHookManager.HotkeyConfig.Parse(hotkey);
            }
            else
            {
                AiAssistantController.Settings.ShortcutCorrect = hotkey;
                KeyboardHookManager.HotkeyCorrect = KeyboardHookManager.HotkeyConfig.Parse(hotkey);
            }
            AiAssistantController.SaveSettings();
        }

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_recordingHotkeyMode != null)
            {
                e.Handled = true;

                if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl ||
                    e.Key == Key.LeftAlt || e.Key == Key.RightAlt ||
                    e.Key == Key.LeftShift || e.Key == Key.RightShift ||
                    e.Key == Key.LWin || e.Key == Key.RWin)
                {
                    return;
                }

                if (e.Key == Key.Escape)
                {
                    string oldKey = GetShortcutStringForMode(_recordingHotkeyMode);
                    GetButtonForMode(_recordingHotkeyMode).Content = $"{_recordingHotkeyMode} Shortcut: {oldKey}";
                    _recordingHotkeyMode = null;
                    return;
                }

                var modifiers = Keyboard.Modifiers;
                List<string> parts = new List<string>();
                if ((modifiers & ModifierKeys.Control) != 0) parts.Add("Ctrl");
                if ((modifiers & ModifierKeys.Alt) != 0) parts.Add("Alt");
                if ((modifiers & ModifierKeys.Shift) != 0) parts.Add("Shift");

                string keyName = e.Key.ToString();
                if (e.Key >= Key.D0 && e.Key <= Key.D9)
                    keyName = ((int)e.Key - (int)Key.D0).ToString();
                else if (e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9)
                    keyName = ((int)e.Key - (int)Key.NumPad0).ToString();

                parts.Add(keyName);

                string hotkey = string.Join("+", parts);
                SaveShortcutForMode(_recordingHotkeyMode, hotkey);

                GetButtonForMode(_recordingHotkeyMode).Content = $"{_recordingHotkeyMode} Shortcut: {hotkey}";
                _recordingHotkeyMode = null;
            }
        }

        private void BindTilde_CheckedChanged(object sender, RoutedEventArgs e)
        {
            bool isChecked = BindTilde.IsChecked == true;
            AiAssistantController.Settings.BindTildeEnabled = isChecked;
            AiAssistantController.SaveSettings();

            KeyboardHookManager.BindTildeToT = isChecked;
        }

        private void AiSoundEnabled_CheckedChanged(object sender, RoutedEventArgs e)
        {
            AiAssistantController.Settings.SoundEnabled = AiSoundEnabled.IsChecked == true;
            AiAssistantController.SaveSettings();
        }

        private void AiPhoneticEnabled_CheckedChanged(object sender, RoutedEventArgs e)
        {
            AiAssistantController.Settings.PhoneticEnabled = AiPhoneticEnabled.IsChecked == true;
            AiAssistantController.SaveSettings();
        }

        private void AiTemperature_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (AiTemperatureLabel == null || AiAssistantController.Settings == null) return;
            double val = Math.Round(e.NewValue, 1);
            AiTemperatureLabel.Content = val.ToString("0.0");
            AiAssistantController.Settings.Temperature = val;
            AiAssistantController.SaveSettings();
        }

        private void AiLength_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AiLength == null || AiAssistantController.Settings == null) return;

            string constraint = "Similar";
            if (AiLength.SelectedIndex == 0) constraint = "NoConstraint";
            else if (AiLength.SelectedIndex == 1) constraint = "Similar";
            else if (AiLength.SelectedIndex == 2) constraint = "Concise";

            AiAssistantController.Settings.LengthConstraint = constraint;
            AiAssistantController.SaveSettings();
        }

        private void AiMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AiMode == null || AiTargetInput == null || AiTargetLabel == null) return;

            string mode = "Accent";
            if (AiMode.SelectedIndex == 1) mode = "Translate";
            else if (AiMode.SelectedIndex == 2) mode = "Correct";

            AiAssistantController.Settings.Mode = mode;
            AiAssistantController.SaveSettings();

            if (mode == "Accent")
            {
                AiTargetInput.IsEnabled = true;
                AiTargetLabel.IsEnabled = true;
                AiTargetLabel.Content = "Target Accent (e.g. Texan Accent, Donald Trump):";
                AiTargetInput.Text = AiAssistantController.Settings.TargetAccent;
            }
            else if (mode == "Translate")
            {
                AiTargetInput.IsEnabled = true;
                AiTargetLabel.IsEnabled = true;
                AiTargetLabel.Content = "Target Language (e.g. Spanish, French, Italian):";
                AiTargetInput.Text = AiAssistantController.Settings.TargetLanguage;
            }
            else // Correct
            {
                AiTargetInput.Text = string.Empty;
                AiTargetInput.IsEnabled = false;
                AiTargetLabel.IsEnabled = false;
                AiTargetLabel.Content = "Target Accent / Language:";
            }
        }

        private void AiTargetInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (AiAssistantController.Settings == null) return;

            if (AiAssistantController.Settings.Mode == "Accent")
            {
                AiAssistantController.Settings.TargetAccent = AiTargetInput.Text;
            }
            else if (AiAssistantController.Settings.Mode == "Translate")
            {
                AiAssistantController.Settings.TargetLanguage = AiTargetInput.Text;
            }
            AiAssistantController.SaveSettings();
        }

        private void AiModel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AiModel == null || AiModel.SelectedItem == null) return;
            if (AiModel.SelectedItem is ComboBoxItem item && item.Content != null)
            {
                string model = item.Content.ToString() ?? string.Empty;
                AiAssistantController.Settings.ActiveModel = model;
                AiAssistantController.SaveSettings();
            }
        }

        private void AiModel_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (AiModel == null || AiAssistantController.Settings == null) return;
            string model = AiModel.Text.Trim();
            if (!string.IsNullOrEmpty(model))
            {
                AiAssistantController.Settings.ActiveModel = model;
                AiAssistantController.SaveSettings();
            }
        }

        private void ModelInfoHelpBtn_Click(object sender, RoutedEventArgs e)
        {
            string info = "Available Groq Models:\n\n" +
                          "• llama-3.1-8b-instant:\n" +
                          "  Extremely fast, lowest latency. Great for standard accent conversion.\n\n" +
                          "• llama-3.3-70b-versatile:\n" +
                          "  High quality and versatile. Best balance of reasoning and vocabulary.\n\n" +
                          "• deepseek-r1-distill-llama-70b:\n" +
                          "  Excellent reasoning model distilled from DeepSeek R1. Recommended for complex personas.\n\n" +
                          "• deepseek-r1-distill-qwen-32b:\n" +
                          "  Great creative writing and reasoning quality. Fast speed.\n\n" +
                          "• gemma2-9b-it:\n" +
                          "  Google's efficient 9B model. Fast speed, good daily limit.";
            MessageBox.Show(this, info, "Model Information", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void TabLogParserBtn_Click(object sender, RoutedEventArgs e)
        {
            SwitchToTab(true);
        }

        private void TabAiAssistantBtn_Click(object sender, RoutedEventArgs e)
        {
            SwitchToTab(false);
        }

        private void SwitchToTab(bool isLogParser)
        {
            if (isLogParser)
            {
                LogParserGrid.Visibility = Visibility.Visible;
                AiAssistantGrid.Visibility = Visibility.Collapsed;
                
                TabLogParserBtn.SetResourceReference(BackgroundProperty, "MahApps.Brushes.Accent");
                TabLogParserBtn.SetResourceReference(ForegroundProperty, "MahApps.Brushes.IdealForeground");
                TabLogParserBtn.FontWeight = FontWeights.Bold;

                TabAiAssistantBtn.Background = Brushes.Transparent;
                TabAiAssistantBtn.SetResourceReference(ForegroundProperty, "MahApps.Brushes.Text");
                TabAiAssistantBtn.FontWeight = FontWeights.Normal;
            }
            else
            {
                LogParserGrid.Visibility = Visibility.Collapsed;
                AiAssistantGrid.Visibility = Visibility.Visible;

                TabAiAssistantBtn.SetResourceReference(BackgroundProperty, "MahApps.Brushes.Accent");
                TabAiAssistantBtn.SetResourceReference(ForegroundProperty, "MahApps.Brushes.IdealForeground");
                TabAiAssistantBtn.FontWeight = FontWeights.Bold;

                TabLogParserBtn.Background = Brushes.Transparent;
                TabLogParserBtn.SetResourceReference(ForegroundProperty, "MahApps.Brushes.Text");
                TabLogParserBtn.FontWeight = FontWeights.Normal;
            }
        }

        private void RecordAccentHotkeyBtn_Click(object sender, RoutedEventArgs e)
        {
            _recordingHotkeyMode = "Accent";
            RecordAccentHotkeyBtn.Content = "Press key combo (Esc to cancel)...";
        }

        private void RecordTranslateHotkeyBtn_Click(object sender, RoutedEventArgs e)
        {
            _recordingHotkeyMode = "Translate";
            RecordTranslateHotkeyBtn.Content = "Press key combo (Esc to cancel)...";
        }

        private void RecordCorrectHotkeyBtn_Click(object sender, RoutedEventArgs e)
        {
            _recordingHotkeyMode = "Correct";
            RecordCorrectHotkeyBtn.Content = "Press key combo (Esc to cancel)...";
        }

        private void ManageKeysBtn_Click(object sender, RoutedEventArgs e)
        {
            GroqApiWindow keysWindow = new GroqApiWindow();
            keysWindow.Owner = this;
            keysWindow.ShowDialog();
        }
    }
}
