using System;
using System.Collections.Generic;

namespace GTAWParser.Shared.Screenshot
{
    public class ResolutionPreset
    {
        public string Name { get; set; } = string.Empty;
        public int Width { get; set; }
        public int Height { get; set; }
        public string Category { get; set; } = string.Empty;

        public string DisplayText => $"{Name} ({Width}x{Height})";

        public ResolutionPreset() { }

        public ResolutionPreset(string name, int width, int height, string category)
        {
            Name = name;
            Width = width;
            Height = height;
            Category = category;
        }

        public static List<ResolutionPreset> DefaultPresets => new List<ResolutionPreset>
        {
            // Roleplay Forum Standards
            new ResolutionPreset("Standard RP Thread", 1300, 730, "Community RP"),
            new ResolutionPreset("Compact RP Thread", 1150, 750, "Community RP"),
            new ResolutionPreset("Large RP Thread", 1650, 1060, "Community RP"),

            // Standard 16:9
            new ResolutionPreset("1080p FHD", 1920, 1080, "Standard 16:9"),
            new ResolutionPreset("900p HD+", 1600, 900, "Standard 16:9"),
            new ResolutionPreset("720p HD", 1280, 720, "Standard 16:9"),

            // Widescreen
            new ResolutionPreset("16:10 Widescreen", 1680, 1050, "Widescreen"),
            new ResolutionPreset("16:10 Standard", 1440, 900, "Widescreen"),
            new ResolutionPreset("3:2 Cinematic", 1200, 800, "Widescreen"),

            // Classic 4:3 / 5:4
            new ResolutionPreset("4:3 Classic", 1024, 768, "Classic"),
            new ResolutionPreset("4:3 High", 1280, 960, "Classic"),
            new ResolutionPreset("5:4 Display", 1280, 1024, "Classic"),
            new ResolutionPreset("Legacy RP", 800, 600, "Classic"),

            // Square
            new ResolutionPreset("Square HD", 1080, 1080, "Square"),
            new ResolutionPreset("Square Avatar", 800, 800, "Square")
        };
    }
}
