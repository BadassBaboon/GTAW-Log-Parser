using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Threading;
using Assistant.Controllers;
using GTAWParser.Shared;
using GTAWParser.Shared.Screenshot;
using MahApps.Metro.Controls;
using MahApps.Metro.IconPacks;
using Microsoft.Win32;
using Serilog;

namespace Assistant.UI
{
    /// <summary>
    /// Shared colour/badge presentation for a chat line in either inspector list.
    /// </summary>
    public abstract class ChatLineViewModel : INotifyPropertyChanged
    {
        private string _colorHex = "#FFFFFF";
        private SolidColorBrush _colorBrush = Brushes.White;
        private SolidColorBrush _badgeBackground = Brushes.Transparent;

        public ChatLineCategory Category { get; set; } = ChatLineCategory.Default;

        /// <summary>The colour actually used to draw this line, override included.</summary>
        public string ColorHex
        {
            get => _colorHex;
            set
            {
                if (_colorHex == value) return;
                _colorHex = value;
                RebuildBrushes();
                Raise(nameof(ColorHex));
                Raise(nameof(ColorBrush));
                Raise(nameof(BadgeBackground));
            }
        }

        public SolidColorBrush ColorBrush => _colorBrush;
        public SolidColorBrush BadgeBackground => _badgeBackground;

        public virtual string TypeLabel => ChatLineClassifier.GetShortLabel(Category);

        protected void RebuildBrushes()
        {
            Color c = Colors.White;
            try
            {
                var converted = ColorConverter.ConvertFromString(_colorHex);
                if (converted != null) c = (Color)converted;
            }
            catch { }

            _colorBrush = new SolidColorBrush(c);
            _colorBrush.Freeze();

            _badgeBackground = new SolidColorBrush(Color.FromArgb(38, c.R, c.G, c.B));
            _badgeBackground.Freeze();
        }

        protected void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    /// <summary>A line offered in the source list, waiting to be picked.</summary>
    public class ChatSelectableItem : ChatLineViewModel
    {
        private bool _isSelected;

        public string RawText { get; set; } = string.Empty;
        public string CleanedText { get; set; } = string.Empty;
        public string DisplayText => CleanedText;

        /// <summary>
        /// Explains where this line's colour came from. For an imported log with no captured
        /// colours this is the only signal the user has that the colour was inferred.
        /// </summary>
        public string SourceTooltip =>
            NuiSpans != null
                ? "Colour captured live from the game"
                : $"Detected as {ChatLineClassifier.DescribeCategory(Category)} ({ColorHex})";

        /// <summary>
        /// Colour spans captured straight from the FiveM NUI. When present these are ground truth
        /// and are carried through to the canvas untouched.
        /// </summary>
        public List<CapturedChatSpan>? NuiSpans { get; set; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                Raise(nameof(IsSelected));
            }
        }
    }

    /// <summary>A line placed on the canvas.</summary>
    public class PlacedChatLine : ChatLineViewModel
    {
        private string _rawText = string.Empty;
        private string? _colorOverride;

        public List<CapturedChatSpan>? NuiSpans { get; set; }

        public string RawText
        {
            get => _rawText;
            set
            {
                if (_rawText == value) return;

                // Once the text is edited the captured spans no longer describe it, so they are
                // dropped and the line falls back to classification.
                if (NuiSpans != null && !string.Equals(_rawText, value, StringComparison.Ordinal))
                    NuiSpans = null;

                _rawText = value;
                Reclassify();
                Raise(nameof(RawText));
                Raise(nameof(SourceTooltip));
            }
        }

        /// <summary>A manual colour, or null to use whatever the game/classifier says.</summary>
        public string? ColorOverride
        {
            get => _colorOverride;
            set
            {
                _colorOverride = value;
                Reclassify();
                Raise(nameof(ColorOverride));
                Raise(nameof(SourceTooltip));
            }
        }

        public string SourceTooltip =>
            _colorOverride != null ? $"Custom colour {_colorOverride} (Click square to change)"
            : NuiSpans != null ? "Colour captured live from the game (Click square to change)"
            : $"Detected as {ChatLineClassifier.DescribeCategory(Category)} — {ColorHex} (Click square to change)";

        public void Reclassify()
        {
            Category = RoleplayChatColorizer.Classify(_rawText);
            ColorHex = _colorOverride ?? DominantOf(NuiSpans) ?? ChatLineClassifier.GetHexColor(Category);
        }

        private static string? DominantOf(List<CapturedChatSpan>? spans)
        {
            if (spans == null) return null;
            foreach (var s in spans)
            {
                if (!string.IsNullOrWhiteSpace(s.Color) &&
                    !string.Equals(s.Color, "#FFFFFF", StringComparison.OrdinalIgnoreCase))
                {
                    return s.Color;
                }
            }
            return null;
        }

        /// <summary>Builds the styled segments this line contributes to the render.</summary>
        public List<ChatStyledSegment> ToSegments()
        {
            List<ChatStyledSegment> segments =
                NuiSpans != null && NuiSpans.Count > 0
                    ? RoleplayChatColorizer.FromSpans(NuiSpans)
                    : RoleplayChatColorizer.ColorizeLine(_rawText);

            if (segments.Count == 0) return segments;

            return _colorOverride != null
                ? RoleplayChatColorizer.Recolor(segments, _colorOverride)
                : segments;
        }
    }

    public partial class ScreenshotEditorWindow : MetroWindow
    {
        private BitmapSource? _backgroundImage;
        private double _imageOffsetX;
        private double _imageOffsetY;
        private double _imageScale = 1.0;

        private double _chatX = 30;
        private double _chatY = 30;

        private bool _isDraggingImage;
        private bool _isDraggingChat;
        private Point _dragStartMousePos;
        private Point _dragStartChatPos;
        private Point _dragStartImageOffset;

