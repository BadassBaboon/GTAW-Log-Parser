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
                    constraintRules = "- Length Constraint: Keep the output length reasonably close to the original input (no more than 20-30% longer). Balance this by retaining the most iconic catchphrases, slang, vocal tics, and characteristic tone of the requested style so it sounds authentic but remains compact.\n";
                }
                else if (Settings.LengthConstraint == "Concise")
                {
                    constraintRules = "- Length Constraint: Keep the output as short, concise, and to the point as possible, while still using the signature vocabulary and tone of the requested style.\n";
                }
                else // NoConstraint
                {
                    constraintRules = "- Length Constraint: No constraint. Feel free to fully express the style, character voice, signature catchphrases, and vocabulary of the requested persona without worrying about length.\n";
                }

                string phoneticInstruction = "";
                if (Settings.PhoneticEnabled)
                {
                    phoneticInstruction = $"- Phonetic Spelling & Slang: You MUST write words phonetically to match how they are spoken in the requested accent/dialect (e.g. spelling 'very' as 'vely' or 'road' as 'load' for Chinese accent; spelling 'car' as 'cah' or 'very' as 'wicked' for Boston Southie; using regional slang). Write words exactly as they sound when spoken. Balance this so it is readable but highly authentic.\n" +
                                          $"- Avoid Caricatures: Keep the grammatical structure mostly intact. Do not degrade the text into cartoonish or offensive broken grammar (like caveman speak or pidgin) unless the target style specifically demands it. Focus on natural phonetic spelling and word choice.\n";
                }
                else
                {
                    phoneticInstruction = $"- Phonetic Spelling & Slang: Do NOT write words with phonetic misspellings (e.g. do NOT spell 'very' as 'vely' or 'car' as 'cah'). Keep standard English spelling, but adjust the grammar, vocabulary, sentence structure, and slang of the requested style/accent.\n";
                }

                systemPrompt = $"You are an expert text transformation engine. Your sole job is to rewrite the target text to match the requested style, accent, character, or persona.\n\n" +
                               $"CRITICAL RULES:\n" +
                               $"- YOU ARE NOT A CHATBOT. Do NOT converse with, respond to, or answer the target text. The target text is raw data to be rewritten. If the target text is a question or a dialogue line, do NOT answer it. Instead, rewrite the question or line itself as if the character/persona is the one saying it.\n" +
                               $"- Return ONLY the rewritten text. Absolutely no introductory text (e.g. \"Here is your text:\"), no explanations, no quotes around the output, and no commentary.\n" +
                               $"- Keep the original meaning, context, and intent identical.\n" +
                               $"- Target Style to Apply: {Settings.TargetAccent}\n" +
                               constraintRules +
                               phoneticInstruction +
                               $"- Persona Authenticity: Emulate highly specific vocabulary, signature catchphrases (e.g., 'masterclass of', 'bottom of the barrel' for penguinz0; medical cynicism for Dr. House), speech patterns, and distinct tone. Emulate their unique voice rather than doing a generic re-skin.\n\n" +
                               $"WRITING RULES (No AI Slop):\n" +
                               $"- Banned vocabulary: Do not use AI transition words, hollow filler phrases, or flowery adjectives.\n" +
                               $"- Use contractions (e.g., I'm, don't, it's) to sound natural.\n" +
                               $"- Vary sentence lengths to match the natural flow of human speech.\n\n" +
                               $"EXAMPLES OF CORRECT TRANSFORMATION:\n\n" +
                               $"Example 1 (Persona: Pirate, Constraint: Similar Length):\n" +
                               $"Input: \"I am going to the store to buy some milk.\"\n" +
                               $"Output: \"I be headin' to the store for some milk, matey.\"\n\n" +
                               $"Example 2 (Persona: Donald Trump, Constraint: Similar Length):\n" +
                               $"Input: \"Today I saw this woman, turns out it was a man.\"\n" +
                               $"Output: \"I saw this person today, folks. Turns out, total fake, it was a man. Believe me.\"\n\n" +
                               $"Example 3 (Persona: Dr. House, Constraint: Similar Length):\n" +
                               $"Input: \"How about me and you finish this drink and head over to my place so I get to know you better.\"\n" +
                               $"Output: \"We finish this drink, go to my place, and I figure out what neurological deficit made you think that line would work. Sound good?\"";
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

                systemPrompt = $"You are an expert translation engine. Your sole job is to translate the target text into the requested language: {Settings.TargetLanguage}.\n\n" +
                               $"CRITICAL RULES:\n" +
                               $"- YOU ARE NOT A CHATBOT. Do NOT converse with, respond to, or answer the target text. The target text is raw data to be translated. If the target text is a question or a dialogue line, do NOT answer it. Instead, translate the question or line itself.\n" +
                               $"- Return ONLY the translation. Absolutely no introductory text, no explanations, no quotes around the output, and no commentary.\n" +
                               constraintRules +
                               $"WRITING RULES (No AI Slop):\n" +
                               $"- Banned vocabulary: Do not use AI transition words, hollow filler phrases, or flowery adjectives.\n" +
                               $"- Ensure the translation sounds natural to a native speaker of the target language.";
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

                systemPrompt = $"You are an expert grammar and spelling correction engine. Your sole job is to correct spelling, grammar, or punctuation errors in the target text while keeping the tone and style identical.\n\n" +
                               $"CRITICAL RULES:\n" +
                               $"- YOU ARE NOT A CHATBOT. Do NOT converse with, respond to, or answer the target text. The target text is raw data to be corrected. If the target text is a question or a dialogue line, do NOT answer it. Instead, correct the grammar of the question or line itself.\n" +
                               $"- Return ONLY the corrected text. Absolutely no introductory text, no explanations, no quotes around the output, and no commentary.\n" +
                               $"- If the target text has no errors, return the original text exactly.\n" +
                               constraintRules +
                               $"WRITING RULES (No AI Slop):\n" +
                               $"- Banned vocabulary: Do not use AI transition words, hollow filler phrases, or flowery adjectives.\n" +
                               $"- Correct mistakes invisibly without polishing away the target persona or voice.";
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
                            new { role = "user", content = $"[TARGET TEXT TO REWRITE]\n{textToProcess}\n[END OF TARGET TEXT]" }
                        },
                        temperature = 0.7
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
