using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Assistant.Controllers;
using GTAWParser.Shared;
using Serilog;

namespace Assistant.UI
{
    /// <summary>
    /// Displays live streaming chat lines with roleplay color-coding,
    /// inline search bar with match stepping, and auto-scroll.
    /// </summary>
    public partial class LiveTailWindow
    {
        private const int MaxLineBuffer = 5000;
        private readonly List<CapturedChatLine> _capturedLines = new List<CapturedChatLine>();
        private readonly FlowDocument _document = new FlowDocument();
        private readonly List<Run> _searchMatchRuns = new List<Run>();
        private int _currentMatchIndex = -1;
        private bool _isWatching;
        private bool _isInitialized;

        // Frozen Brushes for performance
        private static readonly SolidColorBrush TimestampBrush = FreezeBrush("#7F8C8D");
        private static readonly SolidColorBrush DefaultTextBrush = FreezeBrush("#FFFFFF");
        private static readonly SolidColorBrush SearchMatchBgBrush = FreezeBrush("#665500");
        private static readonly SolidColorBrush SearchActiveMatchBgBrush = FreezeBrush("#E67E22");
        private static readonly SolidColorBrush SearchActiveMatchFgBrush = FreezeBrush("#FFFFFF");

        private static readonly ConcurrentDictionary<string, SolidColorBrush> DynamicBrushCache =
            new ConcurrentDictionary<string, SolidColorBrush>(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<ChatLineCategory, SolidColorBrush> CategoryBrushes = new Dictionary<ChatLineCategory, SolidColorBrush>
        {
            { ChatLineCategory.Emote,         FreezeBrush(ChatLineClassifier.GetHexColor(ChatLineCategory.Emote)) },
            { ChatLineCategory.Action,        FreezeBrush(ChatLineClassifier.GetHexColor(ChatLineCategory.Action)) },
            { ChatLineCategory.ICSpeech,      FreezeBrush(ChatLineClassifier.GetHexColor(ChatLineCategory.ICSpeech)) },
            { ChatLineCategory.ICWhisper,     FreezeBrush(ChatLineClassifier.GetHexColor(ChatLineCategory.ICWhisper)) },
            { ChatLineCategory.ICShout,       FreezeBrush(ChatLineClassifier.GetHexColor(ChatLineCategory.ICShout)) },
            { ChatLineCategory.OOC,           FreezeBrush(ChatLineClassifier.GetHexColor(ChatLineCategory.OOC)) },
            { ChatLineCategory.PM,            FreezeBrush(ChatLineClassifier.GetHexColor(ChatLineCategory.PM)) },
            { ChatLineCategory.Radio,         FreezeBrush(ChatLineClassifier.GetHexColor(ChatLineCategory.Radio)) },
            { ChatLineCategory.Ads,           FreezeBrush(ChatLineClassifier.GetHexColor(ChatLineCategory.Ads)) },
            { ChatLineCategory.Phone,         FreezeBrush(ChatLineClassifier.GetHexColor(ChatLineCategory.Phone)) },
            { ChatLineCategory.SystemInfo,    FreezeBrush(ChatLineClassifier.GetHexColor(ChatLineCategory.SystemInfo)) },
            { ChatLineCategory.Success,       FreezeBrush(ChatLineClassifier.GetHexColor(ChatLineCategory.Success)) },
            { ChatLineCategory.Warning,       FreezeBrush(ChatLineClassifier.GetHexColor(ChatLineCategory.Warning)) },
            { ChatLineCategory.Error,         FreezeBrush(ChatLineClassifier.GetHexColor(ChatLineCategory.Error)) },
            { ChatLineCategory.SessionHeader, FreezeBrush(ChatLineClassifier.GetHexColor(ChatLineCategory.SessionHeader)) },
            { ChatLineCategory.Default,       DefaultTextBrush }
        };

        private static SolidColorBrush FreezeBrush(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }

        private static SolidColorBrush GetOrCreateBrush(string? hex)
        {
            if (string.IsNullOrWhiteSpace(hex))
                return DefaultTextBrush;

            string cleanHex = hex.Trim();
            return DynamicBrushCache.GetOrAdd(cleanHex, h =>
            {
                try
                {
                    var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(h));
                    brush.Freeze();
                    return brush;
                }
                catch
                {
                    return DefaultTextBrush;
                }
            });
        }