        private readonly ObservableCollection<ChatSelectableItem> _sourceLines = new ObservableCollection<ChatSelectableItem>();
        private readonly ObservableCollection<ChatSelectableItem> _filteredSourceLines = new ObservableCollection<ChatSelectableItem>();
        private readonly ObservableCollection<PlacedChatLine> _placedLines = new ObservableCollection<PlacedChatLine>();

        /// <summary>Where the chat block sits on the canvas, from the last render.</summary>
        private Rect _chatBlockRect = Rect.Empty;

        private bool _isLoaded;
        private bool _isUpdatingUi;

        public ScreenshotEditorWindow()
        {
            InitializeComponent();

            ChatLinesListBox.ItemsSource = _filteredSourceLines;
            PlacedLinesListBox.ItemsSource = _placedLines;
            _placedLines.CollectionChanged += (_, __) => UpdatePlacedCount();

            Loaded += ScreenshotEditorWindow_Loaded;
            Closing += (_, __) => SaveSettings();
        }

        private void ScreenshotEditorWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = true;
            ApplyThemeStyling();

            if (FontFamilyCombo.SelectedIndex < 0) FontFamilyCombo.SelectedIndex = 0;
            if (ChatSourceCombo.SelectedIndex < 0) ChatSourceCombo.SelectedIndex = 0;

            PopulateResolutionPresets();
            LoadSavedSettings();
            LoadLiveSessionChat();
            UpdateCanvas();
        }

