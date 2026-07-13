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
                    constraintRules = "- Maintain similar length and structure to the original.\n";
                }
                else if (Settings.LengthConstraint == "Concise")
                {
                    constraintRules = "- Keep the output short and punchy.\n";
                }

                string phoneticInstruction = "";
                if (Settings.PhoneticEnabled)
                {
                    phoneticInstruction = "- Write words phonetically where this character/accent would pronounce them differently (spell words how they sound when spoken).\n";
                }
                else
                {
                    phoneticInstruction = "- Use standard English spelling. Do not write words phonetically, but adjust vocabulary and sentence structure to fit the voice.\n";
                }

                systemPrompt = $"You are a text restyling API. You rewrite input text into a specified dialect or style.\n\n" +
                               $"RULES:\n" +
                               $"1. DO NOT reply to the text. You are translating it, not participating in a conversation.\n" +
                               $"2. NEVER start the output with a question (e.g. 'Whaddaya mean?') unless the original text starts with a question.\n" +
                               $"3. DO NOT add conversational reactions, filler, or cartoonish catchphrases (e.g. 'Fuggedaboutit').\n" +
                               $"4. DO NOT change the meaning. Preserve the original intent and core message of every sentence.\n" +
                               $"5. BE AUTHENTIC. Avoid caricatures or extreme stereotypes. Capture the natural cadence of the target voice.\n" +
                               $"6. NO AI SLOP. Avoid words like 'delve', 'tapestry', or flowery language. Use natural contractions and varied sentence lengths.\n" +
                               constraintRules +
                               phoneticInstruction;
            }
            else if (Settings.Mode == "Translate")
            {
                string constraintRules = "";
                if (Settings.LengthConstraint == "Similar")
                {
                    constraintRules = "- Keep the translation close to the original length and sentence structure.\n";
                }
                else if (Settings.LengthConstraint == "Concise")
                {
                    constraintRules = "- Keep the translation as short and concise as possible.\n";
                }

                systemPrompt = $"You are a translation engine. Translate the user's text into {Settings.TargetLanguage}.\n\n" +
                               $"RULES:\n" +
                               $"- Return ONLY the translation. No explanations, no quotes, no extra words.\n" +
                               constraintRules +
                               $"- Sound natural to a native speaker. Avoid flowery or AI-sounding language.";
            }
            else // Correct
            {
                string constraintRules = "";
                if (Settings.LengthConstraint == "Similar")
                {
                    constraintRules = "- Keep the corrected text approximately the same length.\n";
                }
                else if (Settings.LengthConstraint == "Concise")
                {
                    constraintRules = "- Make the corrected text concise, clear, and direct.\n";
                }

                systemPrompt = $"You are a grammar and spelling correction engine. Fix errors in the user's text while keeping the tone and style identical.\n\n" +
                               $"RULES:\n" +
                               $"- Return ONLY the corrected text. No explanations, no quotes, no extra words.\n" +
                               $"- If the text has no errors, return it exactly as-is.\n" +
                               constraintRules +
                               $"- Correct mistakes invisibly without polishing away the voice.";
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
                    var requestBody = new
                    {
                        model = Settings.ActiveModel,
                        messages = new[]
                        {
                            new { role = "system", content = systemPrompt },
                            new { role = "user", content = $"Target Style: {Settings.TargetAccent}\nOriginal Text: \"{textToProcess}\"\nRewritten Text:" }
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
