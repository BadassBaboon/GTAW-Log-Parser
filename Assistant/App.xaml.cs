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

        /// <summary>
        /// Initializes the "follow system eligibility"
        /// for the app mode and system accent color
        /// </summary>
        /// <param name="e"></param>
        protected override void OnStartup(StartupEventArgs e)
        {
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

                // Make sure only one instance is running
                // if the application is not currently restarting
                _appMutex = new Mutex(true, @"Global\" + AppController.MutexName, out bool isUnique);
                if (!isUnique && !isRestarted)
                {
                    if (isQuickLaunch)
                    {
                        // The app is already running in background/tray; we already launched FiveM, so just exit cleanly
                        Serilog.Log.Information("Quick-launch triggered while another instance is running. Exiting duplicate instance cleanly without error dialog.");
                        Current.Shutdown();
                        return;
                    }

                    Serilog.Log.Warning("Another instance is already running (Mutex not unique). Shutting down.");
                    MessageBox.Show(Localization.Strings.OtherInstanceRunning, Localization.Strings.Error, MessageBoxButton.OK, MessageBoxImage.Error);
                    Current.Shutdown();
                    return;
                }

                // Check if settings already exist
                // for a previous assembly version
                if (!Settings.Default.HasPickedLanguage)
                {
                    Settings.Default.Upgrade();
                    Settings.Default.FollowSystemColor = false;
                    Settings.Default.Save();
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
            Logging.Shutdown();
        }
    }
}