        private void LoadSavedSettings()
        {
            _isUpdatingUi = true;
            try
            {
                var s = Properties.Settings.Default;

                if (!string.IsNullOrWhiteSpace(s.ScreenshotFontFamily))
                {
                    foreach (ComboBoxItem item in FontFamilyCombo.Items)
                    {
                        if (string.Equals(item.Content?.ToString(), s.ScreenshotFontFamily, StringComparison.OrdinalIgnoreCase))
                        {
                            FontFamilyCombo.SelectedItem = item;
                            break;
                        }
                    }
                }

                if (s.ScreenshotFontSize >= FontSizeSlider.Minimum && s.ScreenshotFontSize <= FontSizeSlider.Maximum)
                {
                    FontSizeSlider.Value = s.ScreenshotFontSize;
                    FontSizeValText.Text = Math.Round(s.ScreenshotFontSize).ToString();
                }

                if (s.ScreenshotLineSpacing >= LineSpacingSlider.Minimum && s.ScreenshotLineSpacing <= LineSpacingSlider.Maximum)
                {
                    LineSpacingSlider.Value = s.ScreenshotLineSpacing;
                    LineSpacingValText.Text = Math.Round(s.ScreenshotLineSpacing).ToString();
                }

                if (s.ScreenshotOutlineWidth >= OutlineWidthSlider.Minimum && s.ScreenshotOutlineWidth <= OutlineWidthSlider.Maximum)
                {
                    OutlineWidthSlider.Value = s.ScreenshotOutlineWidth;
                    OutlineWidthValText.Text = s.ScreenshotOutlineWidth.ToString("0.0");
                }

                FontBoldCheck.IsChecked = s.ScreenshotFontBold;
                DropShadowCheck.IsChecked = s.ScreenshotDropShadow;
                EnableBgBoxCheck.IsChecked = s.ScreenshotEnableBgBox;

                if (s.ScreenshotBgBoxOpacity >= BgBoxOpacitySlider.Minimum && s.ScreenshotBgBoxOpacity <= BgBoxOpacitySlider.Maximum)
                {
                    BgBoxOpacitySlider.Value = s.ScreenshotBgBoxOpacity;
                    BgBoxOpacityValText.Text = s.ScreenshotBgBoxOpacity.ToString("0.00");
                }

                _chatX = s.ScreenshotChatX > 0 ? s.ScreenshotChatX : 30;
                _chatY = s.ScreenshotChatY > 0 ? s.ScreenshotChatY : 30;
                ChatXTextBox.Text = Math.Round(_chatX).ToString();
                ChatYTextBox.Text = Math.Round(_chatY).ToString();

                if (s.ScreenshotCanvasWidth >= 100 && s.ScreenshotCanvasHeight >= 100)
                {
                    CanvasWidthTextBox.Text = s.ScreenshotCanvasWidth.ToString();
                    CanvasHeightTextBox.Text = s.ScreenshotCanvasHeight.ToString();
                    var match = ResolutionPreset.DefaultPresets.FirstOrDefault(p => p.Width == s.ScreenshotCanvasWidth && p.Height == s.ScreenshotCanvasHeight);
                    if (match != null)
                    {
                        ResolutionPresetCombo.SelectedItem = match;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load screenshot editor settings.");
            }
            finally
            {
                _isUpdatingUi = false;
            }
        }

        private void SaveSettings()
        {
            if (!_isLoaded || _isUpdatingUi) return;
            try
            {
                var s = Properties.Settings.Default;
                s.ScreenshotFontFamily = (FontFamilyCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Arial";
                s.ScreenshotFontSize = FontSizeSlider.Value;
                s.ScreenshotLineSpacing = LineSpacingSlider.Value;
                s.ScreenshotOutlineWidth = OutlineWidthSlider.Value;
                s.ScreenshotFontBold = FontBoldCheck.IsChecked == true;
                s.ScreenshotDropShadow = DropShadowCheck.IsChecked == true;
                s.ScreenshotEnableBgBox = EnableBgBoxCheck.IsChecked == true;
                s.ScreenshotBgBoxOpacity = BgBoxOpacitySlider.Value;
                s.ScreenshotChatX = _chatX;
                s.ScreenshotChatY = _chatY;

                if (int.TryParse(CanvasWidthTextBox.Text, out int w) && w >= 100)
                    s.ScreenshotCanvasWidth = w;
                if (int.TryParse(CanvasHeightTextBox.Text, out int h) && h >= 100)
                    s.ScreenshotCanvasHeight = h;

                s.Save();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to save screenshot editor settings.");
            }
        }

        private static SolidColorBrush FreezeBrush(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }

        /// <summary>
        /// Resolves the surfaces this window cannot take straight from the MahApps palette.
        ///
        /// The canvas viewport and the chat preview lists stay dark in both app modes on purpose:
        /// they show GTA World chat colours, which are chosen to sit on the game's dark HUD and
        /// become unreadable on a light ground. Light mode gets the softer slate the Live Tail
        /// viewport already uses rather than near-black, so the panel still blends into the window.
        /// Everything else in this window is a DynamicResource off the theme.
        /// </summary>
        private void ApplyThemeStyling()
        {
            bool dark = StyleController.DarkMode;

            Resources["ScreenshotChatSurface"] = FreezeBrush(dark ? "#16171A" : "#2B303A");
            Resources["ScreenshotViewportGround"] = FreezeBrush(dark ? "#121212" : "#20242C");
            Resources["ScreenshotCanvasFrame"] = FreezeBrush(dark ? "#0A0A0A" : "#171A20");

            // The checkerboard has to read as "nothing here" against whichever ground is behind it.
            Resources["ScreenshotCheckerLight"] = FreezeBrush(dark ? "#3A3A3A" : "#4A505C");
            Resources["ScreenshotCheckerDark"] = FreezeBrush(dark ? "#2B2B2B" : "#3A404A");

            Resources["ScreenshotOverlayFill"] = FreezeBrush(dark ? "#CC1E1E1E" : "#CC232830");
            Resources["ScreenshotOverlayBorder"] = FreezeBrush("#2EFFFFFF");
            Resources["ScreenshotOverlayText"] = FreezeBrush("#C8C8C8");
            Resources["ScreenshotOverlayMuted"] = FreezeBrush("#8A8A8A");
            Resources["ScreenshotSelectionFill"] = FreezeBrush("#14FFFFFF");

            Resources["ScreenshotBannerFill"] = FreezeBrush(dark ? "#1E2A33" : "#26333D");
            Resources["ScreenshotBannerBorder"] = FreezeBrush(dark ? "#2F4A5A" : "#3B5666");
            Resources["ScreenshotDanger"] = FreezeBrush(dark ? "#E05555" : "#F07070");

            ViewportSurface.Background = (Brush)Resources["ScreenshotViewportGround"];
            CanvasFrame.Background = (Brush)Resources["ScreenshotCanvasFrame"];
        }

        private void PopulateResolutionPresets()
        {
            _isUpdatingUi = true;
            ResolutionPresetCombo.ItemsSource = ResolutionPreset.DefaultPresets;
            ResolutionPresetCombo.DisplayMemberPath = "DisplayText";
            ResolutionPresetCombo.SelectedItem =
                ResolutionPreset.DefaultPresets.FirstOrDefault(p => p.Width == 1300 && p.Height == 730)
                ?? ResolutionPreset.DefaultPresets.FirstOrDefault();
            _isUpdatingUi = false;
        }


        private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isLoaded || _isUpdatingUi) return;
            if (_backgroundImage == null) return;

            double newScale = ZoomSlider.Value / 100.0;
            if (Math.Abs(newScale - _imageScale) < 0.001) return;

            var (canvasW, canvasH) = GetCanvasSize();
            double centerX = canvasW / 2.0;
            double centerY = canvasH / 2.0;

            _imageOffsetX = centerX - (centerX - _imageOffsetX) * (newScale / _imageScale);
            _imageOffsetY = centerY - (centerY - _imageOffsetY) * (newScale / _imageScale);
            _imageScale = newScale;

            if (ZoomValText != null)
                ZoomValText.Text = $"{(int)Math.Round(_imageScale * 100)}%";

            UpdateCanvas();
        }

        private void SyncZoomSlider()
        {
            if (ZoomSlider == null || ZoomValText == null) return;
            bool wasUpdating = _isUpdatingUi;
            _isUpdatingUi = true;
            ZoomSlider.Value = Math.Clamp(Math.Round(_imageScale * 100.0), ZoomSlider.Minimum, ZoomSlider.Maximum);
            ZoomValText.Text = $"{(int)Math.Round(_imageScale * 100)}%";
            _isUpdatingUi = wasUpdating;
        }

        // ==========================================
        // SOURCE LOADING
        // ==========================================

        private static ChatSelectableItem BuildSourceItem(string rawLine, List<CapturedChatSpan>? spans = null)
        {
            string cleaned = RoleplayChatColorizer.StripTimestamp(rawLine);
            var category = RoleplayChatColorizer.Classify(cleaned);

            string colorHex = ChatLineClassifier.GetHexColor(category);
            if (spans != null)
            {
                foreach (var s in spans)
                {
                    if (!string.IsNullOrWhiteSpace(s.Color) &&
                        !string.Equals(s.Color, "#FFFFFF", StringComparison.OrdinalIgnoreCase))
                    {
                        colorHex = s.Color;
                        break;
                    }
                }
            }

            var item = new ChatSelectableItem
            {
                RawText = rawLine,
                CleanedText = cleaned,
                Category = category,
                NuiSpans = spans,
                IsSelected = false
            };
            item.ColorHex = colorHex;
            return item;
        }

        /// <summary>
        /// Prefers the rich lines captured from the FiveM NUI — those carry the exact colours the
        /// game painted. Only falls back to re-reading flat text when no live session is available.
        /// </summary>
        private void LoadLiveSessionChat()
        {
            _sourceLines.Clear();
            int nuiCount = 0;

            try
            {
                IReadOnlyList<CapturedChatLine> rich = FiveMChatCaptureService.SessionRichLines;
                if (rich != null && rich.Count > 0)
                {
                    foreach (var line in rich)
                    {
                        if (string.IsNullOrWhiteSpace(line.Text)) continue;

                        bool hasColor = line.Spans != null && line.Spans.Count > 0;
                        if (hasColor) nuiCount++;

                        _sourceLines.Add(BuildSourceItem(line.Text, hasColor ? line.Spans : null));
                    }
                }
                else
                {
                    string raw = AppController.ParseChatLog(false);
                    if (!string.IsNullOrWhiteSpace(raw))
                    {
                        foreach (var line in raw.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            if (string.IsNullOrWhiteSpace(RoleplayChatColorizer.StripTimestamp(line))) continue;
                            _sourceLines.Add(BuildSourceItem(line));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load live chat session into the Screenshot Editor.");
            }

            SetStatus($"Loaded {_sourceLines.Count} lines.");
            ShowColorSource(nuiCount, _sourceLines.Count);
            ApplyChatFilter();
        }

        private void LoadChatFromFile(string filePath)
        {
            _sourceLines.Clear();
            try
            {
                foreach (var line in File.ReadAllLines(filePath))
                {
                    if (string.IsNullOrWhiteSpace(RoleplayChatColorizer.StripTimestamp(line))) continue;
                    _sourceLines.Add(BuildSourceItem(line));
                }
                SetStatus($"Loaded {_sourceLines.Count} lines from {Path.GetFileName(filePath)}.");
                ShowColorSource(0, _sourceLines.Count);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load chat log file {Path}", filePath);
                MessageBox.Show(this, $"Failed to load chat file:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            ApplyChatFilter();
        }

        /// <summary>
        /// States plainly where the colours came from. An imported log carries no colour of its
        /// own, so the user needs to know the badges are this app's reading of the text and not
        /// something the game recorded — otherwise a wrong colour looks like corrupt data.
        /// </summary>
        private void ShowColorSource(int capturedCount, int totalCount)
        {
            if (totalCount == 0)
            {
                ColorSourceBanner.Visibility = Visibility.Collapsed;
                return;
            }

            if (capturedCount > 0)
            {
                ColorSourceIcon.Kind = MahApps.Metro.IconPacks.PackIconMaterialKind.CheckCircleOutline;
                ColorSourceIcon.Foreground = new SolidColorBrush(Color.FromRgb(0x5F, 0xC3, 0x7A));
                ColorSourceText.Text = capturedCount == totalCount
                    ? "Exact colours captured live from the game."
                    : $"{capturedCount} of {totalCount} lines have exact colours captured from the game. The rest were detected from their text.";
            }
            else
            {
                ColorSourceIcon.Kind = MahApps.Metro.IconPacks.PackIconMaterialKind.InformationOutline;
                ColorSourceIcon.Foreground = new SolidColorBrush(Color.FromRgb(0x5F, 0xA8, 0xD3));
                ColorSourceText.Text = "This chat carries no colour, so colours were detected from each line's text. Hover a badge to see what it was read as, or pick a line and click a swatch to set it yourself.";
            }

            ColorSourceBanner.Visibility = Visibility.Visible;
        }

        private void ApplyChatFilter()
        {
            if (!_isLoaded) return;

            _filteredSourceLines.Clear();
            string filterText = ChatSearchTextBox.Text?.Trim() ?? string.Empty;

            bool meDo = FilterMeDoRadio.IsChecked == true;
            bool dialogue = FilterDialogueRadio.IsChecked == true;
            bool radio = FilterRadioRadio.IsChecked == true;

            foreach (var item in _sourceLines)
            {
                if (!string.IsNullOrEmpty(filterText) &&
                    item.CleanedText.IndexOf(filterText, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                // Filtering is by roleplay category, not by colour — /me and /do share a colour, so
                // a colour comparison could not tell the two filters apart.
                if (meDo && item.Category != ChatLineCategory.Emote && item.Category != ChatLineCategory.Action)
                    continue;

                if (dialogue && item.Category != ChatLineCategory.ICSpeech &&
                    item.Category != ChatLineCategory.ICWhisper && item.Category != ChatLineCategory.ICShout)
                    continue;

                if (radio && item.Category != ChatLineCategory.Radio &&
                    item.Category != ChatLineCategory.Phone && item.Category != ChatLineCategory.PM)
                    continue;

                _filteredSourceLines.Add(item);
            }

            SourceCountText.Text = _filteredSourceLines.Count == _sourceLines.Count
                ? $"{_sourceLines.Count} lines"
                : $"{_filteredSourceLines.Count} of {_sourceLines.Count}";
        }

        // ==========================================
        // RENDERING
        // ==========================================

        private (int Width, int Height) GetCanvasSize()
        {
            int w = 1300, h = 730;
            int.TryParse(CanvasWidthTextBox.Text, out w);
            int.TryParse(CanvasHeightTextBox.Text, out h);
            return (Math.Max(100, w), Math.Max(100, h));
        }

        private ScreenshotRenderOptions BuildOptions()
        {
            var (w, h) = GetCanvasSize();
            return new ScreenshotRenderOptions
            {
                CanvasWidth = w,
                CanvasHeight = h,
                ImageOffsetX = _imageOffsetX,
                ImageOffsetY = _imageOffsetY,
                ImageScale = _imageScale,
                ChatX = _chatX,
                ChatY = _chatY,
                FontFamily = (FontFamilyCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Arial",
                FontSize = FontSizeSlider.Value,
                IsBold = FontBoldCheck.IsChecked == true,
                LineSpacing = LineSpacingSlider.Value,
                OutlineWidth = OutlineWidthSlider.Value,
                EnableDropShadow = DropShadowCheck.IsChecked == true,
                EnableBackgroundBox = EnableBgBoxCheck.IsChecked == true,
                BackgroundBoxOpacity = BgBoxOpacitySlider.Value,

                // Uncovered canvas stays transparent so the checkerboard behind the preview shows
                // through, making an image dragged off-axis immediately obvious.
                TransparentBackground = true
            };
        }

        private void UpdateCanvas()
        {
            if (!_isLoaded || _isUpdatingUi) return;

            var options = BuildOptions();

            var linesSegments = new List<List<ChatStyledSegment>>();
            foreach (var placed in _placedLines)
            {
                if (string.IsNullOrWhiteSpace(placed.RawText)) continue;
                var segs = placed.ToSegments();
                if (segs.Count > 0) linesSegments.Add(segs);
            }

            _chatBlockRect = ScreenshotRenderer.MeasureChatBlock(linesSegments, options);
            CanvasPreviewImage.Source = ScreenshotRenderer.Render(_backgroundImage, linesSegments, options);

            EmptyStateHint.Visibility = _backgroundImage == null ? Visibility.Visible : Visibility.Collapsed;
            CheckerboardLayer.Visibility = CanvasPreviewImage.Source == null ? Visibility.Collapsed : Visibility.Visible;
            PlacedEmptyHint.Visibility = _placedLines.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            CanvasStatsText.Text = $"{options.CanvasWidth} × {options.CanvasHeight}   ·   zoom {Math.Round(_imageScale * 100)}%   ·   chat {Math.Round(_chatX)}, {Math.Round(_chatY)}";

            SyncZoomSlider();
            UpdateChatOutline();
        }

        private void UpdatePlacedCount()
        {
            if (!_isLoaded) return;
            PlacedCountText.Text = _placedLines.Count == 0 ? "" : $"{_placedLines.Count} lines";
        }

        /// <summary>Canvas pixels per displayed DIP.</summary>
        private double GetDipToCanvasRatio()
        {
            var (canvasW, _) = GetCanvasSize();
            double actualW = CanvasPreviewImage.ActualWidth;
            return actualW > 0 ? canvasW / actualW : 1.0;
        }

        /// <summary>
        /// Positions the dashed selection rectangle over the chat block. Without this the block is
        /// invisible until you happen to grab it, which was the single most confusing thing about
        /// dragging text around the canvas.
        /// </summary>
        private void UpdateChatOutline()
        {
            if (_placedLines.Count == 0 || _chatBlockRect.IsEmpty || _chatBlockRect.Width <= 0)
            {
                ChatBlockOutline.Visibility = Visibility.Collapsed;
                return;
            }

            double ratio = GetDipToCanvasRatio();
            if (ratio <= 0) return;

            double scale = 1.0 / ratio;   // canvas px -> DIP
            double pad = 4;

            Canvas.SetLeft(ChatBlockOutline, (_chatBlockRect.X * scale) - pad);
            Canvas.SetTop(ChatBlockOutline, (_chatBlockRect.Y * scale) - pad);
            ChatBlockOutline.Width = Math.Max(0, (_chatBlockRect.Width * scale) + (pad * 2));
            ChatBlockOutline.Height = Math.Max(0, (_chatBlockRect.Height * scale) + (pad * 2));
            ChatBlockOutline.Visibility = Visibility.Visible;
        }

        private void ViewportGrid_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateChatOutline();

        private void SetStatus(string text) => StatusInfoText.Text = text;

        // ==========================================
        // IMAGE IMPORT
        // ==========================================

        private void OpenImageBtn_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Image Files (*.png;*.jpg;*.jpeg;*.bmp;*.webp)|*.png;*.jpg;*.jpeg;*.bmp;*.webp|All Files (*.*)|*.*",
                Title = "Open Image"
            };

            if (dialog.ShowDialog() == true) LoadImageFromFile(dialog.FileName);
        }

        private void PasteImageBtn_Click(object sender, RoutedEventArgs e) => PasteImageFromClipboard();

        private void PasteImageFromClipboard()
        {
            try
            {
                if (Clipboard.ContainsImage())
                {
                    var img = Clipboard.GetImage();
                    if (img != null)
                    {
                        _backgroundImage = img;
                        FitImageToCanvas();
                        SetStatus($"Pasted image ({img.PixelWidth}×{img.PixelHeight}).");
                        UpdateCanvas();
                        return;
                    }
                }
                else if (Clipboard.ContainsFileDropList())
                {
                    foreach (string? file in Clipboard.GetFileDropList())
                    {
                        if (string.IsNullOrEmpty(file) || !File.Exists(file)) continue;
                        string ext = Path.GetExtension(file).ToLowerInvariant();
                        if (ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".webp")
                        {
                            LoadImageFromFile(file);
                            return;
                        }
                    }
                }

                MessageBox.Show(this, "No image found in the clipboard.", "Paste Image", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to paste image from clipboard.");
            }
        }

        private void LoadImageFromFile(string path)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(path);
                bmp.EndInit();
                bmp.Freeze();

                _backgroundImage = bmp;
                FitImageToCanvas();
                SetStatus($"Loaded {Path.GetFileName(path)} ({bmp.PixelWidth}×{bmp.PixelHeight}).");
                UpdateCanvas();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load image file {Path}", path);
                MessageBox.Show(this, $"Failed to load image:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ScaleImageToCanvas(bool fill)
        {
            if (_backgroundImage == null) return;

            var (canvasW, canvasH) = GetCanvasSize();
            double scaleX = (double)canvasW / _backgroundImage.PixelWidth;
            double scaleY = (double)canvasH / _backgroundImage.PixelHeight;
            _imageScale = fill ? Math.Max(scaleX, scaleY) : Math.Min(scaleX, scaleY);

            _imageOffsetX = (canvasW - (_backgroundImage.PixelWidth * _imageScale)) / 2.0;
            _imageOffsetY = (canvasH - (_backgroundImage.PixelHeight * _imageScale)) / 2.0;
        }

        private void FitImageToCanvas() => ScaleImageToCanvas(false);

        private void AspectFitBtn_Click(object sender, RoutedEventArgs e) { ScaleImageToCanvas(false); UpdateCanvas(); }

        private void AspectFillBtn_Click(object sender, RoutedEventArgs e) { ScaleImageToCanvas(true); UpdateCanvas(); }

        private void ResetViewBtn_Click(object sender, RoutedEventArgs e)
        {
            _imageOffsetX = 0;
            _imageOffsetY = 0;
            _imageScale = 1.0;
            UpdateCanvas();
        }

        private void ResolutionPresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded) return;
            if (ResolutionPresetCombo.SelectedItem is not ResolutionPreset preset) return;

            _isUpdatingUi = true;
            CanvasWidthTextBox.Text = preset.Width.ToString();
            CanvasHeightTextBox.Text = preset.Height.ToString();
            _isUpdatingUi = false;
            UpdateCanvas();
            SaveSettings();
        }

        private void CanvasDimensions_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isLoaded) return;
            UpdateCanvas();
            SaveSettings();
        }

        // ==========================================
        // CANVAS INTERACTION
        // ==========================================

        /// <summary>
        /// True when a canvas-space point lands on the chat block. Uses the measured block rect, so
        /// the grab area is exactly the text you can see — not a fixed guess at its size.
        /// </summary>
        private bool HitTestChatBlock(double canvasX, double canvasY)
        {
            if (_placedLines.Count == 0 || _chatBlockRect.IsEmpty) return false;

            var grab = _chatBlockRect;
            grab.Inflate(12, 12);
            return grab.Contains(canvasX, canvasY);
        }

        private void Viewport_MouseDown(object sender, MouseButtonEventArgs e)
        {
            double ratio = GetDipToCanvasRatio();

            if (e.LeftButton == MouseButtonState.Pressed)
            {
                Point pos = e.GetPosition(CanvasPreviewImage);

                if (HitTestChatBlock(pos.X * ratio, pos.Y * ratio))
                {
                    _isDraggingChat = true;
                    _dragStartMousePos = e.GetPosition(this);
                    _dragStartChatPos = new Point(_chatX, _chatY);
                    Mouse.Capture(ViewportGrid);
                    return;
                }

                _isDraggingImage = true;
                _dragStartMousePos = e.GetPosition(this);
                _dragStartImageOffset = new Point(_imageOffsetX, _imageOffsetY);
                Mouse.Capture(ViewportGrid);
            }
            else if (e.RightButton == MouseButtonState.Pressed || e.MiddleButton == MouseButtonState.Pressed)
            {
                _isDraggingImage = true;
                _dragStartMousePos = e.GetPosition(this);
                _dragStartImageOffset = new Point(_imageOffsetX, _imageOffsetY);
                Mouse.Capture(ViewportGrid);
            }
        }

        private void Viewport_MouseMove(object sender, MouseEventArgs e)
        {
            double ratio = GetDipToCanvasRatio();

            if (!_isDraggingChat && !_isDraggingImage)
            {
                // Cursor feedback makes the draggable region discoverable before you commit to a drag.
                Point hover = e.GetPosition(CanvasPreviewImage);
                CanvasPreviewImage.Cursor = HitTestChatBlock(hover.X * ratio, hover.Y * ratio)
                    ? Cursors.SizeAll
                    : Cursors.Arrow;
                return;
            }

            Point currentPos = e.GetPosition(this);
            double deltaX = (currentPos.X - _dragStartMousePos.X) * ratio;
            double deltaY = (currentPos.Y - _dragStartMousePos.Y) * ratio;

            if (_isDraggingChat)
            {
                _chatX = Math.Max(0, _dragStartChatPos.X + deltaX);
                _chatY = Math.Max(0, _dragStartChatPos.Y + deltaY);
                SyncPositionInputs();
            }
            else
            {
                _imageOffsetX = _dragStartImageOffset.X + deltaX;
                _imageOffsetY = _dragStartImageOffset.Y + deltaY;
            }

            UpdateCanvas();
        }

        private void Viewport_MouseUp(object sender, MouseButtonEventArgs e)
        {
            _isDraggingChat = false;
            _isDraggingImage = false;
            Mouse.Capture(null);
        }

        private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_backgroundImage == null) return;

            double oldScale = _imageScale;
            double newScale = Math.Clamp(oldScale * (e.Delta > 0 ? 1.08 : 0.92), 0.02, 20.0);
            if (Math.Abs(newScale - oldScale) < 0.0001) return;

            Point mousePos = e.GetPosition(CanvasPreviewImage);
            double ratio = GetDipToCanvasRatio();
            double mouseCanvasX = mousePos.X * ratio;
            double mouseCanvasY = mousePos.Y * ratio;

            _imageOffsetX = mouseCanvasX - (mouseCanvasX - _imageOffsetX) * (newScale / oldScale);
            _imageOffsetY = mouseCanvasY - (mouseCanvasY - _imageOffsetY) * (newScale / oldScale);
            _imageScale = newScale;

            UpdateCanvas();
        }

        // ==========================================
        // SOURCE LIST ACTIONS
        // ==========================================

        private void ChatSourceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded) return;

            bool isPaste = ChatSourceCombo.SelectedIndex == 2;
            SearchFilterGrid.Visibility = isPaste ? Visibility.Collapsed : Visibility.Visible;
            ChatLinesListBox.Visibility = isPaste ? Visibility.Collapsed : Visibility.Visible;
            CustomChatTextBox.Visibility = isPaste ? Visibility.Visible : Visibility.Collapsed;
            ReloadSourceBtn.IsEnabled = !isPaste;

            if (ChatSourceCombo.SelectedIndex == 0)
            {
                LoadLiveSessionChat();
            }
            else if (ChatSourceCombo.SelectedIndex == 1)
            {
                var dialog = new OpenFileDialog
                {
                    InitialDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GTAW-Log-Parser", "logs"),
                    Filter = "Text & Log Files (*.txt;*.log;*.html)|*.txt;*.log;*.html|All Files (*.*)|*.*",
                    Title = "Open GTA World Chat Log Backup"
                };

                if (dialog.ShowDialog() == true) LoadChatFromFile(dialog.FileName);
            }
            else
            {
                SetStatus("Paste chat lines, then press Add to canvas.");
                ShowColorSource(0, 1);
            }
        }

        private void ReloadSourceBtn_Click(object sender, RoutedEventArgs e)
        {
            if (ChatSourceCombo.SelectedIndex == 0) LoadLiveSessionChat();
            else ChatSourceCombo_SelectionChanged(sender, null!);
        }

        private void ChatSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isLoaded) return;
            ApplyChatFilter();
        }

        private void FilterRadio_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            ApplyChatFilter();
        }

        private void SelectAllLinesBtn_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _filteredSourceLines) item.IsSelected = true;
        }

        private void DeselectAllLinesBtn_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _filteredSourceLines) item.IsSelected = false;
        }

        private void AddSelectedLinesBtn_Click(object sender, RoutedEventArgs e)
        {
            int added = 0;

            if (ChatSourceCombo.SelectedIndex == 2)
            {
                string text = CustomChatTextBox.Text ?? string.Empty;
                foreach (var l in text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string cleaned = RoleplayChatColorizer.StripTimestamp(l);
                    if (string.IsNullOrWhiteSpace(cleaned)) continue;

                    var placed = new PlacedChatLine();
                    placed.RawText = cleaned;
                    _placedLines.Add(placed);
                    added++;
                }
            }
            else
            {
                foreach (var item in _filteredSourceLines.Where(l => l.IsSelected).ToList())
                {
                    var placed = new PlacedChatLine { NuiSpans = item.NuiSpans };
                    placed.RawText = item.CleanedText;
                    _placedLines.Add(placed);
                    item.IsSelected = false;
                    added++;
                }
            }

            UpdateCanvas();
            SetStatus(added == 0 ? "Nothing selected to add." : $"Added {added} line{(added == 1 ? "" : "s")} to the canvas.");
        }

        // ==========================================
        // PLACED LINE ACTIONS
        // ==========================================

        private void PlacedLinesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

        private void PlacedLineText_Changed(object sender, TextChangedEventArgs e)
        {
            if (!_isLoaded) return;
            UpdateCanvas();
        }

        private void DeletePlacedLine_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { DataContext: PlacedChatLine line })
            {
                _placedLines.Remove(line);
                UpdateCanvas();
            }
        }

        private void MoveLineUpBtn_Click(object sender, RoutedEventArgs e) => MovePlacedLine(-1);

        private void MoveLineDownBtn_Click(object sender, RoutedEventArgs e) => MovePlacedLine(+1);

        private void MovePlacedLine(int delta)
        {
            int idx = PlacedLinesListBox.SelectedIndex;
            int target = idx + delta;
            if (idx < 0 || target < 0 || target >= _placedLines.Count) return;

            _placedLines.Move(idx, target);
            PlacedLinesListBox.SelectedIndex = target;
            UpdateCanvas();
        }

        private void AddCustomLineBtn_Click(object sender, RoutedEventArgs e)
        {
            var placed = new PlacedChatLine();
            placed.RawText = "* Character Name performs an action.";
            _placedLines.Add(placed);
            PlacedLinesListBox.SelectedItem = placed;
            UpdateCanvas();
        }

        private void ClearAllPlacedLinesBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_placedLines.Count == 0) return;

            _placedLines.Clear();
            UpdateCanvas();
            SetStatus("Canvas cleared.");
        }

        private void LineColorSquare_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement elem || elem.DataContext is not PlacedChatLine line) return;

            var menu = new ContextMenu
            {
                PlacementTarget = elem,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom
            };

            foreach (var swatch in RoleplayChatColorizer.EditorSwatches)
            {
                var item = new MenuItem
                {
                    Header = swatch.Label,
                    ToolTip = swatch.Tooltip,
                    Icon = new Border
                    {
                        Width = 14,
                        Height = 14,
                        CornerRadius = new CornerRadius(3),
                        Background = FreezeBrush(swatch.Hex),
                        BorderBrush = FreezeBrush("#55FFFFFF"),
                        BorderThickness = new Thickness(1),
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center
                    }
                };

                string hex = swatch.Hex;
                string label = swatch.Label;
                item.Click += (_, __) =>
                {
                    line.ColorOverride = hex;
                    UpdateCanvas();
                    SetStatus($"Line colour set to {label} ({hex}).");
                };

                menu.Items.Add(item);
            }

            menu.Items.Add(new Separator());

            var autoItem = new MenuItem
            {
                Header = "Auto (Detected Colour)",
                ToolTip = "Reset to default detected roleplay colour",
                Icon = new PackIconMaterial
                {
                    Kind = PackIconMaterialKind.AutoFix,
                    Width = 13,
                    Height = 13,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            autoItem.Click += (_, __) =>
            {
                line.ColorOverride = null;
                UpdateCanvas();
                SetStatus("Line colour reset to detected colour.");
            };
            menu.Items.Add(autoItem);

            var customItem = new MenuItem
            {
                Header = "Custom Colour...",
                ToolTip = "Choose any custom colour from palette",
                Icon = new PackIconMaterial
                {
                    Kind = PackIconMaterialKind.Palette,
                    Width = 13,
                    Height = 13,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            customItem.Click += (_, __) =>
            {
                try
                {
                    using var dlg = new System.Windows.Forms.ColorDialog
                    {
                        AllowFullOpen = true,
                        AnyColor = true,
                        FullOpen = true
                    };

                    if (!string.IsNullOrEmpty(line.ColorHex))
                    {
                        try
                        {
                            var wpfColor = (Color)ColorConverter.ConvertFromString(line.ColorHex);
                            dlg.Color = System.Drawing.Color.FromArgb(wpfColor.R, wpfColor.G, wpfColor.B);
                        }
                        catch { }
                    }

                    if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    {
                        string customHex = $"#{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}";
                        line.ColorOverride = customHex;
                        UpdateCanvas();
                        SetStatus($"Line colour set to custom ({customHex}).");
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to open custom colour dialog.");
                }
            };
            menu.Items.Add(customItem);

            menu.IsOpen = true;
        }

        // ==========================================
        // POSITION & STYLE
        // ==========================================

        private void AnchorTopLeft_Click(object sender, RoutedEventArgs e) => AnchorChat(true, true);

        private void AnchorTopRight_Click(object sender, RoutedEventArgs e) => AnchorChat(false, true);

        private void AnchorBottomLeft_Click(object sender, RoutedEventArgs e) => AnchorChat(true, false);

        private void AnchorBottomRight_Click(object sender, RoutedEventArgs e) => AnchorChat(false, false);

        /// <summary>
        /// Anchors using the measured block size, so the block lands flush against the chosen corner
        /// whatever its actual extent — the old fixed 550×220 guess drifted for long or short blocks.
        /// </summary>
        private void AnchorChat(bool left, bool top)
        {
            var (canvasW, canvasH) = GetCanvasSize();
            const double margin = 30;

            double blockW = _chatBlockRect.IsEmpty ? 0 : _chatBlockRect.Width;
            double blockH = _chatBlockRect.IsEmpty ? 0 : _chatBlockRect.Height;

            _chatX = left ? margin : Math.Max(margin, canvasW - blockW - margin);
            _chatY = top ? margin : Math.Max(margin, canvasH - blockH - margin);

            SyncPositionInputs();
            UpdateCanvas();
            SaveSettings();
        }

        private void SyncPositionInputs()
        {
            _isUpdatingUi = true;
            ChatXTextBox.Text = Math.Round(_chatX).ToString();
            ChatYTextBox.Text = Math.Round(_chatY).ToString();
            _isUpdatingUi = false;
        }

        private void ChatPosition_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isLoaded || _isUpdatingUi) return;

            double.TryParse(ChatXTextBox.Text, out _chatX);
            double.TryParse(ChatYTextBox.Text, out _chatY);
            UpdateCanvas();
            SaveSettings();
        }

        private void Styling_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            UpdateCanvas();
            SaveSettings();
        }

        private void StylingSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isLoaded) return;

            FontSizeValText.Text = Math.Round(FontSizeSlider.Value).ToString();
            LineSpacingValText.Text = Math.Round(LineSpacingSlider.Value).ToString();
            OutlineWidthValText.Text = OutlineWidthSlider.Value.ToString("0.0");
            BgBoxOpacityValText.Text = BgBoxOpacitySlider.Value.ToString("0.00");

            UpdateCanvas();
            SaveSettings();
        }

        // ==========================================
        // EXPORT
        // ==========================================

        private void CopyToClipboardBtn_Click(object sender, RoutedEventArgs e)
        {
            if (CanvasPreviewImage.Source is not BitmapSource bmp) return;

            // The clipboard carries a DIB, which does not reliably preserve alpha — pasting a
            // transparent image into Discord or a forum tends to come out black. Flatten first so
            // what lands in the paste target is what the editor shows.
            BitmapSource clipboardImage = ScreenshotRenderer.Flatten(bmp);

            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    Clipboard.SetImage(clipboardImage);
                    SetStatus("Image copied to the clipboard.");
                    return;
                }
                catch
                {
                    Thread.Sleep(30);
                }
            }

            MessageBox.Show(this, "The clipboard is in use by another program.", "Clipboard Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void SavePngBtn_Click(object sender, RoutedEventArgs e) => SaveImage(false);

        private void SaveJpegBtn_Click(object sender, RoutedEventArgs e) => SaveImage(true);

        private void SaveImage(bool jpeg)
        {
            if (CanvasPreviewImage.Source is not BitmapSource bmp) return;

            var dialog = new SaveFileDialog
            {
                Filter = jpeg ? "JPEG Image (*.jpg;*.jpeg)|*.jpg;*.jpeg" : "PNG Image (*.png)|*.png",
                FileName = $"GTAW_Screenshot_{DateTime.Now:yyyyMMdd_HHmmss}{(jpeg ? ".jpg" : ".png")}"
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                if (jpeg) ScreenshotRenderer.SaveToJpeg(bmp, dialog.FileName, 95);
                else ScreenshotRenderer.SaveToPng(bmp, dialog.FileName);

                SetStatus(jpeg
                    ? $"Saved {Path.GetFileName(dialog.FileName)} — JPEG has no transparency, so any uncovered canvas was filled black."
                    : $"Saved {Path.GetFileName(dialog.FileName)} — uncovered canvas was kept transparent.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to save image.");
                MessageBox.Show(this, $"Failed to save image:\n{ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MetroWindow_KeyDown(object sender, KeyEventArgs e)
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

            if (ctrl && e.Key == Key.O)
            {
                OpenImageBtn_Click(sender, e);
                e.Handled = true;
            }
            else if (ctrl && !shift && e.Key == Key.V)
            {
                if (FocusManager.GetFocusedElement(this) is not TextBox)
                {
                    PasteImageFromClipboard();
                    e.Handled = true;
                }
            }
            else if (ctrl && shift && e.Key == Key.C)
            {
                CopyToClipboardBtn_Click(sender, e);
                e.Handled = true;
            }
        }
    }
}
