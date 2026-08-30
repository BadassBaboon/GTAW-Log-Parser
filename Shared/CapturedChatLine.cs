using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GTAWParser.Shared
{
    /// <summary>
    /// Represents an individual colored text segment within a chat line.
    /// </summary>
    public sealed class CapturedChatSpan
    {
        [JsonPropertyName("t")]
        public string Text { get; set; } = string.Empty;

        [JsonPropertyName("c")]
        public string Color { get; set; } = "#FFFFFF";

        public CapturedChatSpan() { }

        public CapturedChatSpan(string text, string color)
        {
            Text = text;
            Color = color;
        }
    }

    /// <summary>
    /// Represents a captured chat line with its raw text, dominant color, and individual colored spans.
    /// </summary>
    public sealed class CapturedChatLine
    {
        [JsonPropertyName("t")]
        public string Text { get; set; } = string.Empty;

        [JsonPropertyName("c")]
        public string DominantColor { get; set; } = "#FFFFFF";

        [JsonPropertyName("s")]
        public List<CapturedChatSpan> Spans { get; set; } = new List<CapturedChatSpan>();

        public CapturedChatLine() { }

        public CapturedChatLine(string text, string dominantColor = "#FFFFFF", List<CapturedChatSpan>? spans = null)
        {
            Text = text;
            DominantColor = dominantColor;
            Spans = spans ?? new List<CapturedChatSpan>();
        }

        private ChatLineCategory? _cachedCategory;

        [JsonIgnore]
        public ChatLineCategory Category
        {
            get
            {
                if (!_cachedCategory.HasValue)
                {
                    _cachedCategory = ChatLineClassifier.Classify(Text);
                }
                return _cachedCategory.Value;
            }
            set => _cachedCategory = value;
        }
    }
}
