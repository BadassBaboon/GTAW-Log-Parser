using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GTAWParser.Shared.Screenshot
{
    public class ScreenshotRenderOptions
    {
        public int CanvasWidth { get; set; } = 1920;
        public int CanvasHeight { get; set; } = 1080;

        // Image background transform
        public double ImageOffsetX { get; set; } = 0;
        public double ImageOffsetY { get; set; } = 0;
        public double ImageScale { get; set; } = 1.0;

        // Chat overlay settings
        public double ChatX { get; set; } = 30;
        public double ChatY { get; set; } = 30;

        // Typography
        public string FontFamily { get; set; } = "Arial";
        public double FontSize { get; set; } = 14.0;
        public bool IsBold { get; set; } = true;
        public double LineSpacing { get; set; } = 4.0;

        // Stroke & Shadow
        public double OutlineWidth { get; set; } = 1.2;
        public Color OutlineColor { get; set; } = Colors.Black;
        public bool EnableDropShadow { get; set; } = true;
        public double ShadowOffset { get; set; } = 1.0;
        public double ShadowOpacity { get; set; } = 0.8;

        /// <summary>
        /// Leaves the canvas transparent where neither the image nor the chat covers it, instead of
        /// filling it black. The editor pairs this with a checkerboard so an image dragged off-axis
        /// is obvious, and PNG export preserves the alpha. JPEG has no alpha and is flattened.
        /// </summary>
        public bool TransparentBackground { get; set; }

        // Background Box / Vignette
        public bool EnableBackgroundBox { get; set; } = false;
        public double BackgroundBoxOpacity { get; set; } = 0.45;
        public double BackgroundBoxPadding { get; set; } = 8.0;
        public double BackgroundBoxCornerRadius { get; set; } = 4.0;
    }

    public static class ScreenshotRenderer
    {
        private static Typeface BuildTypeface(ScreenshotRenderOptions options) => new Typeface(
            new FontFamily(options.FontFamily),
            FontStyles.Normal,
            options.IsBold ? FontWeights.Bold : FontWeights.Normal,
            FontStretches.Normal
        );

        private static FormattedText Format(string text, Typeface typeface, ScreenshotRenderOptions options, Brush brush) =>
            new FormattedText(
                text,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                options.FontSize,
                brush,
                1.0
            );

        /// <summary>
        /// The rectangle the chat block occupies on the canvas, in canvas pixels. Used both to draw
        /// the optional background box and by the editor to hit-test and outline the block, so the
        /// two can never disagree about where the text actually is.
        /// </summary>
        public static Rect MeasureChatBlock(
            List<List<ChatStyledSegment>>? lines,
            ScreenshotRenderOptions options)
        {
            if (lines == null || lines.Count == 0)
                return new Rect(options.ChatX, options.ChatY, 0, 0);

            var typeface = BuildTypeface(options);
            double maxLineWidth = 0;
            double totalHeight = 0;

            foreach (var line in lines)
            {
                double lineWidth = 0;
                double lineHeight = options.FontSize;

                foreach (var seg in line)
                {
                    if (string.IsNullOrEmpty(seg.Text)) continue;
                    var ft = Format(seg.Text, typeface, options, Brushes.White);
                    lineWidth += ft.Width;
                    lineHeight = Math.Max(lineHeight, ft.Height);
                }

                maxLineWidth = Math.Max(maxLineWidth, lineWidth);
                totalHeight += lineHeight + options.LineSpacing;
            }

            totalHeight = Math.Max(0, totalHeight - options.LineSpacing);
            return new Rect(options.ChatX, options.ChatY, maxLineWidth, totalHeight);
        }

        /// <summary>
        /// Renders the complete composite screenshot with background image and GTAW chat typography.
        /// </summary>
        public static RenderTargetBitmap Render(
            BitmapSource? backgroundImage,
            List<List<ChatStyledSegment>>? lines,
            ScreenshotRenderOptions options)
        {
            int width = Math.Max(100, options.CanvasWidth);
            int height = Math.Max(100, options.CanvasHeight);

            var drawingVisual = new DrawingVisual();
            using (DrawingContext dc = drawingVisual.RenderOpen())
            {
                // 1. Ground the canvas. Left transparent when the caller wants to see through to
                //    whatever sits behind it, such as the editor's checkerboard.
                if (!options.TransparentBackground)
                {
                    dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, width, height));
                }

                // 2. Draw transformed background image
                if (backgroundImage != null)
                {
                    dc.PushClip(new RectangleGeometry(new Rect(0, 0, width, height)));

                    double imgW = backgroundImage.PixelWidth * options.ImageScale;
                    double imgH = backgroundImage.PixelHeight * options.ImageScale;
                    double imgX = options.ImageOffsetX;
                    double imgY = options.ImageOffsetY;

                    dc.DrawImage(backgroundImage, new Rect(imgX, imgY, imgW, imgH));
                    dc.Pop(); // Pop clip
                }

                // 3. Draw Chat Overlay
                if (lines != null && lines.Count > 0)
                {
                    DrawChatOverlay(dc, lines, options);
                }
            }

            var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(drawingVisual);
            return rtb;
        }

        private static void DrawChatOverlay(
            DrawingContext dc,
            List<List<ChatStyledSegment>> lines,
            ScreenshotRenderOptions options)
        {
            var typeface = BuildTypeface(options);
            double currentY = options.ChatY;

            if (options.EnableBackgroundBox)
            {
                Rect block = MeasureChatBlock(lines, options);
                double pad = options.BackgroundBoxPadding;

                var boxBrush = new SolidColorBrush(Color.FromArgb((byte)(options.BackgroundBoxOpacity * 255), 0, 0, 0));
                boxBrush.Freeze();

                var boxRect = new Rect(
                    block.X - pad,
                    block.Y - pad,
                    block.Width + (pad * 2),
                    block.Height + (pad * 2)
                );

                dc.DrawRoundedRectangle(boxBrush, null, boxRect, options.BackgroundBoxCornerRadius, options.BackgroundBoxCornerRadius);
            }

            var outlineBrush = new SolidColorBrush(options.OutlineColor);
            outlineBrush.Freeze();

            var shadowBrush = new SolidColorBrush(Color.FromArgb((byte)(options.ShadowOpacity * 255), 0, 0, 0));
            shadowBrush.Freeze();

            foreach (var line in lines)
            {
                double currentX = options.ChatX;
                double maxLineHeight = options.FontSize;

                foreach (var seg in line)
                {
                    if (string.IsNullOrEmpty(seg.Text))
                        continue;

                    Color fgColor = Colors.White;
                    try
                    {
                        var converted = ColorConverter.ConvertFromString(seg.ColorHex);
                        if (converted != null) fgColor = (Color)converted;
                    }
                    catch { }

                    var segBrush = new SolidColorBrush(fgColor);
                    segBrush.Freeze();

                    var formattedText = Format(seg.Text, typeface, options, segBrush);
                    maxLineHeight = Math.Max(maxLineHeight, formattedText.Height);

                    if (seg.IsCensored)
                    {
                        // Discord-style solid black spoiler redaction bar with subtle rounding
                        double padX = 1.0;
                        double padY = 1.0;
                        var censorRect = new Rect(
                            currentX - padX,
                            currentY - padY,
                            formattedText.Width + (padX * 2),
                            formattedText.Height + (padY * 2)
                        );
                        dc.DrawRoundedRectangle(Brushes.Black, null, censorRect, 2.0, 2.0);
                    }
                    else
                    {
                        // 1. Eight-directional outline pass, mimicking the GTA chat font border.
                        double ow = options.OutlineWidth;
                        if (ow > 0)
                        {
                            var outlineFt = Format(seg.Text, typeface, options, outlineBrush);
                            for (double dx = -ow; dx <= ow; dx += ow)
                            {
                                for (double dy = -ow; dy <= ow; dy += ow)
                                {
                                    if (dx == 0 && dy == 0) continue;
                                    dc.DrawText(outlineFt, new Point(currentX + dx, currentY + dy));
                                }
                            }
                        }

                        // 2. Soft drop shadow.
                        if (options.EnableDropShadow && options.ShadowOffset > 0)
                        {
                            var shadowFt = Format(seg.Text, typeface, options, shadowBrush);
                            dc.DrawText(shadowFt, new Point(currentX + options.ShadowOffset, currentY + options.ShadowOffset));
                        }

                        // 3. Draw foreground text
                        dc.DrawText(formattedText, new Point(currentX, currentY));
                    }

                    currentX += formattedText.Width;
                }

                currentY += maxLineHeight + options.LineSpacing;
            }
        }

        /// <summary>
        /// Composites a possibly-transparent bitmap onto black, for formats that cannot store alpha.
        /// </summary>
        public static BitmapSource Flatten(BitmapSource bitmap)
        {
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                var bounds = new Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight);
                dc.DrawRectangle(Brushes.Black, null, bounds);
                dc.DrawImage(bitmap, bounds);
            }

            var flattened = new RenderTargetBitmap(
                bitmap.PixelWidth, bitmap.PixelHeight, 96, 96, PixelFormats.Pbgra32);
            flattened.Render(visual);
            return flattened;
        }

        /// <summary>
        /// Encodes and saves a BitmapSource to a PNG file.
        /// </summary>
        public static void SaveToPng(BitmapSource bitmap, string filePath)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            {
                encoder.Save(stream);
            }
        }

        /// <summary>
        /// Encodes and saves a BitmapSource to a JPEG file with quality level (1-100).
        /// </summary>
        public static void SaveToJpeg(BitmapSource bitmap, string filePath, int quality = 95)
        {
            var encoder = new JpegBitmapEncoder { QualityLevel = Math.Clamp(quality, 10, 100) };
            encoder.Frames.Add(BitmapFrame.Create(Flatten(bitmap)));
            using (var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            {
                encoder.Save(stream);
            }
        }
    }
}
