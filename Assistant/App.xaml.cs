using System;
using System.Linq;
using Assistant.UI;
using System.Windows;
using System.Threading;
using Assistant.Properties;
using Assistant.Controllers;
using GTAWParser.Shared;

namespace Assistant
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App
    {
        private static Mutex? _appMutex;
        private static bool startMinimized;
        private static bool isRestarted;

        [System.Runtime.InteropServices.DllImport("shell32.dll", SetLastError = true)]
        private static extern void SetCurrentProcessExplicitAppUserModelID([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] string AppID);

        /// <summary>
        /// Initializes the "follow system eligibility"
        /// for the app mode and system accent color
        /// </summary>
        /// <param name="e"></param>
        protected override void OnStartup(StartupEventArgs e)
        {
            try
            {
                SetCurrentProcessExplicitAppUserModelID("GTAW.ChatLogAssistant");
            }
            catch
            {
                // Ignored if not supported on older platforms
            }

            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                if (args.ExceptionObject is Exception ex)
                    Serilog.Log.Fatal(ex, "Unhandled AppDomain Exception");
            };
            DispatcherUnhandledException += (s, args) =>
            {
                Serilog.Log.Fatal(args.Exception, "Unhandled Dispatcher Exception");
            };

            Logging.Initialize("Assistant");
            AppController.MigrateLegacyAppDataDirectories();
            AppSettingsManager.Initialize(Settings.Default, "assistant_settings.json", "GTAWAssistant*", "GTAW-Log-Parser*");

            // Initialize the eligibility
            StyleController.InitializeFollowEligibility();

            // Set the current app mode depending
            // on the "follow system eligibility"
            if (Settings.Default.FollowSystemMode)
            {
                if (AppController.CanFollowSystemMode)
                    StyleController.DarkMode = StyleController.GetAppMode();
                else
                    Settings.Default.FollowSystemMode = false;
            }

            // Set the current app theme depending
            // on the "follow system eligibility"
            if (Settings.Default.FollowSystemColor && !AppController.CanFollowSystemColor)
                Settings.Default.FollowSystemColor = false;
            Settings.Default.Save();

            // Apply the changes
            StyleController.UpdateTheme();
            base.OnStartup(e);
        }

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Application_Startup(object sender, StartupEventArgs e)
        {
            try
            {
                Serilog.Log.Information("Application_Startup triggered. Args: {Args}", string.Join(" ", Environment.GetCommandLineArgs()));

                // Get the command line arguments and check
                // if the current session is a restart or
                // a minimized start
                string[] args = Environment.GetCommandLineArgs();
                if (args.Any(arg => arg == $"{AppController.ParameterPrefix}restart"))
                    isRestarted = true;

                if (args.Any(arg => arg == $"{AppController.ParameterPrefix}minimized"))
                    startMinimized = true;

                bool isQuickLaunch = args.Any(arg => arg == $"{AppController.ParameterPrefix}quick-launch" || arg == $"{AppController.ParameterPrefix}launch-game");

                if (isQuickLaunch)
                {
                    startMinimized = true;
                    FiveMDetector.LaunchFiveMAndConnect("fivem.gta.world");
                }

                // Make sure only one instance is running per user session
                // if the application is not currently restarting
                try
                {
                    _appMutex = new Mutex(true, @"Local\" + AppController.MutexName, out bool isUnique);
                    if (!isUnique && !isRestarted)
                    {
                        if (isQuickLaunch)
                        {
                            Serilog.Log.Information("Quick-launch triggered while another instance is running. Exiting duplicate instance cleanly without error dialog.");
                            Current.Shutdown();
                            return;
                        }

                        Serilog.Log.Warning("Another instance is already running (Mutex not unique). Shutting down.");
                        MessageBox.Show(Localization.Strings.OtherInstanceRunning, Localization.Strings.Error, MessageBoxButton.OK, MessageBoxImage.Error);
                        Current.Shutdown();
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Serilog.Log.Warning(ex, "Could not acquire single-instance mutex. Continuing startup.");
                }

                // Initialize the controllers and
                // display the server picker on the
                // first start, or the main window
                // on subsequent starts
                LocalizationController.InitializeLocale(Settings.Default.LanguageCode);
                AppController.InitializeServerIp();

                if (!Settings.Default.HasPickedLanguage)
                {
                    Settings.Default.LanguageCode = LocalizationController.GetCodeFromLanguage(LocalizationController.Language.English);
                    Settings.Default.HasPickedLanguage = true;
                    Settings.Default.Save();
                }

                Serilog.Log.Information("Creating MainWindow (startMinimized={StartMinimized})", startMinimized);
                MainWindow mainWindow = new MainWindow(startMinimized);
                if (!startMinimized)
                {
                    mainWindow.Show();
                    Serilog.Log.Information("MainWindow.Show() called");
                }

                // Don't let the garbage
                // collector touch the Mutex
                if (_appMutex != null)
                    GC.KeepAlive(_appMutex);
            }
            catch (Exception ex)
            {
                Serilog.Log.Fatal(ex, "Fatal exception during Application_Startup");
                throw;
            }
        }

        /// <summary>
        /// Stops the running threads when
        /// quitting the application
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Application_Exit(object sender, ExitEventArgs e)
        {
            StyleController.StopWatchers();
            BackupController.Quitting = true;
            FiveMChatCaptureService.Stop();
            Logging.Shutdown();

            if (_appMutex != null)
            {
                try
                {
                    _appMutex.ReleaseMutex();
                }
                catch
                {
                    // Ignored if mutex was not acquired or already released
                }
                _appMutex.Dispose();
                _appMutex = null;
            }
        }
    }
}
