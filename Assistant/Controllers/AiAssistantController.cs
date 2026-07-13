using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Assistant.Controllers
{
    public class GroqApiKeyInfo
    {
        public string ApiKey { get; set; } = string.Empty;
        public int RequestCount { get; set; }
        public DateTime LastUsedDate { get; set; } = DateTime.Today;
        public bool IsActive { get; set; } = true;
        public bool IsRateLimited { get; set; } = false;

        public string DisplayKey
        {
            get
            {
                if (string.IsNullOrEmpty(ApiKey)) return string.Empty;
                if (ApiKey.Length <= 10) return "****";
                return ApiKey.Substring(0, 7) + "..." + ApiKey.Substring(ApiKey.Length - 4);
            }
        }
    }

    public class AiAssistantSettings
    {
        public List<GroqApiKeyInfo> ApiKeys { get; set; } = new List<GroqApiKeyInfo>();
        public string ActiveModel { get; set; } = "llama-3.1-8b-instant";
        public string Mode { get; set; } = "Accent"; // Accent, Translate, Correct
        public string TargetAccent { get; set; } = "Texan Accent";
        public string TargetLanguage { get; set; } = "Spanish";
        public string ShortcutKey { get; set; } = "Ctrl+T";
        public bool SoundEnabled { get; set; } = true;
        public bool BindTildeEnabled { get; set; } = false;
        public string LengthConstraint { get; set; } = "Similar"; // NoConstraint, Similar, Concise
        public bool PhoneticEnabled { get; set; } = true;
        public double Temperature { get; set; } = 0.6;
    }

    public static class AiAssistantController
    {
        private static readonly string ConfigDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GTAWChatLogAssistant"
        );
        private static readonly string ConfigFile = Path.Combine(ConfigDir, "ai_settings.json");
        private static readonly HttpClient _httpClient = new HttpClient();

        public static AiAssistantSettings Settings { get; private set; } = new AiAssistantSettings();

        static AiAssistantController()
        {
            LoadSettings();
        }

        public static void LoadSettings()
        {
            try
            {
                if (!Directory.Exists(ConfigDir))
                {
                    Directory.CreateDirectory(ConfigDir);
                }

                if (File.Exists(ConfigFile))
                {
                    string json = File.ReadAllText(ConfigFile);
                    var loaded = JsonSerializer.Deserialize<AiAssistantSettings>(json);
                    if (loaded != null)
                    {
                        Settings = loaded;
                        ResetQuotasIfNeeded();
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Failed to load AI Assistant settings.");
            }

            // Create default settings if failed or not exist
            Settings = new AiAssistantSettings();
            SaveSettings();
        }

        public static void SaveSettings()
        {
            try
            {
                if (!Directory.Exists(ConfigDir))
                {
                    Directory.CreateDirectory(ConfigDir);
                }

                string json = JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true });
                File.ReadAllText(ConfigFile); // probe
                File.WriteAllText(ConfigFile, json);
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Failed to save AI Assistant settings.");
            }
        }

        public static void ResetQuotasIfNeeded()
        {
            bool changed = false;
            DateTime today = DateTime.Today;

            foreach (var key in Settings.ApiKeys)
            {
                if (key.LastUsedDate.Date != today)
                {
                    key.RequestCount = 0;
                    key.LastUsedDate = today;
                    key.IsRateLimited = false; // Reset rate limit status daily
                    changed = true;
                }
            }

            if (changed)
            {
                SaveSettings();
            }
        }

        // Retrieves the next available API key. Performs rotation and quota checking.
        private static GroqApiKeyInfo? GetNextApiKey()
        {
            ResetQuotasIfNeeded();

            var availableKeys = Settings.ApiKeys
                .Where(k => k.IsActive && !k.IsRateLimited && !string.IsNullOrWhiteSpace(k.ApiKey))
                .OrderBy(k => k.RequestCount) // Pick the one with the fewest requests today
                .ToList();

            return availableKeys.FirstOrDefault();
        }

        public static async Task<string> ProcessTextAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            // 1. Check for slash command prefix (e.g. /me, /do, /s, /w, /b)
            string commandPrefix = "";
            string textToProcess = text;

            // Matches ^/command_name followed by whitespace
            var match = Regex.Match(text, @"^/([a-zA-Z0-9_]+)\s+(.*)", RegexOptions.Singleline);
            if (match.Success)
            {
                commandPrefix = "/" + match.Groups[1].Value + " ";
                textToProcess = match.Groups[2].Value;
            }

            // 2. Build system prompt based on mode and length constraints
            string systemPrompt = "";

            if (Settings.Mode == "Accent")
            {
                string constraintRules = "";
                if (Settings.LengthConstraint == "Similar")
                {
                    constraintRules = "Maintain similar length. ";
                }
                else if (Settings.LengthConstraint == "Concise")
                {
                    constraintRules = "Keep it short and punchy. ";
                }

                string phoneticInstruction = "";
                if (Settings.PhoneticEnabled)
                {
                    phoneticInstruction = "Write words phonetically where they would pronounce them differently (spell words how they sound when spoken). ";
                }
                else
                {
                    phoneticInstruction = "Use standard English spelling. Do not write words phonetically, but adjust vocabulary and syntax. ";
                }

                systemPrompt = $"Rewrite the text in the requested style. " +
                               $"RULES: DO NOT write conversational replies. " +
                               $"DO NOT start with 'Whaddaya mean' or caricature phrases like 'Fuggedaboutit'. " +
                               $"Paraphrase deeply to match how the character would express the underlying thought in a realistic conversation. " +
                               $"Use natural profanity or complaints (like headaches/stress) if it fits. " +
                               constraintRules +
                               phoneticInstruction +
                               $"No AI slop, no flowery language.";
            }
            else if (Settings.Mode == "Translate")
            {
                string constraintRules = "";
                if (Settings.LengthConstraint == "Similar")
                {
                    constraintRules = "Keep translation close to original length. ";
                }
                else if (Settings.LengthConstraint == "Concise")
                {
                    constraintRules = "Keep translation as short as possible. ";
                }

                systemPrompt = $"Translate the text into the requested language. " +
                               $"RULES: Return ONLY the translation. " +
                               $"Do not explain or add commentary. " +
                               constraintRules +
                               $"Sound natural to a native speaker.";
            }
            else // Correct
            {
                string constraintRules = "";
                if (Settings.LengthConstraint == "Similar")
                {
                    constraintRules = "Keep corrected text same length. ";
                }
                else if (Settings.LengthConstraint == "Concise")
                {
                    constraintRules = "Make corrected text concise. ";
                }

                systemPrompt = $"Correct grammar and spelling errors in the text while keeping tone and style identical. " +
                               $"RULES: Return ONLY the corrected text. " +
                               $"If there are no errors, return the text exactly as-is. " +
                               constraintRules +
                               $"Do not explain.";
            }

            // 3. Request Loop with Key Rotation
            int retryCount = 0;
            int maxRetries = Settings.ApiKeys.Count(k => k.IsActive && !string.IsNullOrWhiteSpace(k.ApiKey));
            if (maxRetries == 0)
            {
                throw new InvalidOperationException("No active Groq API keys configured. Please add one in the AI Assistant settings.");
            }

            while (retryCount < maxRetries)
            {
                var keyInfo = GetNextApiKey();
                if (keyInfo == null)
                {
                    // Reset rate limited flags to retry
                    foreach (var k in Settings.ApiKeys)
                        k.IsRateLimited = false;
                    keyInfo = GetNextApiKey();

                    if (keyInfo == null)
                    {
                        throw new InvalidOperationException("All configured API keys are currently rate-limited or unavailable.");
                    }
                }

                try
                {
                    string userPromptContent = "";
                    if (Settings.Mode == "Accent")
                    {
                        userPromptContent = $"Style: {Settings.TargetAccent}\nOriginal: {textToProcess}\nTranslation:";
                    }
                    else if (Settings.Mode == "Translate")
                    {
                        userPromptContent = $"Language: {Settings.TargetLanguage}\nOriginal: {textToProcess}\nTranslation:";
                    }
                    else // Correct
                    {
                        userPromptContent = $"Original: {textToProcess}\nCorrected:";
                    }

                    var requestBody = new
                    {
                        model = Settings.ActiveModel,
                        messages = new[]
                        {
                            new { role = "system", content = systemPrompt },
                            new { role = "user", content = userPromptContent }
                        },
                        temperature = Settings.Temperature
                    };

                    string jsonBody = JsonSerializer.Serialize(requestBody);

                    using (var request = new HttpRequestMessage(HttpMethod.Post, "https://api.groq.com/openai/v1/chat/completions"))
                    {
                        request.Headers.Add("Authorization", $"Bearer {keyInfo.ApiKey}");
                        request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

                        var response = await _httpClient.SendAsync(request);

                        if (response.StatusCode == System.Net.HttpStatusCode.OK)
                        {
                            string responseJson = await response.Content.ReadAsStringAsync();
                            using (var doc = JsonDocument.Parse(responseJson))
                            {
                                var content = doc.RootElement
                                    .GetProperty("choices")[0]
                                    .GetProperty("message")
                                    .GetProperty("content")
                                    .GetString();

                                if (content != null)
                                {
                                    keyInfo.RequestCount++;
                                    keyInfo.LastUsedDate = DateTime.Today;
                                    SaveSettings();

                                    string cleanedResult = content.Trim();
                                    // Remove enclosing quotes if model incorrectly added them
                                    if (cleanedResult.StartsWith("\"") && cleanedResult.EndsWith("\""))
                                    {
                                        cleanedResult = cleanedResult.Substring(1, cleanedResult.Length - 2).Trim();
                                    }

                                    return commandPrefix + cleanedResult;
                                }
                            }
                        }
                        else if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests) // 429
                        {
                            keyInfo.IsRateLimited = true;
                            SaveSettings();
                            // Fall through to retry next key
                        }
                        else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized) // 401
                        {
                            keyInfo.IsActive = false; // Disable invalid key
                            SaveSettings();
                            // Fall through to retry next key
                        }
                        else
                        {
                            string errContent = await response.Content.ReadAsStringAsync();
                            throw new HttpRequestException($"Groq API responded with status {response.StatusCode}: {errContent}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Serilog.Log.Warning(ex, $"Failed request using key {keyInfo.DisplayKey}. Rotating key.");
                }

                retryCount++;
            }

            throw new Exception("Translation failed after trying all available Groq API keys.");
        }
    }
}
