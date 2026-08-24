using System;
using System.Text.RegularExpressions;

namespace GTAWParser.Shared
{
    public static class AiTextSanitizer
    {
        public static bool IsMeCommand(string cmdLower) =>
            cmdLower.StartsWith("/me") || cmdLower.StartsWith("/ame") || cmdLower.StartsWith("/melow") || cmdLower.StartsWith("/melong");

        public static bool IsMyCommand(string cmdLower) =>
            cmdLower.StartsWith("/my") || cmdLower.StartsWith("/amy") || cmdLower.StartsWith("/mylow") || cmdLower.StartsWith("/mylong");

        /// <summary>
        /// Normalizes typographic smart quotes, curly apostrophes, and dashes to standard ASCII characters.
        /// </summary>
        public static string NormalizeTypography(string? input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            return input
                .Replace('’', '\'')
                .Replace('‘', '\'')
                .Replace('`', '\'')
                .Replace('´', '\'')
                .Replace('ʻ', '\'')
                .Replace('ʼ', '\'')
                .Replace('“', '"')
                .Replace('”', '"')
                .Replace('„', '"')
                .Replace('‟', '"')
                .Replace('«', '"')
                .Replace('»', '"')
                .Replace('—', ' ')
                .Replace('–', ' ')
                .Replace('―', ' ');
        }

        /// <summary>
        /// Cleans, sanitizes, and strips third-person redundancy from AI-generated chat responses.
        /// </summary>
        public static string SanitizeResult(string content, string commandPrefix, string originalTextToProcess)
        {
            if (string.IsNullOrWhiteSpace(content))
                return string.Empty;

            string cleaned = content.Trim();

            // 1. Strip reasoning/thinking tags (<think>...</think>) from reasoning models
            cleaned = Regex.Replace(cleaned, @"<think>[\s\S]*?</think>", string.Empty).Trim();
            if (cleaned.Contains("<think>"))
            {
                int idx = cleaned.IndexOf("<think>");
                cleaned = cleaned.Substring(0, idx).Trim();
            }

            // 2. Normalize smart quotes, curly apostrophes, and dashes to standard ASCII
            cleaned = NormalizeTypography(cleaned);

            // 3. Remove enclosing quotes if the model wrapped the response in quotes
            if ((cleaned.StartsWith("\"") && cleaned.EndsWith("\"")) ||
                (cleaned.StartsWith("'") && cleaned.EndsWith("'")))
            {
                cleaned = cleaned.Substring(1, cleaned.Length - 2).Trim();
            }

            if (string.IsNullOrWhiteSpace(cleaned))
                return string.Empty;

            string lowerCmd = (commandPrefix ?? string.Empty).Trim().ToLower();

            // 4. Handle /me and /my third-person pronoun stripping
            if (IsMeCommand(lowerCmd))
            {
                // Strip leading third-person pronouns or noun phrases like "He ", "She ", "They ", "The man "
                cleaned = Regex.Replace(
                    cleaned,
                    @"^(?:he|she|they|the\s+(?:man|woman|guy|girl|figure|person|character|individual))\s+",
                    string.Empty,
                    RegexOptions.IgnoreCase
                ).Trim();

                if (cleaned.Length > 0 && char.IsUpper(cleaned[0]))
                {
                    cleaned = char.ToLower(cleaned[0]) + cleaned.Substring(1);
                }
            }
            else if (IsMyCommand(lowerCmd))
            {
                // Strip leading possessive pronouns like "His ", "Her ", "Their ", "The "
                cleaned = Regex.Replace(
                    cleaned,
                    @"^(?:his|her|their|the)\s+",
                    string.Empty,
                    RegexOptions.IgnoreCase
                ).Trim();

                if (cleaned.Length > 0 && char.IsUpper(cleaned[0]))
                {
                    cleaned = char.ToLower(cleaned[0]) + cleaned.Substring(1);
                }
            }

            // 5. Enforce finishing punctuation for action commands
            if (!string.IsNullOrWhiteSpace(commandPrefix))
            {
                if (originalTextToProcess.Trim().EndsWith("?") && !cleaned.EndsWith("?"))
                {
                    if (cleaned.EndsWith("."))
                        cleaned = cleaned.Substring(0, cleaned.Length - 1) + "?";
                    else
                        cleaned += "?";
                }
                else if (cleaned.Length > 0 && !cleaned.EndsWith(".") && !cleaned.EndsWith("?") && !cleaned.EndsWith("!"))
                {
                    cleaned += ".";
                }
            }

            return cleaned;
        }
    }
}
