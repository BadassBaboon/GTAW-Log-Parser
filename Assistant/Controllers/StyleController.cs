using System;
using ControlzEx.Theming;
using System.Windows;
using Microsoft.Win32;
using Assistant.Properties;
using Serilog;
using System.Collections.Generic;
using System.Windows.Media;

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

                // Apply or remove GTA World theme overrides dynamically
                ApplyGtaWorldThemeOverrides(Style == "Default");

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

        /// <summary>
        /// Dynamically injects or removes the custom GTA World accent and title bar colors
        /// so they do not bleed into other selectable themes.
        /// </summary>
        public static void ApplyGtaWorldThemeOverrides(bool apply)
        {
            var app = Application.Current;
            if (app == null) return;

            string[] brushKeys = new[]
            {
                "MahApps.Brushes.Accent",
                "MahApps.Brushes.Accent2",
                "MahApps.Brushes.Accent3",
                "MahApps.Brushes.Accent4",
                "MahApps.Brushes.WindowTitle",
                "MahApps.Brushes.Border.Accent",
                "MahApps.Brushes.IdealForeground",
                "MahApps.Brushes.WindowTitleText"
            };

            string[] colorKeys = new[]
            {
                "MahApps.Colors.Accent",
                "MahApps.Colors.Accent2",
                "MahApps.Colors.Accent3",
                "MahApps.Colors.Accent4",
                "MahApps.Colors.IdealForeground",
                "MahApps.Colors.WindowTitleText"
            };

            if (apply)
            {
                var brushConverter = new BrushConverter();
                
                app.Resources["MahApps.Colors.Accent"] = ColorConverter.ConvertFromString("#febf2d");
                app.Resources["MahApps.Colors.Accent2"] = ColorConverter.ConvertFromString("#e0a724");
                app.Resources["MahApps.Colors.Accent3"] = ColorConverter.ConvertFromString("#c28f1b");
                app.Resources["MahApps.Colors.Accent4"] = ColorConverter.ConvertFromString("#a37812");
                app.Resources["MahApps.Colors.IdealForeground"] = ColorConverter.ConvertFromString("#000000");
                app.Resources["MahApps.Colors.WindowTitleText"] = ColorConverter.ConvertFromString("#000000");

                app.Resources["MahApps.Brushes.Accent"] = brushConverter.ConvertFromString("#febf2d");
                app.Resources["MahApps.Brushes.Accent2"] = brushConverter.ConvertFromString("#e0a724");
                app.Resources["MahApps.Brushes.Accent3"] = brushConverter.ConvertFromString("#c28f1b");
                app.Resources["MahApps.Brushes.Accent4"] = brushConverter.ConvertFromString("#a37812");
                app.Resources["MahApps.Brushes.WindowTitle"] = brushConverter.ConvertFromString("#febf2d");
                app.Resources["MahApps.Brushes.Border.Accent"] = brushConverter.ConvertFromString("#febf2d");
                app.Resources["MahApps.Brushes.IdealForeground"] = brushConverter.ConvertFromString("#000000");
                app.Resources["MahApps.Brushes.WindowTitleText"] = brushConverter.ConvertFromString("#000000");
            }
            else
            {
                foreach (var key in brushKeys)
                {
                    if (app.Resources.Contains(key))
                        app.Resources.Remove(key);
                }
                foreach (var key in colorKeys)
                {
                    if (app.Resources.Contains(key))
                        app.Resources.Remove(key);
                }
            }
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
