using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Text;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Threading;
using Assistant.Controllers;
using ControlzEx.Theming;
using GTAWParser.Shared;
using GTAWParser.Shared.Screenshot;
using MahApps.Metro.Controls;
using MahApps.Metro.IconPacks;
using Microsoft.Win32;
using Serilog;

namespace Assistant.UI
{
    /// <summary>A line placed on the canvas.</summary>
    public class PlacedChatLine : INotifyPropertyChanged
    {
        private string _colorHex = "#FFFFFF";
        private SolidColorBrush _colorBrush = Brushes.White;
        private SolidColorBrush _badgeBackground = Brushes.Transparent;
        private string _rawText = string.Empty;
        private string? _colorOverride;
        private List<ChatStyledSegment> _segments = new List<ChatStyledSegment>();

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
        public string TypeLabel => ChatLineClassifier.GetShortLabel(Category);

        public List<CapturedChatSpan>? NuiSpans { get; set; }

        public List<ChatStyledSegment> Segments
        {
            get => _segments;
            set
            {
                _segments = value;
                Raise(nameof(Segments));
            }
        }

        public string RawText
        {
            get => _rawText;
            set
            {
                if (_rawText == value) return;
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
            : $"Detected as {ChatLineClassifier.DescribeCategory(Category)} ({ColorHex}) (Click square to change)";

        public void Reclassify()
        {
            Category = RoleplayChatColorizer.Classify(_rawText);
            ColorHex = _colorOverride ?? DominantOf(NuiSpans) ?? DominantOf(_segments) ?? ChatLineClassifier.GetHexColor(Category);
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

        private static string? DominantOf(List<ChatStyledSegment>? segments)
        {
            if (segments == null) return null;
            foreach (var s in segments)
            {
                if (!string.IsNullOrWhiteSpace(s.ColorHex) &&
                    !string.Equals(s.ColorHex, "#FFFFFF", StringComparison.OrdinalIgnoreCase))
                {
                    return s.ColorHex;
                }
            }
            return null;
        }

        /// <summary>Builds the styled segments this line contributes to the render.</summary>
        public List<ChatStyledSegment> ToSegments()
        {
            if (_colorOverride != null)
            {
                var baseSegs = _segments != null && _segments.Count > 0
                    ? _segments
                    : RoleplayChatColorizer.ColorizeLine(_rawText);
                return RoleplayChatColorizer.Recolor(baseSegs, _colorOverride);
            }

            if (_segments != null && _segments.Count > 0)
            {
                return _segments;
            }

            List<ChatStyledSegment> segments =
                NuiSpans != null && NuiSpans.Count > 0
                    ? RoleplayChatColorizer.FromSpans(NuiSpans)
                    : RoleplayChatColorizer.ColorizeLine(_rawText);

            if (segments.Count == 0 && !string.IsNullOrWhiteSpace(_rawText))
            {
                segments.Add(new ChatStyledSegment(_rawText, ColorHex));
            }

            return segments;
        }

        private void RebuildBrushes()
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

        private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public static class RichChatLineHelper
    {
        public static readonly DependencyProperty PlacedLineProperty =
            DependencyProperty.RegisterAttached(
                "PlacedLine",
                typeof(PlacedChatLine),
                typeof(RichChatLineHelper),
                new PropertyMetadata(null, OnPlacedLineChanged));

        public static PlacedChatLine? GetPlacedLine(DependencyObject obj) =>
            (PlacedChatLine?)obj.GetValue(PlacedLineProperty);

        public static void SetPlacedLine(DependencyObject obj, PlacedChatLine? value) =>
            obj.SetValue(PlacedLineProperty, value);

        private static void OnPlacedLineChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not RichTextBox rtb) return;

            DataObject.RemovePastingHandler(rtb, OnPasting);
            DataObject.AddPastingHandler(rtb, OnPasting);

            if (rtb.IsLoaded)
            {
                PopulateDocument(rtb, e.NewValue as PlacedChatLine);
            }
            else
            {
                RoutedEventHandler? onLoaded = null;
                onLoaded = (s, args) =>
                {
                    rtb.Loaded -= onLoaded;
                    PopulateDocument(rtb, GetPlacedLine(rtb));
                };
                rtb.Loaded += onLoaded;
            }
        }

        private static void OnPasting(object sender, DataObjectPastingEventArgs e)
        {
            if (e.DataObject.GetDataPresent(DataFormats.UnicodeText))
            {
                string text = (string)e.DataObject.GetData(DataFormats.UnicodeText);
                if (sender is RichTextBox rtb)
                {
                    rtb.Selection.Text = text.Replace("\r\n", " ").Replace("\n", " ");
                    e.CancelCommand();
                    e.Handled = true;
                }
            }
        }

        public static void PopulateDocument(RichTextBox rtb, PlacedChatLine? line)
        {
            if (line == null)
            {
                rtb.Document = new FlowDocument();
                return;
            }

            var doc = new FlowDocument
            {
                PagePadding = new Thickness(0),
                LineHeight = 1
            };
            var para = new Paragraph { Margin = new Thickness(0), Padding = new Thickness(0) };

            var segments = line.ToSegments();
            if (segments.Count == 0 && !string.IsNullOrEmpty(line.RawText))
            {
                segments = new List<ChatStyledSegment> { new ChatStyledSegment(line.RawText, line.ColorHex) };
            }

            foreach (var seg in segments)
            {
                if (string.IsNullOrEmpty(seg.Text)) continue;
                var run = new Run(seg.Text)
                {
                    FontWeight = seg.IsBold ? FontWeights.Bold : FontWeights.Normal,
                    FontStyle = seg.IsItalic ? FontStyles.Italic : FontStyles.Normal
                };

                if (seg.IsCensored)
                {
                    run.Background = ScreenshotEditorWindow.CensorFillBrush;
                    run.Foreground = ScreenshotEditorWindow.CensorTextBrush;
                }
                else
                {
                    run.Background = Brushes.Transparent;
                    run.Foreground = ScreenshotEditorWindow.FreezeBrush(seg.ColorHex);
                }

                para.Inlines.Add(run);
            }

            if (para.Inlines.Count == 0)
            {
                para.Inlines.Add(new Run("") { Foreground = ScreenshotEditorWindow.FreezeBrush(line.ColorHex) });
            }

            doc.Blocks.Add(para);

            rtb.Tag = "populating";
            rtb.Document = doc;
            rtb.Tag = null;
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

        private Point _dragStartListPos;
        private PlacedChatLine? _draggedListLine;
        private bool _isDraggingListLine;

        private readonly ObservableCollection<PlacedChatLine> _placedLines = new ObservableCollection<PlacedChatLine>();

        /// <summary>Where the chat block sits on the canvas, from the last render.</summary>
        private Rect _chatBlockRect = Rect.Empty;

        /// <summary>
        /// The chat block pre-rendered to its own bitmap while an interactive drag is in progress.
        /// Its pixels do not change as the block or the image moves, so re-rasterising every glyph
        /// on each mouse move is pure waste -- and with the eight-pass outline it is most of the
        /// frame. Non-null only between drag start and drag end.
        /// </summary>
        private RenderTargetBitmap? _chatBlockCache;
        private Vector _chatBlockCacheOffset;
        private Size _chatBlockCacheSize;

        private bool _isLoaded;
        private bool _isUpdatingUi;

        public ScreenshotEditorWindow()
        {
            InitializeComponent();

            PlacedLinesListBox.ItemsSource = _placedLines;
            _placedLines.CollectionChanged += (_, __) => UpdatePlacedCount();

            Loaded += ScreenshotEditorWindow_Loaded;
            Closing += (_, __) =>
            {
                ThemeManager.Current.ThemeChanged -= OnThemeChanged;
                SaveSettings();
            };
            ThemeManager.Current.ThemeChanged += OnThemeChanged;
        }

        private void OnThemeChanged(object? sender, ThemeChangedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                ApplyThemeStyling();
                UpdateCanvas();
            });
        }

        private void ScreenshotEditorWindow_Loaded(object sender, RoutedEventArgs e)
        {
            ApplyThemeStyling();

            if (FontFamilyCombo.SelectedIndex < 0) FontFamilyCombo.SelectedIndex = 0;

            PopulateResolutionPresets();
            LoadSavedSettings();

            // Alt-tabbing mid-drag drops the capture without a MouseUp, which would otherwise leave
            // the cached drag bitmap in place and freeze the preview against later edits.
            ViewportGrid.LostMouseCapture += (_, __) =>
            {
                _isDraggingChat = false;
                _isDraggingImage = false;
                EndInteractiveDrag();
            };

            _isLoaded = true;
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

                int savedWidth = s.ScreenshotCanvasWidth >= 100 ? s.ScreenshotCanvasWidth : 1300;
                int savedHeight = s.ScreenshotCanvasHeight >= 100 ? s.ScreenshotCanvasHeight : 730;
                string? savedPreset = s.ScreenshotResolutionPreset;

                CanvasWidthTextBox.Text = savedWidth.ToString();
                CanvasHeightTextBox.Text = savedHeight.ToString();

                ResolutionPreset? match = null;
                if (!string.IsNullOrWhiteSpace(savedPreset))
                {
                    match = ResolutionPreset.DefaultPresets.FirstOrDefault(p =>
                        string.Equals(p.Name, savedPreset, StringComparison.OrdinalIgnoreCase));
                }

                if (match == null)
                {
                    match = ResolutionPreset.DefaultPresets.FirstOrDefault(p =>
                        p.Width == savedWidth && p.Height == savedHeight);
                }

                ResolutionPresetCombo.SelectedItem = match;
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

                if (ResolutionPresetCombo.SelectedItem is ResolutionPreset preset)
                {
                    s.ScreenshotResolutionPreset = preset.Name;
                }
                else
                {
                    s.ScreenshotResolutionPreset = "Custom";
                }

                s.Save();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to save screenshot editor settings.");
            }
        }

