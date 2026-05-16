using System;
using System.Linq;
using System.Threading;
using System.Globalization;
using System.Collections.Generic;

namespace GTAWParser.Shared
{
    /// <summary>
    /// Sets the current thread's UI culture from a language code or enum.
    /// Decoupled from per-app settings — callers pass an optional persist
    /// callback when they want a setting flushed.
    /// </summary>
    public static class LocalizationController
    {
        private static string currentLanguage = string.Empty;
        public enum Language { English, Spanish }

        private static readonly Dictionary<Language, string> Languages = new Dictionary<Language, string>
        {
            { Language.English, "en-US" },
            { Language.Spanish, "es-ES" }
        };

        /// <summary>
        /// Initializes the locale from a saved code, falling back to English.
        /// </summary>
        public static void InitializeLocale(string? savedCode = null, Action<string>? persist = null)
        {
            if (string.IsNullOrWhiteSpace(currentLanguage))
                currentLanguage = string.IsNullOrWhiteSpace(savedCode) ? Languages[Language.English] : savedCode;

            Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo(currentLanguage);

            persist?.Invoke(currentLanguage);
        }

        public static string GetLanguage() => currentLanguage;

        /// <summary>
        /// Switches to the given language. If <paramref name="persist"/> is
        /// supplied, it is invoked with the new code so the caller can save it.
        /// </summary>
        public static void SetLanguage(Language language, Action<string>? persist = null)
        {
            if (!Languages.ContainsKey(language))
                language = Language.English;

            currentLanguage = Languages[language];
            InitializeLocale(currentLanguage, persist);
        }

        public static string GetLanguageFromCode(string code)
        {
            return Languages.FirstOrDefault(x => x.Value == code).Key.ToString() ?? Language.English.ToString();
        }

        public static string GetCodeFromLanguage(Language language)
        {
            return Languages.TryGetValue(language, out string? code) ? code : Languages[Language.English];
        }
    }
}