        public LiveTailWindow()
        {
            InitializeComponent();
            _document.PagePadding = new Thickness(0);
            TailRich.Document = _document;
            _isInitialized = true;
        }

        private void LiveTail_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_isWatching)
            {
                StartWatching();
            }
        }

        private void LiveTail_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            StopWatching();
        }

        private void ToggleWatch_Click(object sender, RoutedEventArgs e)
        {
            if (!_isWatching)
                StartWatching();
            else
                StopWatching();
        }

        private void StartWatching()
        {
            if (_isWatching)
                return;

            FiveMChatCaptureService.Initialize();
            FiveMChatCaptureService.CapturedLineReceived += OnCapturedLineReceived;
            _isWatching = true;

            // Load existing chat from current session
            IReadOnlyList<CapturedChatLine> richLines = FiveMChatCaptureService.SessionRichLines;
            _capturedLines.Clear();

            if (richLines != null && richLines.Count > 0)
            {
                foreach (CapturedChatLine line in richLines)
                {
                    _capturedLines.Add(line);
                }
            }
            else
            {
                // Fallback to reading disk file if buffer is empty
                string existing = FiveMChatCaptureService.ReadCapturedChat(false);
                if (!string.IsNullOrWhiteSpace(existing))
                {
                    string[] lines = existing.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (string l in lines)
                    {
                        string trimmed = l.Trim();
                        if (!string.IsNullOrWhiteSpace(trimmed))
                        {
                            _capturedLines.Add(new CapturedChatLine(trimmed));
                        }
                    }
                }
            }

            if (_capturedLines.Count > MaxLineBuffer)
            {
                _capturedLines.RemoveRange(0, _capturedLines.Count - MaxLineBuffer);
            }

            RebuildDocument();

            ToggleWatch.Content = "Stop";
            StatusLabel.Content = "Live Capture Active";
            StatusLabel.Foreground = Brushes.Green;

            Log.Information("Live tail started on FiveMChatCaptureService");
        }

        private void StopWatching()
        {
            if (_isWatching)
            {
                FiveMChatCaptureService.CapturedLineReceived -= OnCapturedLineReceived;
                _isWatching = false;
            }

            ToggleWatch.Content = "Start";
            StatusLabel.Content = "Stopped";
            StatusLabel.Foreground = Brushes.Gray;

            Log.Information("Live tail stopped");
        }

        private void OnCapturedLineReceived(CapturedChatLine line)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (line == null || string.IsNullOrWhiteSpace(line.Text))
                    return;

                _capturedLines.Add(line);
                if (_capturedLines.Count > MaxLineBuffer)
                {
                    _capturedLines.RemoveAt(0);
                    if (_document.Blocks.FirstBlock != null)
                    {
                        _document.Blocks.Remove(_document.Blocks.FirstBlock);
                    }
                }

                string query = SearchBox?.Text?.Trim() ?? string.Empty;
                Paragraph p = CreateLineParagraph(line, RemoveTimestamps?.IsChecked == true, ColoredText?.IsChecked == true, query, _searchMatchRuns);
                _document.Blocks.Add(p);

                UpdateCounter();

                if (!string.IsNullOrEmpty(query))
                {
                    if (_searchMatchRuns.Count > 0)
                    {
                        if (_currentMatchIndex < 0) _currentMatchIndex = 0;
                        if (SearchMatchCount != null) SearchMatchCount.Text = $"{_currentMatchIndex + 1} of {_searchMatchRuns.Count}";
                        if (SearchPrevBtn != null) SearchPrevBtn.IsEnabled = true;
                        if (SearchNextBtn != null) SearchNextBtn.IsEnabled = true;
                    }
                    else
                    {
                        if (SearchMatchCount != null) SearchMatchCount.Text = "No matches";
                        if (SearchPrevBtn != null) SearchPrevBtn.IsEnabled = false;
                        if (SearchNextBtn != null) SearchNextBtn.IsEnabled = false;
                    }
                }

                if (AutoScroll?.IsChecked == true && string.IsNullOrEmpty(query))
                {
                    TailRich?.ScrollToEnd();
                }
            }));
        }

        private Paragraph CreateLineParagraph(
            CapturedChatLine line,
            bool hideTimestamps,
            bool useColors,
            string? searchQuery = null,
            List<Run>? searchMatchRuns = null)
        {
            Paragraph p = new Paragraph
            {
                Margin = new Thickness(0, 1, 0, 1),
                LineHeight = 16
            };

            var (timestamp, content) = ChatLineClassifier.SplitTimestamp(line.Text);

            if (!hideTimestamps && !string.IsNullOrEmpty(timestamp))
            {
                AddInlineWithSearchHighlight(p, timestamp, TimestampBrush, searchQuery, searchMatchRuns);
            }

            if (!useColors)
            {
                AddInlineWithSearchHighlight(p, content, DefaultTextBrush, searchQuery, searchMatchRuns);
                return p;
            }

            // Per-span coloring if NUI DOM spans are available AND carry at least one non-white color.
            bool nuiHasColor = line.Spans != null && line.Spans.Count > 0 &&
                line.Spans.Any(s => !string.IsNullOrEmpty(s.Color) &&
                    !string.Equals(s.Color, "#FFFFFF", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(s.Color, "#DCDCDC", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(s.Color, "#F0F0F0", StringComparison.OrdinalIgnoreCase));

            if (nuiHasColor)
            {
                bool firstSpan = true;
                int runsAdded = 0;

                foreach (CapturedChatSpan span in line.Spans!)
                {
                    if (string.IsNullOrEmpty(span.Text))
                        continue;

                    string spanText = span.Text;
                    if (firstSpan)
                    {
                        firstSpan = false;
                        if (!string.IsNullOrEmpty(timestamp) && spanText.StartsWith(timestamp, StringComparison.Ordinal))
                        {
                            spanText = spanText.Substring(timestamp.Length);
                        }
                        else if (spanText.StartsWith("[") && spanText.IndexOf(']') > 0)
                        {
                            int endBracket = spanText.IndexOf(']');
                            string potentialTs = spanText.Substring(0, endBracket + 1);
                            if (ChatLineClassifier.SplitTimestamp(potentialTs + " ").Timestamp.Length > 0)
                            {
                                spanText = spanText.Substring(endBracket + 1).TrimStart(' ');
                            }
                        }
                    }

                    if (string.IsNullOrEmpty(spanText))
                        continue;

                    SolidColorBrush spanBrush = GetOrCreateBrush(span.Color);
                    AddInlineWithSearchHighlight(p, spanText, spanBrush, searchQuery, searchMatchRuns);
                    runsAdded++;
                }

                if (runsAdded > 0)
                    return p;
            }

            // Dominant color if provided and not default
            if (!string.IsNullOrWhiteSpace(line.DominantColor) &&
                !string.Equals(line.DominantColor, "#FFFFFF", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(line.DominantColor, "#DCDCDC", StringComparison.OrdinalIgnoreCase))
            {
                SolidColorBrush dominantBrush = GetOrCreateBrush(line.DominantColor);
                AddInlineWithSearchHighlight(p, content, dominantBrush, searchQuery, searchMatchRuns);
                return p;
            }

            // Fallback: Rich per-span pattern parsing
            List<CapturedChatSpan> fallbackSpans = ChatLineClassifier.ParseSpans(content);
            if (fallbackSpans != null && fallbackSpans.Count > 0)
            {
                foreach (CapturedChatSpan span in fallbackSpans)
                {
                    if (string.IsNullOrEmpty(span.Text))
                        continue;

                    SolidColorBrush spanBrush = GetOrCreateBrush(span.Color);
                    AddInlineWithSearchHighlight(p, span.Text, spanBrush, searchQuery, searchMatchRuns);
                }
                return p;
            }

            AddInlineWithSearchHighlight(p, content, DefaultTextBrush, searchQuery, searchMatchRuns);
            return p;
        }

        private void AddInlineWithSearchHighlight(
            Paragraph paragraph,
            string text,
            SolidColorBrush foreground,
            string? searchQuery,
            List<Run>? searchMatchRuns)
        {
            if (string.IsNullOrEmpty(text))
                return;

            if (string.IsNullOrEmpty(searchQuery))
            {
                paragraph.Inlines.Add(new Run(text) { Foreground = foreground });
                return;
            }

            int idx = text.IndexOf(searchQuery, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                paragraph.Inlines.Add(new Run(text) { Foreground = foreground });
                return;
            }

            int lastIdx = 0;
            while (idx >= 0)
            {
                if (idx > lastIdx)
                {
                    string before = text.Substring(lastIdx, idx - lastIdx);
                    paragraph.Inlines.Add(new Run(before) { Foreground = foreground });
                }

                string matchText = text.Substring(idx, searchQuery.Length);
                Run matchRun = new Run(matchText)
                {
                    Foreground = foreground,
                    Background = SearchMatchBgBrush
                };
                paragraph.Inlines.Add(matchRun);
                searchMatchRuns?.Add(matchRun);

                lastIdx = idx + searchQuery.Length;
                idx = text.IndexOf(searchQuery, lastIdx, StringComparison.OrdinalIgnoreCase);
            }

            if (lastIdx < text.Length)
            {
                string remaining = text.Substring(lastIdx);
                paragraph.Inlines.Add(new Run(remaining) { Foreground = foreground });
            }
        }

        private void RebuildDocument()
        {
            if (!_isInitialized || _document == null)
                return;

            _document.Blocks.Clear();
            _searchMatchRuns.Clear();
            _currentMatchIndex = -1;

            bool hideTimestamps = RemoveTimestamps?.IsChecked == true;
            bool useColors = ColoredText?.IsChecked == true;
            string query = SearchBox?.Text?.Trim() ?? string.Empty;

            foreach (CapturedChatLine line in _capturedLines)
            {
                Paragraph p = CreateLineParagraph(line, hideTimestamps, useColors, query, _searchMatchRuns);
                _document.Blocks.Add(p);
            }

            UpdateCounter();

            if (string.IsNullOrEmpty(query))
            {
                if (SearchMatchCount != null) SearchMatchCount.Text = "0/0";
                if (SearchPrevBtn != null) SearchPrevBtn.IsEnabled = false;
                if (SearchNextBtn != null) SearchNextBtn.IsEnabled = false;
                if (SearchClearBtn != null) SearchClearBtn.IsEnabled = false;
            }
            else
            {
                if (SearchClearBtn != null) SearchClearBtn.IsEnabled = true;

                if (_searchMatchRuns.Count > 0)
                {
                    _currentMatchIndex = 0;
                    HighlightActiveMatch();
                    if (SearchMatchCount != null) SearchMatchCount.Text = $"1 of {_searchMatchRuns.Count}";
                    if (SearchPrevBtn != null) SearchPrevBtn.IsEnabled = true;
                    if (SearchNextBtn != null) SearchNextBtn.IsEnabled = true;
                }
                else
                {
                    if (SearchMatchCount != null) SearchMatchCount.Text = "No matches";
                    if (SearchPrevBtn != null) SearchPrevBtn.IsEnabled = false;
                    if (SearchNextBtn != null) SearchNextBtn.IsEnabled = false;
                }
            }

            if (AutoScroll?.IsChecked == true && string.IsNullOrEmpty(query))
            {
                TailRich?.ScrollToEnd();
            }
        }

        private void UpdateCounter()
        {
            if (Counter == null)
                return;

            int lineCount = _capturedLines.Count;
            int charCount = 0;
            for (int i = 0; i < _capturedLines.Count; i++)
            {
                charCount += _capturedLines[i].Text.Length;
            }
            Counter.Text = $"{charCount:N0} characters and {lineCount:N0} lines";
        }

        private void ColoredText_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            RebuildDocument();
        }

        private void RemoveTimestamps_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            RebuildDocument();
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            _capturedLines.Clear();
            _document.Blocks.Clear();
            _searchMatchRuns.Clear();
            _currentMatchIndex = -1;
            if (SearchMatchCount != null) SearchMatchCount.Text = "0/0";
            if (SearchPrevBtn != null) SearchPrevBtn.IsEnabled = false;
            if (SearchNextBtn != null) SearchNextBtn.IsEnabled = false;
            if (SearchClearBtn != null) SearchClearBtn.IsEnabled = false;
            UpdateCounter();
        }

        private void CopyAllButton_Click(object sender, RoutedEventArgs e)
        {
            TextRange range = new TextRange(_document.ContentStart, _document.ContentEnd);
            if (!string.IsNullOrWhiteSpace(range.Text))
            {
                try
                {
                    Clipboard.SetText(range.Text.TrimEnd('\r', '\n'));
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Failed to copy Live Tail text to clipboard");
                }
            }
        }

        private void TailRich_SelectionChanged(object sender, RoutedEventArgs e)
        {
            // Event hook for user selection
        }

        // ==========================================
        // SEARCH ENGINE & NAVIGATION
        // ==========================================

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isInitialized) return;
            RebuildDocument();
        }

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
                {
                    NavigateSearch(-1);
                }
                else
                {
                    NavigateSearch(1);
                }
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                SearchBox.Text = string.Empty;
                TailRich.Focus();
            }
        }

        private void LiveTail_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                e.Handled = true;
                SearchBox.Focus();
                SearchBox.SelectAll();
            }
            else if (e.Key == Key.F3)
            {
                e.Handled = true;
                if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
                {
                    NavigateSearch(-1);
                }
                else
                {
                    NavigateSearch(1);
                }
            }
            else if (e.Key == Key.Escape)
            {
                if (SearchBox.IsFocused || !string.IsNullOrEmpty(SearchBox.Text))
                {
                    e.Handled = true;
                    SearchBox.Text = string.Empty;
                    TailRich.Focus();
                }
            }
        }

        private void SearchPrevBtn_Click(object sender, RoutedEventArgs e)
        {
            NavigateSearch(-1);
        }

        private void SearchNextBtn_Click(object sender, RoutedEventArgs e)
        {
            NavigateSearch(1);
        }

        private void SearchClearBtn_Click(object sender, RoutedEventArgs e)
        {
            if (SearchBox != null) SearchBox.Text = string.Empty;
            _searchMatchRuns.Clear();
            _currentMatchIndex = -1;
            if (SearchMatchCount != null) SearchMatchCount.Text = "0/0";
            if (SearchPrevBtn != null) SearchPrevBtn.IsEnabled = false;
            if (SearchNextBtn != null) SearchNextBtn.IsEnabled = false;
            if (SearchClearBtn != null) SearchClearBtn.IsEnabled = false;
            RebuildDocument();
            TailRich?.Focus();
        }

        private void NavigateSearch(int direction)
        {
            if (_searchMatchRuns.Count == 0)
                return;

            // Reset current active highlight
            if (_currentMatchIndex >= 0 && _currentMatchIndex < _searchMatchRuns.Count)
            {
                _searchMatchRuns[_currentMatchIndex].Background = SearchMatchBgBrush;
            }

            _currentMatchIndex = (_currentMatchIndex + direction + _searchMatchRuns.Count) % _searchMatchRuns.Count;
            HighlightActiveMatch();
            if (SearchMatchCount != null)
            {
                SearchMatchCount.Text = $"{_currentMatchIndex + 1} of {_searchMatchRuns.Count}";
            }
        }

        private void HighlightActiveMatch()
        {
            if (_currentMatchIndex < 0 || _currentMatchIndex >= _searchMatchRuns.Count)
                return;

            Run active = _searchMatchRuns[_currentMatchIndex];
            active.Background = SearchActiveMatchBgBrush;

            try
            {
                active.BringIntoView();
            }
            catch { }
        }
    }
}