        /// <summary>Redaction bar fill, matching what the renderer bakes into the image.</summary>
        internal static SolidColorBrush CensorFillBrush { get; } = FreezeBrush("#050505");

        /// <summary>Muted text shown inside a redaction bar while the line is being edited.</summary>
        internal static SolidColorBrush CensorTextBrush { get; } = FreezeBrush("#888888");

        /// <summary>Hairline around a colour swatch, so it stays visible on any swatch colour.</summary>
        internal static SolidColorBrush SwatchBorderBrush { get; } = FreezeBrush("#55FFFFFF");

        public static SolidColorBrush FreezeBrush(string hex)
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

            var lightChecker = FreezeBrush(dark ? "#3A3A3A" : "#4A505C");
            var darkChecker = FreezeBrush(dark ? "#2B2B2B" : "#3A404A");

            Resources["ScreenshotChatSurface"] = FreezeBrush(dark ? "#16171A" : "#2B303A");
            Resources["ScreenshotChatMuted"] = FreezeBrush(dark ? "#8E9297" : "#A5ACB8");
            Resources["ScreenshotViewportGround"] = FreezeBrush(dark ? "#121212" : "#20242C");
            Resources["ScreenshotCanvasFrame"] = FreezeBrush(dark ? "#0A0A0A" : "#171A20");

