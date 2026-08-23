using System;
using System.Collections.Generic;
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
        private readonly List<string> _rawLines = new List<string>();
        private readonly FlowDocument _document = new FlowDocument();
        private readonly List<TextRange> _searchMatches = new List<TextRange>();
        private int _currentMatchIndex = -1;
        private bool _isWatching;
        private bool _isInitialized;

        // Frozen Brushes for performance
        private static readonly SolidColorBrush TimestampBrush = FreezeBrush("#7F8C8D");
        private static readonly SolidColorBrush DefaultTextBrush = FreezeBrush("#DCDCDC");
        private static readonly SolidColorBrush SearchMatchBgBrush = FreezeBrush("#665500");
        private static readonly SolidColorBrush SearchActiveMatchBgBrush = FreezeBrush("#E67E22");
        private static readonly SolidColorBrush SearchActiveMatchFgBrush = FreezeBrush("#FFFFFF");

        private static readonly Dictionary<ChatLineCategory, SolidColorBrush> CategoryBrushes = new Dictionary<ChatLineCategory, SolidColorBrush>
        {
            { ChatLineCategory.Emote,      FreezeBrush(ChatLineClassifier.GetHexColor(ChatLineCategory.Emote)) },
            { ChatLineCategory.Action,     FreezeBrush(ChatLineClassifier.GetHexColor(ChatLineCategory.Action)) },
            { ChatLineCategory.ICSpeech,   FreezeBrush(ChatLineClassifier.GetHexColor(ChatLineCategory.ICSpeech)) },
            { ChatLineCategory.ICWhisper,  FreezeBrush(ChatLineClassifier.GetHexColor(ChatLineCategory.ICWhisper)) },
            { ChatLineCategory.ICShout,    FreezeBrush(ChatLineClassifier.GetHexColor(ChatLineCategory.ICShout)) },
            { ChatLineCategory.OOC,        FreezeBrush(ChatLineClassifier.GetHexColor(ChatLineCategory.OOC)) },
            { ChatLineCategory.PM,         FreezeBrush(ChatLineClassifier.GetHexColor(ChatLineCategory.PM)) },
            { ChatLineCategory.Radio,      FreezeBrush(ChatLineClassifier.GetHexColor(ChatLineCategory.Radio)) },
            { ChatLineCategory.Ads,        FreezeBrush(ChatLineClassifier.GetHexColor(ChatLineCategory.Ads)) },
            { ChatLineCategory.Phone,      FreezeBrush(ChatLineClassifier.GetHexColor(ChatLineCategory.Phone)) },
            { ChatLineCategory.SystemInfo, FreezeBrush(ChatLineClassifier.GetHexColor(ChatLineCategory.SystemInfo)) },
            { ChatLineCategory.Default,    DefaultTextBrush }
        };

        private static SolidColorBrush FreezeBrush(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
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
            FiveMChatCaptureService.LineReceived += OnLineReceived;
            _isWatching = true;

            // Load existing chat from current session
            string existing = FiveMChatCaptureService.ReadCapturedChat(false);
            _rawLines.Clear();

            if (!string.IsNullOrWhiteSpace(existing))
            {
                string[] lines = existing.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string l in lines)
                {
                    string trimmed = l.Trim();
                    if (!string.IsNullOrWhiteSpace(trimmed))
                    {
                        _rawLines.Add(trimmed);
                    }
                }
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
                FiveMChatCaptureService.LineReceived -= OnLineReceived;
                _isWatching = false;
            }

            ToggleWatch.Content = "Start";
            StatusLabel.Content = "Stopped";
            StatusLabel.Foreground = Brushes.Gray;

            Log.Information("Live tail stopped");
        }

        private void OnLineReceived(string line)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                string trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed))
                    return;

                _rawLines.Add(trimmed);
                if (_rawLines.Count > MaxLineBuffer)
                {
                    _rawLines.RemoveAt(0);
                    if (_document.Blocks.FirstBlock != null)
                    {
                        _document.Blocks.Remove(_document.Blocks.FirstBlock);
                    }
                }

                Paragraph p = CreateLineParagraph(trimmed, RemoveTimestamps.IsChecked == true, ColoredText.IsChecked == true);
                _document.Blocks.Add(p);

                UpdateCounter();

                if (!string.IsNullOrWhiteSpace(SearchBox.Text))
                {
                    ExecuteSearch();
                }

                if (AutoScroll.IsChecked == true)
                {
                    TailRich.ScrollToEnd();
                }
            }));
        }

        private Paragraph CreateLineParagraph(string rawLine, bool hideTimestamps, bool useColors)
        {
            Paragraph p = new Paragraph
            {
                Margin = new Thickness(0, 1, 0, 1),
                LineHeight = 16
            };

            var (timestamp, content) = ChatLineClassifier.SplitTimestamp(rawLine);

            if (!hideTimestamps && !string.IsNullOrEmpty(timestamp))
            {
                Run tsRun = new Run(timestamp)
                {
                    Foreground = TimestampBrush
                };
                p.Inlines.Add(tsRun);
            }

            ChatLineCategory category = ChatLineClassifier.Classify(content);
            SolidColorBrush textBrush = useColors && CategoryBrushes.TryGetValue(category, out var brush)
                ? brush
                : DefaultTextBrush;

            Run contentRun = new Run(content)
            {
                Foreground = textBrush
            };
            p.Inlines.Add(contentRun);

            return p;
        }

        private void RebuildDocument()
        {
            if (!_isInitialized || _document == null)
                return;

            _document.Blocks.Clear();
            bool hideTimestamps = RemoveTimestamps?.IsChecked == true;
            bool useColors = ColoredText?.IsChecked == true;

            foreach (string line in _rawLines)
            {
                Paragraph p = CreateLineParagraph(line, hideTimestamps, useColors);
                _document.Blocks.Add(p);
            }

            UpdateCounter();

            if (SearchBox != null && !string.IsNullOrWhiteSpace(SearchBox.Text))
            {
                ExecuteSearch();
            }

            if (AutoScroll?.IsChecked == true)
            {
                TailRich?.ScrollToEnd();
            }
        }

        private void UpdateCounter()
        {
            if (Counter == null)
                return;

            int lineCount = _rawLines.Count;
            int charCount = 0;
            for (int i = 0; i < _rawLines.Count; i++)
            {
                charCount += _rawLines[i].Length;
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
            _rawLines.Clear();
            _document.Blocks.Clear();
            ClearHighlights();
            _searchMatches.Clear();
            _currentMatchIndex = -1;
            if (SearchMatchCount != null) SearchMatchCount.Text = "0/0";
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
            ExecuteSearch();
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
            SearchBox.Text = string.Empty;
            SearchBox.Focus();
        }

        private void ClearHighlights()
        {
            foreach (TextRange match in _searchMatches)
            {
                try
                {
                    match.ApplyPropertyValue(TextElement.BackgroundProperty, Brushes.Transparent);
                }
                catch { }
            }
        }

        private void ExecuteSearch()
        {
            ClearHighlights();
            _searchMatches.Clear();
            _currentMatchIndex = -1;

            string query = SearchBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(query))
            {
                SearchMatchCount.Text = "0/0";
                SearchPrevBtn.IsEnabled = false;
                SearchNextBtn.IsEnabled = false;
                return;
            }

            TextPointer position = _document.ContentStart;
            while (position != null && position.CompareTo(_document.ContentEnd) < 0)
            {
                if (position.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
                {
                    string textRun = position.GetTextInRun(LogicalDirection.Forward);
                    int matchIndex = textRun.IndexOf(query, StringComparison.OrdinalIgnoreCase);

                    while (matchIndex >= 0)
                    {
                        TextPointer start = position.GetPositionAtOffset(matchIndex);
                        TextPointer end = position.GetPositionAtOffset(matchIndex + query.Length);

                        if (start != null && end != null)
                        {
                            TextRange matchRange = new TextRange(start, end);
                            matchRange.ApplyPropertyValue(TextElement.BackgroundProperty, SearchMatchBgBrush);
                            _searchMatches.Add(matchRange);
                        }

                        matchIndex = textRun.IndexOf(query, matchIndex + query.Length, StringComparison.OrdinalIgnoreCase);
                    }
                }

                position = position.GetNextContextPosition(LogicalDirection.Forward);
            }

            if (_searchMatches.Count > 0)
            {
                _currentMatchIndex = 0;
                HighlightActiveMatch();
                SearchMatchCount.Text = $"1 of {_searchMatches.Count}";
                SearchPrevBtn.IsEnabled = true;
                SearchNextBtn.IsEnabled = true;
            }
            else
            {
                SearchMatchCount.Text = "No matches";
                SearchPrevBtn.IsEnabled = false;
                SearchNextBtn.IsEnabled = false;
            }
        }

        private void NavigateSearch(int direction)
        {
            if (_searchMatches.Count == 0)
                return;

            // Reset current active highlight
            if (_currentMatchIndex >= 0 && _currentMatchIndex < _searchMatches.Count)
            {
                _searchMatches[_currentMatchIndex].ApplyPropertyValue(TextElement.BackgroundProperty, SearchMatchBgBrush);
            }

            _currentMatchIndex = (_currentMatchIndex + direction + _searchMatches.Count) % _searchMatches.Count;
            HighlightActiveMatch();
            SearchMatchCount.Text = $"{_currentMatchIndex + 1} of {_searchMatches.Count}";
        }

        private void HighlightActiveMatch()
        {
            if (_currentMatchIndex < 0 || _currentMatchIndex >= _searchMatches.Count)
                return;

            TextRange active = _searchMatches[_currentMatchIndex];
            active.ApplyPropertyValue(TextElement.BackgroundProperty, SearchActiveMatchBgBrush);

            try
            {
                TailRich.Selection.Select(active.Start, active.End);
                var charRect = active.Start.GetCharacterRect(LogicalDirection.Forward);
                TailRich.ScrollToVerticalOffset(TailRich.VerticalOffset + charRect.Top - 50);
            }
            catch { }
        }
    }
}
