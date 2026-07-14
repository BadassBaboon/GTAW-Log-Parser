using System;
using ControlzEx.Theming;
using System.Windows;
using Microsoft.Win32;
using Assistant.Properties;
using Serilog;
using System.Collections.Generic;

namespace Assistant.Controllers
{
    public static class StyleController
    {
        private const string DefaultLightAccent = "Amber";
        private const string DefaultDarkAccent = "Amber";

        public static bool DarkMode
        {
            get => Settings.Default.DarkMode;
            set
            {
                Settings.Default.DarkMode = value;
                Settings.Default.Save();
            }
        }

        public static string Style
        {
            get => Settings.Default.Theme;
            set
            {
                Settings.Default.Theme = value;
                Settings.Default.Save();
            }
        }

        public static readonly List<string> ValidStyles = new List<string>
        {
            "Default",
            "Red", "Green", "Blue", "Purple", "Orange",
            "Lime", "Emerald", "Teal", "Cyan", "Cobalt", "Indigo", "Violet",
            "Pink", "Magenta", "Crimson", "Amber", "Yellow", "Brown",
            "Olive", "Steel", "Mauve", "Taupe", "Sienna"
        };

        /// <summary>
        /// Detects whether the system exposes the registry keys we need
        /// to follow system app mode and accent color.
        /// </summary>
        public static void InitializeFollowEligibility()
        {
            try
            {
                object? keyValue = Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "AppsUseLightTheme", null);
                AppController.CanFollowSystemMode = keyValue != null;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "AppMode probe failed");
                AppController.CanFollowSystemMode = false;
            }

            try
            {
                object? keyValue = Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\DWM",
                    "ColorizationColor", null);
                AppController.CanFollowSystemColor = keyValue != null && AppController.CanFollowSystemMode;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "SystemAccent probe failed");
                AppController.CanFollowSystemColor = false;
            }
        }

        /// <summary>
        /// MahApps 2.x's ThemeSyncMode handles change-watching internally,
        /// so there's nothing for us to stop on shutdown.
        /// </summary>
        public static void StopWatchers()
        {
        }

        /// <summary>
        /// Returns true if Windows is in dark mode (AppsUseLightTheme == 0).
        /// </summary>
        public static bool GetAppMode()
        {
            try
            {
                object? keyValue = Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "AppsUseLightTheme", null);
                return keyValue != null && (uint)(int)keyValue == 0;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "GetAppMode failed");
                AppController.CanFollowSystemMode = false;
                Settings.Default.FollowSystemMode = false;
                Settings.Default.Save();
                return false;
            }
        }

        /// <summary>
        /// Applies the current app mode + accent. If "follow system" is on for
        /// either dimension, ThemeManager's built-in sync mode takes over.
        /// </summary>
        public static void UpdateTheme()
        {
            if (!ValidStyles.Contains(Style))
                Style = "Default";

            Application.Current?.Dispatcher?.Invoke(() =>
            {
                ApplyThemeSyncMode();

                string accent = Style == "Default"
                    ? (DarkMode ? DefaultDarkAccent : DefaultLightAccent)
                    : Style;

                string baseColor = DarkMode ? "Dark" : "Light";

                try
                {
                    ThemeManager.Current.ChangeTheme(Application.Current, $"{baseColor}.{accent}");
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "ChangeTheme failed for {BaseColor}.{Accent}", baseColor, accent);
                    ThemeManager.Current.ChangeTheme(Application.Current, $"{baseColor}.{(DarkMode ? DefaultDarkAccent : DefaultLightAccent)}");
                }
            });
        }

        private static void ApplyThemeSyncMode()
        {
            bool followMode = Settings.Default.FollowSystemMode && AppController.CanFollowSystemMode;
            bool followColor = Settings.Default.FollowSystemColor && AppController.CanFollowSystemColor;

            ThemeSyncMode mode;
            if (followMode && followColor)
                mode = ThemeSyncMode.SyncAll;
            else if (followMode)
                mode = ThemeSyncMode.SyncWithAppMode;
            else if (followColor)
                mode = ThemeSyncMode.SyncWithAccent;
            else
                mode = ThemeSyncMode.DoNotSync;

            ThemeManager.Current.ThemeSyncMode = mode;

            if (mode != ThemeSyncMode.DoNotSync)
                ThemeManager.Current.SyncTheme();
        }
    }
}