            Resources["ScreenshotCheckerLight"] = lightChecker;
            Resources["ScreenshotCheckerDark"] = darkChecker;

            Resources["ScreenshotOverlayFill"] = FreezeBrush(dark ? "#CC1E1E1E" : "#CC232830");
            Resources["ScreenshotOverlayBorder"] = FreezeBrush(dark ? "#2EFFFFFF" : "#3B4252");
            Resources["ScreenshotOverlayText"] = FreezeBrush(dark ? "#C8C8C8" : "#E2E8F0");
            Resources["ScreenshotOverlayMuted"] = FreezeBrush(dark ? "#8A8A8A" : "#94A3B8");
            Resources["ScreenshotSelectionFill"] = FreezeBrush("#14FFFFFF");

            // The chat surface stays dark in both app modes, so its text and the hairline around
            // a colour swatch are fixed rather than mode-dependent -- but they still live here so
            // nothing in this window carries a literal colour of its own.
            Resources["ScreenshotChatText"] = FreezeBrush("#FFFFFF");
            Resources["ScreenshotSwatchBorder"] = SwatchBorderBrush;

            Resources["ScreenshotBannerFill"] = FreezeBrush(dark ? "#1E2A33" : "#26333D");
            Resources["ScreenshotBannerBorder"] = FreezeBrush(dark ? "#2F4A5A" : "#3B5666");
            Resources["ScreenshotDanger"] = FreezeBrush(dark ? "#E05555" : "#F07070");

