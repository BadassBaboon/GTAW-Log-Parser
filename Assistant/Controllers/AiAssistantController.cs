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

    public class CustomAccentProfile
    {
        public string TargetAccent { get; set; } = string.Empty;
        public string CustomDirectives { get; set; } = string.Empty;
    }

    public class AiAssistantSettings
    {
        public List<GroqApiKeyInfo> ApiKeys { get; set; } = new List<GroqApiKeyInfo>();
        public List<CustomAccentProfile> CustomProfiles { get; set; } = new List<CustomAccentProfile>();
        public string ActiveModel { get; set; } = "llama-3.1-8b-instant";
        public string Mode { get; set; } = "Accent"; // Accent, Translate, Correct
        public string TargetAccent { get; set; } = "Texan Accent";
        public string TargetLanguage { get; set; } = "Spanish";
        [Obsolete("Use ShortcutAccent instead")]
        public string? ShortcutKey { get; set; }

        public string ShortcutAccent { get; set; } = "Ctrl+T";
        public string ShortcutTranslate { get; set; } = "Ctrl+U";
        public string ShortcutCorrect { get; set; } = "Ctrl+H";
        public bool SoundEnabled { get; set; } = true;
        public bool BindTildeEnabled { get; set; } = false;
        public string LengthConstraint { get; set; } = "Similar"; // NoConstraint, Similar, Concise
        public bool PhoneticEnabled { get; set; } = false;
        public bool ActionEnricherEnabled { get; set; } = true;
        public double Temperature { get; set; } = 0.6;
        public bool MigratedToCtrlU { get; set; } = false;
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
#pragma warning disable CS0618
                        if (!string.IsNullOrEmpty(Settings.ShortcutKey))
                        {
                            Settings.ShortcutAccent = Settings.ShortcutKey;
                            Settings.ShortcutKey = null;
                            SaveSettings();
                        }
#pragma warning restore CS0618
                        if (!Settings.MigratedToCtrlU)
                        {
                            if (Settings.ShortcutTranslate == "Ctrl+Y")
                            {
                                Settings.ShortcutTranslate = "Ctrl+U";
                            }
                            Settings.MigratedToCtrlU = true;
                            SaveSettings();
                        }
                        EnsureDefaultProfiles();
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
            EnsureDefaultProfiles();
            SaveSettings();
        }

        private static void EnsureDefaultProfiles()
        {
            if (Settings.CustomProfiles == null)
            {
                Settings.CustomProfiles = new List<CustomAccentProfile>();
            }

            var sopranoProfile = Settings.CustomProfiles.FirstOrDefault(p => 
                p.TargetAccent != null && 
                p.TargetAccent.IndexOf("Tony Soprano", StringComparison.OrdinalIgnoreCase) >= 0);

            string latestSopranoDirectives = "NEVER use the word 'capisce'. Speak authoritatively with direct order-like phrasing. " +
                                             "NEVER write 'ovah' or 'ova' for 'over' (Tony Soprano pronounces 'over' normally, he does not have a Boston accent). " +
                                             "Avoid direct words like 'buddies', 'buddy', or 'friend'; use euphemisms instead (e.g. 'our friend from that thing', 'a friend of ours', 'our friend who celebrates Hanukkah'). " +
                                             "Use light phonetic spellings for ending 'g' on 'ing' words ('talkin', 'goin') and words like 'fuhchrissake' (for Christ's sake), 'fache' (face), 'shaw' (saw), 'dat' (that), 'pash' (pass), 'Chrishtufah' (Christopher). " +
                                             "Use signature phrases: 'the whole fuckin' thing', 'end of story', 'end of subject', 'poor you' (sarcastic), 'Jesus Christ...' (sigh), 'all due respect', 'you know what I'm sayin'?', 'this is givin' me agita' (heartburn/worry). " +
                                             "For surprise/disbelief, use 'the fuck?', 'get the fuck out!', or 'the fuck outta here!'. " +
                                             "Vocabulary terms like 'prick' or 'broad' (woman) can be used when natural, but do not force them into every sentence.";

            if (sopranoProfile == null)
            {
                Settings.CustomProfiles.Add(new CustomAccentProfile
                {
                    TargetAccent = "Tony Soprano",
                    CustomDirectives = latestSopranoDirectives
                });
                SaveSettings();
            }
            else if (sopranoProfile.CustomDirectives == null || !sopranoProfile.CustomDirectives.Contains("Vocabulary terms like 'prick'"))
            {
                // Upgrade to the improved directives
                sopranoProfile.CustomDirectives = latestSopranoDirectives;
                SaveSettings();
            }
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

        private static readonly string[] ActionPrefixes = new[]
        {
            "/me", "/do", "/dolow", "/melow", "/melong", "/dolong", "/ame", "/ado",
            "/my", "/mylow", "/mylong", "/amy"
        };

        public static bool IsActionCommand(string text, out string prefix, out string payload)
        {
            prefix = "";
            payload = "";
            if (string.IsNullOrWhiteSpace(text)) return false;

            var match = Regex.Match(text.Trim(), @"^/([a-zA-Z0-9_]+)\s+(.*)", RegexOptions.Singleline);
            if (match.Success)
            {
                string cmd = "/" + match.Groups[1].Value.ToLower();
                if (ActionPrefixes.Contains(cmd))
                {
                    prefix = "/" + match.Groups[1].Value + " ";
                    payload = match.Groups[2].Value;
                    return true;
                }
            }
            return false;
        }

        public static async Task<string> ProcessTextAsync(string text, string? overrideMode = null)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            string commandPrefix = "";
            string textToProcess = text;
            string systemPrompt = "";
            string activeMode = overrideMode ?? Settings.Mode;

            // Check if this is a roleplay action command (/me, /do, /melow, etc.) and Action Enricher is enabled
            if (Settings.ActionEnricherEnabled && IsActionCommand(text, out string actPrefix, out string actPayload))
            {
                commandPrefix = actPrefix;
                textToProcess = actPayload;

                string constraintRules = "";
                if (Settings.LengthConstraint == "Similar")
                {
                    constraintRules = "6. CRITICAL: Maintain a similar action length and word count to the original text. Do not over-expand simple short actions into long multi-sentence paragraphs.\n";
                }
                else if (Settings.LengthConstraint == "Concise")
                {
                    constraintRules = "6. CRITICAL: Keep the enriched action description short, concise, and punchy.\n";
                }

                string cmdLower = actPrefix.Trim().ToLower();
                string prefixCasingInstruction = "";
                if (cmdLower.StartsWith("/me") || cmdLower.StartsWith("/ame") || cmdLower.StartsWith("/melow") || cmdLower.StartsWith("/melong"))
                {
                    prefixCasingInstruction = "CRITICAL: The output will follow the character's name directly in GTA World chat (e.g. '* Firstname Lastname <action>'). The output MUST start with a lowercase letter and begin directly with a verb or action (e.g. 'side steps...', 'reaches under...'). DO NOT start with 'The figure', 'He', 'She', or any capitalized words.\n";
                }
                else if (cmdLower.StartsWith("/my") || cmdLower.StartsWith("/amy") || cmdLower.StartsWith("/mylow") || cmdLower.StartsWith("/mylong"))
                {
                    prefixCasingInstruction = "CRITICAL: The output will follow the character's name possessive directly in GTA World chat (e.g. '* Firstname Lastname's <body part/item action>'). The output MUST start with a lowercase letter and begin directly with the possessive noun/body part (e.g. 'wrist is deeply lacerated...', 'eyes widen...'). DO NOT start with 'The', 'His', 'Her', or any capitalized words.\n";
                }

                systemPrompt = "Enrich the roleplay action description to make it vivid, atmospheric, detailed, and expressive.\n" +
                               "RULES:\n" +
                               prefixCasingInstruction +
                               "1. Preserve the underlying action, intent, and scene context.\n" +
                               "2. Use standard English spelling and grammar (do NOT apply accent phonetics or slang to action descriptions).\n" +
                               "3. Return ONLY the enriched action description.\n" +
                               "4. Do not include conversational preambles, explanations, or quotes.\n" +
                               "5. DO NOT use em-dashes (— or --).\n" +
                               "7. Always end the action description with proper sentence-ending punctuation (a period '.', '?', or '!').\n" +
                               constraintRules;
            }
            else
            {
                // Matches ^/command_name followed by whitespace
                var match = Regex.Match(text, @"^/([a-zA-Z0-9_]+)\s+(.*)", RegexOptions.Singleline);
                if (match.Success)
                {
                    commandPrefix = "/" + match.Groups[1].Value + " ";
                    textToProcess = match.Groups[2].Value;
                }

                if (activeMode == "Accent")
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
                    phoneticInstruction = "Apply spelling conventions and slang words (e.g., dropping ending 'g' on 'ing' words, writing contractions, and using regional slang) directly to the rewritten statement. ";
                }
                else
                {
                    phoneticInstruction = "Use standard English spelling. Do not write words phonetically (like writing accent sounds, e.g. 'dat' or 'ova' unless explicitly instructed). Adjust vocabulary, phrasing, and syntax. ";
                }

                // Look for custom profiles matching the target accent name
                CustomAccentProfile? matchedProfile = null;
                if (Settings.CustomProfiles != null && Settings.TargetAccent != null)
                {
                    foreach (var profile in Settings.CustomProfiles)
                    {
                        if (!string.IsNullOrEmpty(profile.TargetAccent) &&
                            Settings.TargetAccent.IndexOf(profile.TargetAccent, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            matchedProfile = profile;
                            break;
                        }
                    }
                }

                string profileDirectives = "";
                if (matchedProfile != null && !string.IsNullOrEmpty(matchedProfile.CustomDirectives))
                {
                    profileDirectives = $"Specific speech guidelines for {matchedProfile.TargetAccent}: {matchedProfile.CustomDirectives} ";
                }

                systemPrompt = $"Rewrite the text in the requested style. " +
                               $"RULES: DO NOT write conversational replies. " +
                               $"DO NOT start with 'Whaddaya mean' or caricature phrases like 'Fuggedaboutit'. " +
                               $"DO NOT add introductory questions or conversational preambles (e.g. 'You're telling me', 'Are you saying', 'Listen here') unless they correspond directly to words in the original text. " +
                               $"DO NOT use em-dashes (— or --) under any circumstances. " +
                               $"Paraphrase deeply to match how the character would express the underlying thought in a realistic conversation. " +
                               $"Use natural profanity or complaints (like headaches/stress) if it fits. " +
                               constraintRules +
                               phoneticInstruction +
                               profileDirectives +
                               $"No AI slop, no flowery language.";
            }
            else if (activeMode == "Translate")
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
                    if (activeMode == "Accent")
                    {
                        userPromptContent = $"Style: {Settings.TargetAccent}\nOriginal: {textToProcess}\nTranslation:";
                    }
                    else if (activeMode == "Translate")
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
                        temperature = Settings.Temperature,
                        max_tokens = 1024
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

                                    // Strip reasoning/thinking tags (e.g. <think>...</think>) from Qwen/DeepSeek reasoning models
                                    cleanedResult = System.Text.RegularExpressions.Regex.Replace(
                                        cleanedResult,
                                        @"<think>[\s\S]*?</think>",
                                        string.Empty).Trim();

                                    // Fallback if there is an unclosed <think> tag
                                    if (cleanedResult.Contains("<think>"))
                                    {
                                        int idx = cleanedResult.IndexOf("<think>");
                                        cleanedResult = cleanedResult.Substring(0, idx).Trim();
                                    }

                                    // Remove em-dashes
                                    cleanedResult = cleanedResult.Replace("—", " ").Replace("--", " ");

                                    // Remove enclosing quotes if model incorrectly added them
                                    if (cleanedResult.StartsWith("\"") && cleanedResult.EndsWith("\""))
                                    {
                                        cleanedResult = cleanedResult.Substring(1, cleanedResult.Length - 2).Trim();
                                    }

                                    if (string.IsNullOrWhiteSpace(cleanedResult))
                                    {
                                        return text;
                                    }

                                    // Enforce finishing period / punctuation for action commands
                                    if (!string.IsNullOrWhiteSpace(commandPrefix))
                                    {
                                        if (cleanedResult.Length > 0 && !cleanedResult.EndsWith(".") && !cleanedResult.EndsWith("?") && !cleanedResult.EndsWith("!"))
                                        {
                                            cleanedResult += ".";
                                        }
                                    }

                                    // For /me and /my variants, enforce lowercase first character
                                    string lowerCmdPrefix = commandPrefix.Trim().ToLower();
                                    if (lowerCmdPrefix.StartsWith("/me") || lowerCmdPrefix.StartsWith("/ame") || lowerCmdPrefix.StartsWith("/my") || lowerCmdPrefix.StartsWith("/amy"))
                                    {
                                        if (cleanedResult.Length > 0 && char.IsUpper(cleanedResult[0]))
                                        {
                                            cleanedResult = char.ToLower(cleanedResult[0]) + cleanedResult.Substring(1);
                                        }
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

        public static async Task<CustomAccentProfile> GenerateProfileFromBackstoryAsync(string backstory)
        {
            if (string.IsNullOrWhiteSpace(backstory))
            {
                throw new ArgumentException("Backstory text cannot be empty.", nameof(backstory));
            }

            string systemPrompt = "You are an expert linguistic character analyst and roleplay persona creator.\n" +
                               $"Given a character's backstory, origin, age, personality, and background details, construct a tailored accent/speech profile.\n" +
                               $"RULES:\n" +
                               $"Output EXACTLY two sections formatted as follows:\n\n" +
                               $"NAME: [Concise Accent or Persona Name, e.g. South Boston Mobster]\n" +
                               $"DIRECTIVES: [Detailed bullet points specifying speech habits, local slang words, phrasing, tone, contractions, phonetic rules (like dropping ending 'g's or sound replacements), and any banned words or caricature cliches to avoid]\n\n" +
                               $"Do not include any conversational preamble or extra text outside of NAME: and DIRECTIVES:.";

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
                    string modelToUse = !string.IsNullOrEmpty(Settings.ActiveModel) && Settings.ActiveModel.Contains("8b")
                        ? "llama-3.3-70b-versatile"
                        : Settings.ActiveModel;

                    var requestBody = new
                    {
                        model = modelToUse,
                        messages = new[]
                        {
                            new { role = "system", content = systemPrompt },
                            new { role = "user", content = backstory }
                        },
                        temperature = 0.5,
                        max_tokens = 1024
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

                                    // Strip reasoning/thinking tags if present
                                    cleanedResult = System.Text.RegularExpressions.Regex.Replace(
                                        cleanedResult,
                                        @"<think>[\s\S]*?</think>",
                                        string.Empty).Trim();

                                    if (cleanedResult.Contains("<think>"))
                                    {
                                        int idx = cleanedResult.IndexOf("<think>");
                                        cleanedResult = cleanedResult.Substring(0, idx).Trim();
                                    }

                                    // Parse NAME: and DIRECTIVES:
                                    string name = "Custom Generated Profile";
                                    string directives = cleanedResult;

                                    var nameMatch = Regex.Match(cleanedResult, @"NAME:\s*(.+)", RegexOptions.IgnoreCase);
                                    var dirMatch = Regex.Match(cleanedResult, @"DIRECTIVES:\s*([\s\S]+)", RegexOptions.IgnoreCase);

                                    if (nameMatch.Success)
                                    {
                                        name = nameMatch.Groups[1].Value.Split('\n')[0].Trim();
                                    }
                                    if (dirMatch.Success)
                                    {
                                        directives = dirMatch.Groups[1].Value.Trim();
                                    }

                                    return new CustomAccentProfile
                                    {
                                        TargetAccent = name,
                                        CustomDirectives = directives
                                    };
                                }
                            }
                        }
                        else if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                        {
                            keyInfo.IsRateLimited = true;
                            SaveSettings();
                        }
                        else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                        {
                            keyInfo.IsActive = false;
                            SaveSettings();
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
                    Serilog.Log.Warning(ex, $"Failed profile generation request using key {keyInfo.DisplayKey}. Rotating key.");
                }

                retryCount++;
            }

            throw new Exception("Profile generation failed after trying all available Groq API keys.");
        }
    }
}
