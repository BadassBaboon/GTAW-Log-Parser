#if COLOR_AUDIT
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GTAWParser.Shared;
using Xunit;
using Xunit.Abstractions;

namespace GTAWParser.Shared.Tests
{
    /// <summary>
    /// Diffs the classifier's inferred colours against colours captured from the live game.
    /// Run manually with: dotnet test -p:DefineConstants=COLOR_AUDIT
    /// </summary>
    public class ColorAuditHarness
    {
        private readonly ITestOutputHelper _out;
        public ColorAuditHarness(ITestOutputHelper o) => _out = o;

        private sealed class Span { public string t { get; set; } = ""; public string c { get; set; } = ""; }
        private sealed class Line { public string text { get; set; } = ""; public List<Span> spans { get; set; } = new(); }

        [Fact]
        public void Audit()
        {
            string? path = Environment.GetEnvironmentVariable("AUDIT_JSON");
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                _out.WriteLine("Set AUDIT_JSON to a capture file to run this audit.");
                return;
            }

            var lines = JsonSerializer.Deserialize<List<Line>>(File.ReadAllText(path))!;

            int match = 0, mismatch = 0;
            foreach (var line in lines)
            {
                var (_, content) = ChatLineClassifier.SplitTimestamp(line.text);
                var got = ChatLineClassifier.ParseSpans(content);

                // Compare dominant colours: first non-white span on each side.
                string Dom(IEnumerable<(string t, string c)> s) =>
                    s.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.c)
                        && !x.c.Equals("#FFFFFF", StringComparison.OrdinalIgnoreCase)
                        && !x.c.Equals("#F0F0F0", StringComparison.OrdinalIgnoreCase)).c ?? "#FFFFFF";

                string real = Dom(line.spans.Select(s => (s.t, s.c)));
                string mine = Dom(got.Select(s => (s.Text, s.Color)));

                if (string.Equals(real, mine, StringComparison.OrdinalIgnoreCase)) { match++; continue; }

                mismatch++;
                _out.WriteLine($"MISMATCH real={real} mine={mine} cat={ChatLineClassifier.Classify(content)}");
                _out.WriteLine($"   {content.Substring(0, Math.Min(110, content.Length))}");
            }

            _out.WriteLine($"\n=== {match} match, {mismatch} mismatch of {lines.Count} ===");
        }
    }
}
#endif
