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
        public string ActiveModel { get; set; } = "openai/gpt-oss-20b";
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
        public bool PhoneticEnabled { get; set; } = true;
        public bool ActionEnricherEnabled { get; set; } = false;
        public bool AccentEnabled { get; set; } = true;
        public bool TranslateEnabled { get; set; } = true;
        public bool CorrectEnabled { get; set; } = true;
        public bool FiveMOnly { get; set; } = true;
        public double Temperature { get; set; } = 0.6;
        public bool MigratedToCtrlU { get; set; } = false;
    }

    public static class AiAssistantController
    {
        private static readonly string ConfigDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GTAW-Log-Parser",
            "config"
        );
        private static readonly string ConfigFile = Path.Combine(ConfigDir, "ai_settings.json");
        private static readonly HttpClient _httpClient = new HttpClient();
        private static readonly object _settingsLock = new object();

        public static AiAssistantSettings Settings { get; private set; } = new AiAssistantSettings();

        static AiAssistantController()
        {
            LoadSettings();
        }

        public static void LoadSettings()
        {
            lock (_settingsLock)
            {
                try
                {
                    if (!Directory.Exists(ConfigDir))
                    {
                        Directory.CreateDirectory(ConfigDir);
                    }

                    // Check legacy Roaming path if new config file does not exist yet
                    string legacyRoamingDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "GTAWChatLogAssistant"
                    );
                    string legacyRoamingFile = Path.Combine(legacyRoamingDir, "ai_settings.json");

                    if (!File.Exists(ConfigFile) && File.Exists(legacyRoamingFile))
                    {
                        try
                        {
                            File.Copy(legacyRoamingFile, ConfigFile, true);
                            File.Delete(legacyRoamingFile);
                            if (Directory.GetFiles(legacyRoamingDir).Length == 0 && Directory.GetDirectories(legacyRoamingDir).Length == 0)
                            {
                                Directory.Delete(legacyRoamingDir, false);
                            }
                        }
                        catch { }
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
                                SaveSettingsLocked();
                            }
#pragma warning restore CS0618
                            if (!Settings.MigratedToCtrlU)
                            {
                                if (Settings.ShortcutTranslate == "Ctrl+Y")
                                {
                                    Settings.ShortcutTranslate = "Ctrl+U";
                                }
                                Settings.MigratedToCtrlU = true;
                                SaveSettingsLocked();
                            }

                            // Auto-migrate decommissioned Llama 3 models to recommended replacements
                            if (string.Equals(Settings.ActiveModel, "llama-3.1-8b-instant", StringComparison.OrdinalIgnoreCase))
                            {
                                Settings.ActiveModel = "openai/gpt-oss-20b";
                                SaveSettingsLocked();
                            }
                            else if (string.Equals(Settings.ActiveModel, "llama-3.3-70b-versatile", StringComparison.OrdinalIgnoreCase))
                            {
                                Settings.ActiveModel = "openai/gpt-oss-120b";
                                SaveSettingsLocked();
                            }

                            EnsureDefaultProfilesLocked();
                            ResetQuotasIfNeededLocked();
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
                EnsureDefaultProfilesLocked();
                SaveSettingsLocked();
            }
        }

        public static void EnsureDefaultProfiles()
        {
            lock (_settingsLock)
            {
                EnsureDefaultProfilesLocked();
            }
        }

        private static void EnsureDefaultProfilesLocked()
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
                SaveSettingsLocked();
            }
            else if (sopranoProfile.CustomDirectives == null || !sopranoProfile.CustomDirectives.Contains("Vocabulary terms like 'prick'"))
            {
                // Upgrade to the improved directives
                sopranoProfile.CustomDirectives = latestSopranoDirectives;
                SaveSettingsLocked();
            }
        }

        public static void SaveSettings()
        {
            lock (_settingsLock)
            {
                SaveSettingsLocked();
            }
        }

        private static void SaveSettingsLocked()
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
            lock (_settingsLock)
            {
                ResetQuotasIfNeededLocked();
            }
        }

        private static void ResetQuotasIfNeededLocked()
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
                SaveSettingsLocked();
            }
        }

        // Retrieves the next available API key. Performs rotation and quota checking.
        private static GroqApiKeyInfo? GetNextApiKey()
        {
            lock (_settingsLock)
            {
                ResetQuotasIfNeededLocked();

                var availableKeys = Settings.ApiKeys
                    .Where(k => k.IsActive && !k.IsRateLimited && !string.IsNullOrWhiteSpace(k.ApiKey))
                    .OrderBy(k => k.RequestCount) // Pick the one with the fewest requests today
                    .ToList();

                return availableKeys.FirstOrDefault();
            }
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

                string rpQuestionInstruction = "";
                bool isQuestion = actPayload.Trim().EndsWith("?") || Regex.IsMatch(actPayload.Trim(), @"^(what|is|does|would|can|where|how|who|which|are)\b", RegexOptions.IgnoreCase);
                if (isQuestion)
                {
                    rpQuestionInstruction = "CRITICAL ROLEPLAY QUESTION RULE: The input is a roleplay question to another player (asking what is observed, what would be found, or if something is possible). DO NOT answer the question! DO NOT invent imaginary items, answers, or first-person responses! Instead, enrich and refine the ROLEPLAY QUESTION ITSELF so it remains a clear, atmospheric question ending with a question mark (?).\n";
                }

                systemPrompt = "Enrich the roleplay action description to make it vivid, atmospheric, detailed, and expressive.\n" +
                               "RULES:\n" +
                               prefixCasingInstruction +
                               rpQuestionInstruction +
                               "1. Preserve the underlying action, intent, and scene context.\n" +
                               "2. Use standard English spelling and grammar (do NOT apply accent phonetics or slang to action descriptions).\n" +
                               "3. Return ONLY the enriched action description.\n" +
                               "4. Do not include conversational preambles, explanations, or quotes.\n" +
                               "5. DO NOT use em-dashes (— or --).\n" +
                               "7. Always end the action description with proper sentence-ending punctuation (a period '.', '?', or '!').\n" +
                               "8. CRITICAL ANTI-HALLUCINATION RULE: DO NOT invent unstated physical details, injuries, bloodstains, damage, clothing conditions (e.g., 'tattered', 'blood-stained', 'worn'), specific objects, or unstated facts not present in the original input. Only refine the phrasing, vocabulary, structure, and atmospheric delivery of the EXACT facts and items provided.\n" +
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
                }                string commandSpecificRule = "";
                string cmdLowerPrefix = commandPrefix.Trim().ToLower();
                if (AiTextSanitizer.IsMeCommand(cmdLowerPrefix))
                {
                    commandSpecificRule = "CRITICAL /me FORMATTING RULE: The text is a GTA World roleplay action command (/me) that automatically follows a player name in game (e.g. '* PlayerName <action>'). The output MUST begin directly with a lowercase present-tense verb (e.g. 'pulls out...', 'steps back...', 'glances around...'). DO NOT include third-person pronouns ('He', 'She', 'They', 'The man') or character names at the beginning.\n";
                }
                else if (AiTextSanitizer.IsMyCommand(cmdLowerPrefix))
                {
                    commandSpecificRule = "CRITICAL /my FORMATTING RULE: The text is a GTA World roleplay action command (/my) that follows a player name possessive in game (e.g. '* PlayerName's <body part/item>'). The output MUST begin directly with a lowercase noun or body part (e.g. 'eyes widen...', 'hands tremble...'). DO NOT start with 'His', 'Her', 'Their', or 'The'.\n";
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

                    systemPrompt = $"Rewrite the text in the requested style.\n" +
                                   $"RULES:\n" +
                                   commandSpecificRule +
                                   $"DO NOT write conversational replies.\n" +
                                   $"DO NOT start with 'Whaddaya mean' or caricature phrases like 'Fuggedaboutit'.\n" +
                                   $"DO NOT add introductory questions or conversational preambles (e.g. 'You're telling me', 'Are you saying', 'Listen here') unless they correspond directly to words in the original text.\n" +
                                   $"DO NOT use curly apostrophes or smart quotes (such as '’', '‘', '“', '”'). Always use standard ASCII straight apostrophes (') and straight quotes (\").\n" +
                                   $"DO NOT use em-dashes (— or --) or en-dashes (–).\n" +
                                   $"Paraphrase deeply to match how the character would express the underlying thought in a realistic conversation.\n" +
                                   $"Use natural profanity or complaints (like headaches/stress) if it fits.\n" +
                                   constraintRules + "\n" +
                                   phoneticInstruction + "\n" +
                                   profileDirectives + "\n" +
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

                    systemPrompt = $"Translate the text into the requested language.\n" +
                                   $"RULES:\n" +
                                   commandSpecificRule +
                                   $"Return ONLY the translation.\n" +
                                   $"DO NOT use curly apostrophes or smart quotes (such as '’', '‘', '“', '”'). Always use standard ASCII straight apostrophes (') and straight quotes (\").\n" +
                                   $"DO NOT use em-dashes (— or --) or en-dashes (–).\n" +
                                   $"Do not explain or add commentary.\n" +
                                   constraintRules + "\n" +
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

                    systemPrompt = $"Correct grammar and spelling errors in the text while keeping tone and style identical.\n" +
                                   $"RULES:\n" +
                                   commandSpecificRule +
                                   $"Return ONLY the corrected text.\n" +
                                   $"If there are no errors, return the text exactly as-is.\n" +
                                   $"DO NOT use curly apostrophes or smart quotes (such as '’', '‘', '“', '”'). Always use standard ASCII straight apostrophes (') and straight quotes (\").\n" +
                                   $"DO NOT use em-dashes (— or --) or en-dashes (–).\n" +
                                   constraintRules + "\n" +
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

                        var response = await _httpClient.SendAsync(request).ConfigureAwait(false);

                        if (response.StatusCode == System.Net.HttpStatusCode.OK)
                        {
                            string responseJson = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                            using (var doc = JsonDocument.Parse(responseJson))
                            {
                                var content = doc.RootElement
                                    .GetProperty("choices")[0]
                                    .GetProperty("message")
                                    .GetProperty("content")
                                    .GetString();

                                if (content != null)
                                {
                                    lock (_settingsLock)
                                    {
                                        keyInfo.RequestCount++;
                                        keyInfo.LastUsedDate = DateTime.Today;
                                        SaveSettingsLocked();
                                    }

                                    string cleanedResult = AiTextSanitizer.SanitizeResult(content, commandPrefix, textToProcess);
                                    if (string.IsNullOrWhiteSpace(cleanedResult))
                                    {
                                        return text;
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
                    string modelToUse = !string.IsNullOrEmpty(Settings.ActiveModel) && (Settings.ActiveModel.Contains("20b") || Settings.ActiveModel.Contains("8b"))
                        ? "openai/gpt-oss-120b"
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

                        var response = await _httpClient.SendAsync(request).ConfigureAwait(false);

                        if (response.StatusCode == System.Net.HttpStatusCode.OK)
                        {
                            string responseJson = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                            using (var doc = JsonDocument.Parse(responseJson))
                            {
                                var content = doc.RootElement
                                    .GetProperty("choices")[0]
                                    .GetProperty("message")
                                    .GetProperty("content")
                                    .GetString();

                                if (content != null)
                                {
                                    lock (_settingsLock)
                                    {
                                        keyInfo.RequestCount++;
                                        keyInfo.LastUsedDate = DateTime.Today;
                                        SaveSettingsLocked();
                                    }

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