            // Rebuild the checkerboard DrawingBrush dynamically so it re-renders immediately on theme changes
            var group = new DrawingGroup();
            group.Children.Add(new GeometryDrawing(lightChecker, null, new RectangleGeometry(new Rect(0, 0, 16, 16))));
            var darkGeom = new GeometryGroup();
            darkGeom.Children.Add(new RectangleGeometry(new Rect(0, 0, 8, 8)));
            darkGeom.Children.Add(new RectangleGeometry(new Rect(8, 8, 8, 8)));
            group.Children.Add(new GeometryDrawing(darkChecker, null, darkGeom));
            var checker = new DrawingBrush(group)
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, 16, 16),
                ViewportUnits = BrushMappingMode.Absolute,
                Stretch = Stretch.None
            };
            checker.Freeze();
            Resources["CheckerboardBrush"] = checker;
            if (CheckerboardLayer != null)
            {
                CheckerboardLayer.Fill = checker;
            }

            if (ViewportSurface != null)
                ViewportSurface.Background = (Brush)Resources["ScreenshotViewportGround"];
            if (CanvasFrame != null)
                CanvasFrame.Background = (Brush)Resources["ScreenshotCanvasFrame"];
        }

        private void PopulateResolutionPresets()
        {
            _isUpdatingUi = true;
            ResolutionPresetCombo.ItemsSource = ResolutionPreset.DefaultPresets;
            ResolutionPresetCombo.DisplayMemberPath = "DisplayText";
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

        /// <summary>Builds the drag-time chat bitmap, so each move is a composite rather than a redraw.</summary>
        private void BeginInteractiveDrag()
        {
            if (_placedLines.Count == 0)
            {
                _chatBlockCache = null;
                return;
            }

            var options = BuildOptions();
            var lines = BuildLineSegments();
            if (lines.Count == 0)
            {
                _chatBlockCache = null;
                return;
            }

            Rect block = ScreenshotRenderer.MeasureChatBlock(lines, options);
            _chatBlockCacheSize = new Size(block.Width, block.Height);

            var (bitmap, offset) = ScreenshotRenderer.RenderChatBlock(lines, options);
            _chatBlockCache = bitmap;
            _chatBlockCacheOffset = offset;
        }

        private void EndInteractiveDrag()
        {
            if (_chatBlockCache == null) return;

            _chatBlockCache = null;
            UpdateCanvas();
        }

        private List<List<ChatStyledSegment>> BuildLineSegments()
        {
            var linesSegments = new List<List<ChatStyledSegment>>();
            foreach (var placed in _placedLines)
            {
                if (string.IsNullOrWhiteSpace(placed.RawText)) continue;
                var segs = placed.ToSegments();
                if (segs.Count > 0) linesSegments.Add(segs);
            }
            return linesSegments;
        }

        private void UpdateCanvas()
        {
            if (!_isLoaded || _isUpdatingUi) return;

            var options = BuildOptions();

            if (_chatBlockCache != null)
            {
                // Mid-drag: the block's pixels and size are already known, only its origin moved.
                _chatBlockRect = new Rect(options.ChatX, options.ChatY, _chatBlockCacheSize.Width, _chatBlockCacheSize.Height);
                CanvasPreviewImage.Source = ScreenshotRenderer.RenderWithChatBlock(
                    _backgroundImage,
                    _chatBlockCache,
                    new Point(options.ChatX + _chatBlockCacheOffset.X, options.ChatY + _chatBlockCacheOffset.Y),
                    options);
            }
            else
            {
                var linesSegments = BuildLineSegments();
                _chatBlockRect = ScreenshotRenderer.MeasureChatBlock(linesSegments, options);
                CanvasPreviewImage.Source = ScreenshotRenderer.Render(_backgroundImage, linesSegments, options);
            }

            EmptyStateHint.Visibility = _backgroundImage == null ? Visibility.Visible : Visibility.Collapsed;
            CheckerboardLayer.Visibility = CanvasPreviewImage.Source == null ? Visibility.Collapsed : Visibility.Visible;
            PlacedEmptyHint.Visibility = _placedLines.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            CanvasStatsText.Text = $"{options.CanvasWidth} × {options.CanvasHeight}   ·   zoom {Math.Round(_imageScale * 100)}%   ·   chat {Math.Round(_chatX)}, {Math.Round(_chatY)}";

            SyncZoomSlider();
            UpdateChatOutline();
            UpdateImageDependentControls();
        }

        private void UpdateImageDependentControls()
        {
            bool hasImage = _backgroundImage != null;

            if (ResolutionPresetCombo != null)
            {
                ResolutionPresetCombo.IsEnabled = hasImage;
                ResolutionPresetCombo.ToolTip = hasImage ? "Choose a standard canvas resolution preset" : "Open or paste an image first";
            }
            if (CanvasWidthTextBox != null)
            {
                CanvasWidthTextBox.IsEnabled = hasImage;
                CanvasWidthTextBox.ToolTip = hasImage ? "Canvas width in pixels" : "Open or paste an image first";
            }
            if (CanvasHeightTextBox != null)
            {
                CanvasHeightTextBox.IsEnabled = hasImage;
                CanvasHeightTextBox.ToolTip = hasImage ? "Canvas height in pixels" : "Open or paste an image first";
            }

            if (AspectFitBtn != null)
            {
                AspectFitBtn.IsEnabled = hasImage;
                AspectFitBtn.ToolTip = hasImage ? "Scale the image to fit inside the canvas" : "Open or paste an image first";
            }
            if (AspectFillBtn != null)
            {
                AspectFillBtn.IsEnabled = hasImage;
                AspectFillBtn.ToolTip = hasImage ? "Scale the image to cover the canvas" : "Open or paste an image first";
            }
            if (ResetViewBtn != null)
            {
                ResetViewBtn.IsEnabled = hasImage;
                ResetViewBtn.ToolTip = hasImage ? "Reset pan and zoom to 100%" : "Open or paste an image first";
            }
            if (ZoomSlider != null)
            {
                ZoomSlider.IsEnabled = hasImage;
                ZoomSlider.ToolTip = hasImage ? "Zoom image manually (10% to 400%)" : "Open or paste an image first";
            }
            if (SaveImageBtn != null)
            {
                SaveImageBtn.IsEnabled = hasImage;
                SaveImageBtn.ToolTip = hasImage ? "Save rendered image to disk (PNG or JPEG)" : "Open or paste an image first";
            }
            if (CopyToClipboardBtn != null)
            {
                CopyToClipboardBtn.IsEnabled = hasImage;
                CopyToClipboardBtn.ToolTip = hasImage ? "Copy the rendered image to the clipboard (Ctrl+Shift+C)" : "Open or paste an image first";
            }
        }

        private void UpdatePlacedCount()
        {
            if (!_isLoaded) return;
            PlacedCountText.Text = _placedLines.Count == 0 ? "" : $"{_placedLines.Count} line{(_placedLines.Count == 1 ? "" : "s")}";
            if (PlacedEmptyHint != null)
            {
                PlacedEmptyHint.Visibility = _placedLines.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
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
            if (!_isLoaded || _isUpdatingUi) return;
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
            if (!_isLoaded || _isUpdatingUi) return;
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
                    BeginInteractiveDrag();
                    Mouse.Capture(ViewportGrid);
                    return;
                }

                _isDraggingImage = true;
                _dragStartMousePos = e.GetPosition(this);
                _dragStartImageOffset = new Point(_imageOffsetX, _imageOffsetY);
                BeginInteractiveDrag();
                Mouse.Capture(ViewportGrid);
            }
            else if (e.RightButton == MouseButtonState.Pressed || e.MiddleButton == MouseButtonState.Pressed)
            {
                _isDraggingImage = true;
                _dragStartMousePos = e.GetPosition(this);
                _dragStartImageOffset = new Point(_imageOffsetX, _imageOffsetY);
                BeginInteractiveDrag();
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
            bool wasDraggingChat = _isDraggingChat;
            _isDraggingChat = false;
            EndInteractiveDrag();
            _isDraggingImage = false;
            Mouse.Capture(null);
            if (wasDraggingChat)
            {
                SaveSettings();
            }
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
        // CHAT LINES ACTIONS
        // ==========================================

        private void ImportChatlogBtn_Click(object sender, RoutedEventArgs e)
        {
            string initialDir;
            string userBackupPath = Properties.Settings.Default.BackupPath;
            if (!string.IsNullOrWhiteSpace(userBackupPath) && Directory.Exists(userBackupPath))
            {
                initialDir = userBackupPath;
            }
            else
            {
                string defaultLogDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GTAW-Log-Parser", "logs");
                initialDir = Directory.Exists(defaultLogDir)
                    ? defaultLogDir
                    : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            }

            var dialog = new OpenFileDialog
            {
                InitialDirectory = initialDir,
                Filter = "Text & Log Files (*.txt;*.log;*.html)|*.txt;*.log;*.html|All Files (*.*)|*.*",
                Title = "Import GTA World Chat Log"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    string content = File.ReadAllText(dialog.FileName);
                    InsertChatLines(content);
                    SetStatus($"Imported chat log from {Path.GetFileName(dialog.FileName)}.");
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to import chat log file {Path}", dialog.FileName);
                    MessageBox.Show(this, $"Failed to read chat log file:\n{ex.Message}", "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void PasteChatBtn_Click(object sender, RoutedEventArgs e)
        {
            PasteChatFromClipboard();
        }

        private void PasteChatFromClipboard()
        {
            // Check HTML clipboard first (e.g. copied from browser or forum)
            if (Clipboard.ContainsText(TextDataFormat.Html))
            {
                string html = Clipboard.GetText(TextDataFormat.Html);
                if (ChatLogHtmlParser.IsHtml(html))
                {
                    InsertChatLines(html);
                    return;
                }
            }

            if (Clipboard.ContainsText())
            {
                string text = Clipboard.GetText();
                InsertChatLines(text);
                return;
            }

            SetStatus("Clipboard does not contain text.");
        }

        private void InsertChatLines(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            bool autoColor = AutoColorCheckBox?.IsChecked ?? true;
            int insertIndex = PlacedLinesListBox.SelectedIndex >= 0
                ? PlacedLinesListBox.SelectedIndex + 1
                : _placedLines.Count;

            var linesToAdd = new List<PlacedChatLine>();

            if (ChatLogHtmlParser.IsHtml(text))
            {
                var parsedHtmlLines = ChatLogHtmlParser.Parse(text);
                foreach (var htmlLine in parsedHtmlLines)
                {
                    if (string.IsNullOrWhiteSpace(htmlLine.RawText)) continue;

                    bool useHtmlColors = htmlLine.HasExplicitColors || htmlLine.IsFromStructuredChatLine;
                    PlacedChatLine placed;
                    if (useHtmlColors && htmlLine.Segments.Count > 0)
                    {
                        // Prioritize rich HTML colors (exact in-game CEF DOM capture from Live Tail or forum styles)
                        placed = new PlacedChatLine
                        {
                            RawText = htmlLine.RawText,
                            Segments = autoColor
                                ? htmlLine.Segments
                                : RoleplayChatColorizer.Recolor(htmlLine.Segments, "#FFFFFF")
                        };
                    }
                    else if (autoColor)
                    {
                        // Fallback to pattern guessing only for untagged, styleless HTML snippets
                        placed = new PlacedChatLine
                        {
                            RawText = htmlLine.RawText,
                            Segments = RoleplayChatColorizer.ColorizeLine(htmlLine.RawText)
                        };
                    }
                    else
                    {
                        placed = new PlacedChatLine
                        {
                            RawText = htmlLine.RawText,
                            ColorOverride = "#FFFFFF",
                            Segments = new List<ChatStyledSegment> { new ChatStyledSegment(htmlLine.RawText, "#FFFFFF") }
                        };
                    }

                    linesToAdd.Add(placed);
                }
            }
            else
            {
                var rawLines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var l in rawLines)
                {
                    string cleaned = RoleplayChatColorizer.StripTimestamp(l);
                    if (string.IsNullOrWhiteSpace(cleaned)) continue;

                    PlacedChatLine placed;
                    if (autoColor)
                    {
                        placed = new PlacedChatLine
                        {
                            RawText = cleaned,
                            Segments = RoleplayChatColorizer.ColorizeLine(cleaned)
                        };
                    }
                    else
                    {
                        placed = new PlacedChatLine
                        {
                            RawText = cleaned,
                            ColorOverride = "#FFFFFF",
                            Segments = new List<ChatStyledSegment> { new ChatStyledSegment(cleaned, "#FFFFFF") }
                        };
                    }

                    linesToAdd.Add(placed);
                }
            }

            if (linesToAdd.Count == 0)
            {
                SetStatus("No valid chat lines found.");
                return;
            }

            foreach (var placed in linesToAdd)
            {
                if (insertIndex <= _placedLines.Count)
                {
                    _placedLines.Insert(insertIndex, placed);
                    insertIndex++;
                }
                else
                {
                    _placedLines.Add(placed);
                }
            }

            UpdateCanvas();
            SetStatus($"Added {linesToAdd.Count} line{(linesToAdd.Count == 1 ? "" : "s")} to the canvas.");
        }

        private void PlacedLinesListBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                PasteChatFromClipboard();
                e.Handled = true;
            }
            else if (e.Key == Key.Delete && PlacedLinesListBox.SelectedItem is PlacedChatLine line)
            {
                _placedLines.Remove(line);
                UpdateCanvas();
                e.Handled = true;
            }
        }

        private void PlacedLineRichText_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true; // Prevent multiline inside a single chat line
                if (sender is RichTextBox rtb && rtb.DataContext is PlacedChatLine currentLine)
                {
                    int index = _placedLines.IndexOf(currentLine);
                    var newLine = new PlacedChatLine
                    {
                        RawText = "",
                        ColorOverride = "#FFFFFF",
                        Segments = new List<ChatStyledSegment>()
                    };
                    int insertIndex = index >= 0 ? index + 1 : _placedLines.Count;
                    _placedLines.Insert(insertIndex, newLine);
                    PlacedLinesListBox.SelectedIndex = insertIndex;
                    UpdateCanvas();

                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        var container = PlacedLinesListBox.ItemContainerGenerator.ContainerFromIndex(insertIndex);
                        if (container != null)
                        {
                            var newRtb = FindVisualChild<RichTextBox>(container);
                            newRtb?.Focus();
                        }
                    }), System.Windows.Threading.DispatcherPriority.Input);
                }
            }
        }

        private void PlacedLineRichText_Changed(object sender, TextChangedEventArgs e)
        {
            if (!_isLoaded) return;
            if (sender is not RichTextBox rtb) return;
            if (rtb.Tag is string s && s == "populating") return;
            if (rtb.DataContext is not PlacedChatLine line) return;

            SyncRichTextBoxToLine(rtb, line);
            UpdateCanvas();
        }

        private static void SyncRichTextBoxToLine(RichTextBox rtb, PlacedChatLine line)
        {
            var segments = new List<ChatStyledSegment>();
            var sb = new StringBuilder();

            foreach (var block in rtb.Document.Blocks)
            {
                if (block is Paragraph p)
                {
                    foreach (var inline in p.Inlines)
                    {
                        if (inline is Run run && !string.IsNullOrEmpty(run.Text))
                        {
                            string hex = "#FFFFFF";
                            if (run.Foreground is SolidColorBrush scb)
                            {
                                hex = $"#{scb.Color.R:X2}{scb.Color.G:X2}{scb.Color.B:X2}";
                            }

                            bool isCensored = false;
                            if (run.Background is SolidColorBrush bg &&
                                bg.Color.A > 0 &&
                                bg.Color.R <= 15 && bg.Color.G <= 15 && bg.Color.B <= 15)
                            {
                                isCensored = true;
                            }

                            segments.Add(new ChatStyledSegment(run.Text, hex,
                                run.FontWeight == FontWeights.Bold,
                                run.FontStyle == FontStyles.Italic,
                                isCensored));
                            sb.Append(run.Text);
                        }
                    }
                }
            }

            line.Segments = segments;
            line.RawText = sb.ToString();
            line.Reclassify();
        }

        private void DeletePlacedLine_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { DataContext: PlacedChatLine line })
            {
                _placedLines.Remove(line);
                UpdateCanvas();
            }
        }

        private void DragHandle_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement elem && elem.DataContext is PlacedChatLine line)
            {
                _isDraggingListLine = true;
                _draggedListLine = line;
                _dragStartListPos = e.GetPosition(PlacedLinesListBox);
            }
        }

        private void PlacedLinesListBox_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDraggingListLine || _draggedListLine == null || e.LeftButton != MouseButtonState.Pressed)
            {
                _isDraggingListLine = false;
                _draggedListLine = null;
                return;
            }

            Point currentPoint = e.GetPosition(PlacedLinesListBox);
            Vector diff = _dragStartListPos - currentPoint;

            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                try
                {
                    var data = new DataObject("PlacedChatLine", _draggedListLine);
                    DragDrop.DoDragDrop(PlacedLinesListBox, data, DragDropEffects.Move);
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "DragDrop reorder exception");
                }
                finally
                {
                    _isDraggingListLine = false;
                    _draggedListLine = null;
                }
            }
        }

        private void PlacedLinesListBox_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("PlacedChatLine"))
            {
                e.Effects = DragDropEffects.Move;

                Point pos = e.GetPosition(PlacedLinesListBox);
                var scrollViewer = GetScrollViewer(PlacedLinesListBox);
                if (scrollViewer != null)
                {
                    const double edge = 22.0;
                    if (pos.Y < edge)
                    {
                        scrollViewer.ScrollToVerticalOffset(Math.Max(0, scrollViewer.VerticalOffset - 4));
                    }
                    else if (pos.Y > PlacedLinesListBox.ActualHeight - edge)
                    {
                        scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + 4);
                    }
                }

                e.Handled = true;
            }
            else if (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.UnicodeText))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
        }

        private void PlacedLinesListBox_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("PlacedChatLine"))
            {
                if (e.Data.GetData("PlacedChatLine") is not PlacedChatLine sourceLine) return;

                Point dropPos = e.GetPosition(PlacedLinesListBox);
                var hit = PlacedLinesListBox.InputHitTest(dropPos) as DependencyObject;
                var targetContainer = FindVisualParent<ListBoxItem>(hit);

                int targetIndex = _placedLines.Count - 1;
                if (targetContainer != null)
                {
                    targetIndex = PlacedLinesListBox.ItemContainerGenerator.IndexFromContainer(targetContainer);
                    if (targetIndex < 0) targetIndex = _placedLines.Count - 1;
                }

                int sourceIndex = _placedLines.IndexOf(sourceLine);
                if (sourceIndex >= 0 && targetIndex >= 0 && sourceIndex != targetIndex)
                {
                    _placedLines.Move(sourceIndex, targetIndex);
                    PlacedLinesListBox.SelectedItem = sourceLine;
                    UpdateCanvas();
                }

                _isDraggingListLine = false;
                _draggedListLine = null;
                e.Handled = true;
                return;
            }

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0 && File.Exists(files[0]))
                {
                    try
                    {
                        string content = File.ReadAllText(files[0]);
                        InsertChatLines(content);
                        SetStatus($"Imported chat from {Path.GetFileName(files[0])}.");
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Failed to load dropped chat log file.");
                    }
                    e.Handled = true;
                    return;
                }
            }

            if (e.Data.GetDataPresent(DataFormats.UnicodeText))
            {
                string text = (string)e.Data.GetData(DataFormats.UnicodeText);
                InsertChatLines(text);
                e.Handled = true;
            }
        }

        private static ScrollViewer? GetScrollViewer(DependencyObject dep)
        {
            if (dep is ScrollViewer sv) return sv;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(dep); i++)
            {
                var child = VisualTreeHelper.GetChild(dep, i);
                var res = GetScrollViewer(child);
                if (res != null) return res;
            }
            return null;
        }

        private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
        {
            while (child != null)
            {
                if (child is T parent) return parent;
                child = VisualTreeHelper.GetParent(child);
            }
            return null;
        }

        private static T? FindVisualChild<T>(DependencyObject? parent) where T : DependencyObject
        {
            if (parent == null) return null;
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild) return typedChild;
                var result = FindVisualChild<T>(child);
                if (result != null) return result;
            }
            return null;
        }

        private void AddCustomLineBtn_Click(object sender, RoutedEventArgs e)
        {
            string defaultText = "* Character Name performs an action.";
            var placed = new PlacedChatLine
            {
                RawText = defaultText,
                Segments = RoleplayChatColorizer.ColorizeLine(defaultText)
            };

            int selectedIndex = PlacedLinesListBox.SelectedIndex;
            int insertIndex;
            if (selectedIndex >= 0 && selectedIndex < _placedLines.Count)
            {
                insertIndex = selectedIndex + 1;
                _placedLines.Insert(insertIndex, placed);
            }
            else
            {
                insertIndex = _placedLines.Count;
                _placedLines.Add(placed);
            }

            PlacedLinesListBox.SelectedIndex = insertIndex;
            PlacedLinesListBox.ScrollIntoView(placed);
            UpdateCanvas();
            SetStatus("Added line to canvas.");

            Dispatcher.BeginInvoke(new Action(() =>
            {
                var container = PlacedLinesListBox.ItemContainerGenerator.ContainerFromIndex(insertIndex);
                if (container != null)
                {
                    var rtb = FindVisualChild<RichTextBox>(container);
                    rtb?.Focus();
                }
            }), System.Windows.Threading.DispatcherPriority.Input);
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

            var grid = FindVisualParent<Grid>(elem);
            var rtb = grid?.Children.OfType<RichTextBox>().FirstOrDefault();

            bool hasSelection = rtb != null && !rtb.Selection.IsEmpty && !string.IsNullOrWhiteSpace(rtb.Selection.Text);
            TextRange? activeSelection = hasSelection ? new TextRange(rtb!.Selection.Start, rtb.Selection.End) : null;
            string? selectedSample = hasSelection ? (activeSelection!.Text.Trim().Length > 25 ? activeSelection.Text.Trim().Substring(0, 22) + "..." : activeSelection.Text.Trim()) : null;

            var menu = new ContextMenu
            {
                PlacementTarget = elem,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom
            };

            if (hasSelection)
            {
                var selHeader = new MenuItem
                {
                    Header = $"Recolor \"{selectedSample}\":",
                    IsEnabled = false,
                    FontWeight = FontWeights.SemiBold
                };
                menu.Items.Add(selHeader);
                menu.Items.Add(new Separator());
            }

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
                        BorderBrush = SwatchBorderBrush,
                        BorderThickness = new Thickness(1),
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center
                    }
                };

                string hex = swatch.Hex;
                string label = swatch.Label;
                item.Click += (_, __) =>
                {
                    if (rtb != null && activeSelection != null && !activeSelection.IsEmpty)
                    {
                        activeSelection.ApplyPropertyValue(TextElement.BackgroundProperty, Brushes.Transparent);
                        activeSelection.ApplyPropertyValue(TextElement.ForegroundProperty, FreezeBrush(hex));
                        line.ColorOverride = null;
                        SyncRichTextBoxToLine(rtb, line);
                        UpdateCanvas();
                        SetStatus($"Selection recolored to {label} ({hex}).");
                    }
                    else if (rtb != null)
                    {
                        var wholeDoc = new TextRange(rtb.Document.ContentStart, rtb.Document.ContentEnd);
                        wholeDoc.ApplyPropertyValue(TextElement.BackgroundProperty, Brushes.Transparent);
                        wholeDoc.ApplyPropertyValue(TextElement.ForegroundProperty, FreezeBrush(hex));
                        line.ColorOverride = hex;
                        SyncRichTextBoxToLine(rtb, line);
                        UpdateCanvas();
                        SetStatus($"Line color set to {label} ({hex}).");
                    }
                    else
                    {
                        line.ColorOverride = hex;
                        UpdateCanvas();
                        SetStatus($"Line color set to {label} ({hex}).");
                    }
                };

                menu.Items.Add(item);
            }

            menu.Items.Add(new Separator());

            var autoItem = new MenuItem
            {
                Header = hasSelection ? "Reset Selection to Detected Color" : "Auto (Detected Color)",
                ToolTip = "Reset to default detected roleplay color",
                Icon = new PackIconMaterial
                {
                    Kind = PackIconMaterialKind.AutoFix,
                    Width = 13,
                    Height = 13,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = (Brush)FindResource("MahApps.Brushes.ThemeForeground")
                }
            };
            autoItem.Click += (_, __) =>
            {
                if (rtb != null && activeSelection != null && !activeSelection.IsEmpty)
                {
                    activeSelection.ApplyPropertyValue(TextElement.BackgroundProperty, Brushes.Transparent);
                    string defaultColor = ChatLineClassifier.GetHexColor(line.Category);
                    activeSelection.ApplyPropertyValue(TextElement.ForegroundProperty, FreezeBrush(defaultColor));
                    line.ColorOverride = null;
                    SyncRichTextBoxToLine(rtb, line);
                    UpdateCanvas();
                    SetStatus($"Selection color reset to default ({defaultColor}).");
                }
                else
                {
                    line.ColorOverride = null;
                    line.Segments = RoleplayChatColorizer.ColorizeLine(line.RawText);
                    if (rtb != null) RichChatLineHelper.PopulateDocument(rtb, line);
                    UpdateCanvas();
                    SetStatus("Line color reset to detected color.");
                }
            };
            menu.Items.Add(autoItem);

            var customItem = new MenuItem
            {
                Header = hasSelection ? "Custom Color for Selection..." : "Custom Color...",
                ToolTip = "Choose any custom color from palette",
                Icon = new PackIconMaterial
                {
                    Kind = PackIconMaterialKind.Palette,
                    Width = 13,
                    Height = 13,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = (Brush)FindResource("MahApps.Brushes.ThemeForeground")
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
                        if (rtb != null && activeSelection != null && !activeSelection.IsEmpty)
                        {
                            activeSelection.ApplyPropertyValue(TextElement.BackgroundProperty, Brushes.Transparent);
                            activeSelection.ApplyPropertyValue(TextElement.ForegroundProperty, FreezeBrush(customHex));
                            line.ColorOverride = null;
                            SyncRichTextBoxToLine(rtb, line);
                            UpdateCanvas();
                            SetStatus($"Selection recolored to custom ({customHex}).");
                        }
                        else if (rtb != null)
                        {
                            var wholeDoc = new TextRange(rtb.Document.ContentStart, rtb.Document.ContentEnd);
                            wholeDoc.ApplyPropertyValue(TextElement.BackgroundProperty, Brushes.Transparent);
                            wholeDoc.ApplyPropertyValue(TextElement.ForegroundProperty, FreezeBrush(customHex));
                            line.ColorOverride = customHex;
                            SyncRichTextBoxToLine(rtb, line);
                            UpdateCanvas();
                            SetStatus($"Line color set to custom ({customHex}).");
                        }
                        else
                        {
                            line.ColorOverride = customHex;
                            UpdateCanvas();
                            SetStatus($"Line color set to custom ({customHex}).");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to open custom color dialog.");
                }
            };
            menu.Items.Add(customItem);

            menu.Items.Add(new Separator());

            var censorItem = new MenuItem
            {
                Header = hasSelection ? "Redact selection" : "Redact line",
                ToolTip = "Redact with a solid black spoiler bar (like Discord) to hide amounts, names, or spoilers",
                Icon = new PackIconMaterial
                {
                    Kind = PackIconMaterialKind.EyeOffOutline,
                    Width = 13,
                    Height = 13,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = (Brush)FindResource("MahApps.Brushes.ThemeForeground")
                }
            };
            censorItem.Click += (_, __) =>
            {
                if (rtb != null && activeSelection != null && !activeSelection.IsEmpty)
                {
                    activeSelection.ApplyPropertyValue(TextElement.BackgroundProperty, CensorFillBrush);
                    activeSelection.ApplyPropertyValue(TextElement.ForegroundProperty, CensorTextBrush);
                    line.ColorOverride = null;
                    SyncRichTextBoxToLine(rtb, line);
                    UpdateCanvas();
                    SetStatus("Selection redacted with black spoiler bar.");
                }
                else if (rtb != null)
                {
                    var wholeDoc = new TextRange(rtb.Document.ContentStart, rtb.Document.ContentEnd);
                    wholeDoc.ApplyPropertyValue(TextElement.BackgroundProperty, CensorFillBrush);
                    wholeDoc.ApplyPropertyValue(TextElement.ForegroundProperty, CensorTextBrush);
                    SyncRichTextBoxToLine(rtb, line);
                    UpdateCanvas();
                    SetStatus("Line redacted with black spoiler bar.");
                }
                else
                {
                    foreach (var s in line.Segments) s.IsCensored = true;
                    UpdateCanvas();
                    SetStatus("Line redacted with black spoiler bar.");
                }
            };
            menu.Items.Add(censorItem);

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
            if (!_isLoaded || _isUpdatingUi) return;
            UpdateCanvas();
            SaveSettings();
        }

        private void StylingSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isLoaded || _isUpdatingUi) return;

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

        private void SaveImageBtn_Click(object sender, RoutedEventArgs e)
        {
            if (CanvasPreviewImage.Source is not BitmapSource bmp) return;

            var dialog = new SaveFileDialog
            {
                Title = "Save Screenshot",
                Filter = "PNG Image (*.png)|*.png|JPEG Image (*.jpg;*.jpeg)|*.jpg;*.jpeg|All Files (*.*)|*.*",
                FilterIndex = 1,
                DefaultExt = ".png",
                FileName = $"GTAW_Screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png"
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                string ext = Path.GetExtension(dialog.FileName).ToLowerInvariant();
                bool isJpeg = ext is ".jpg" or ".jpeg" || (dialog.FilterIndex == 2 && ext != ".png");

                if (isJpeg)
                {
                    ScreenshotRenderer.SaveToJpeg(bmp, dialog.FileName, 95);
                    SetStatus($"Saved {Path.GetFileName(dialog.FileName)} (JPEG).");
                }
                else
                {
                    ScreenshotRenderer.SaveToPng(bmp, dialog.FileName);
                    SetStatus($"Saved {Path.GetFileName(dialog.FileName)} (PNG).");
                }
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
                var focused = FocusManager.GetFocusedElement(this);
                if (focused is not TextBox && focused is not RichTextBox)
                {
                    if (Clipboard.ContainsImage())
                    {
                        PasteImageFromClipboard();
                    }
                    else if (Clipboard.ContainsText())
                    {
                        PasteChatFromClipboard();
                    }
                    e.Handled = true;
                }
            }
            else if (ctrl && !shift && e.Key == Key.S)
            {
                if (_backgroundImage != null)
                {
                    SaveImageBtn_Click(sender, e);
                }
                e.Handled = true;
            }
            else if (ctrl && shift && e.Key == Key.C)
            {
                if (_backgroundImage != null)
                {
                    CopyToClipboardBtn_Click(sender, e);
                }
                e.Handled = true;
            }
        }
    }
}
