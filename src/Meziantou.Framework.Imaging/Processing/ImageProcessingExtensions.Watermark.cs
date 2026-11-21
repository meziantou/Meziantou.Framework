using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Imaging.Typography;

namespace Meziantou.Framework.Imaging;

// Watermark: a text drawn once (in the center, in a corner or against an edge) or repeated across the image
public static partial class ImageProcessingExtensions
{
    // The size the text is laid out at to measure it, when the font size is computed from the image
    private const double WatermarkReferenceSize = 100;

    /// <summary>
    /// Draws a text watermark, such as "DRAFT" or "CONFIDENTIAL", on every frame of the image (including the poster frame),
    /// in place: once, in the center, in a corner or against an edge, or repeated across the whole image.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The text is laid out once, with the fonts of <paramref name="options"/>, and placed by the box of what it draws
    /// (its ink), turned by <see cref="ImageWatermarkOptions.Rotation"/>: a centered watermark has the center of that box
    /// on the center of the image, and a watermark in a corner or against an edge keeps the rotated box at
    /// <see cref="ImageWatermarkOptions.Margin"/> from the edges. A tiled watermark repeats the text in rows along the
    /// rotated text, each row offset by half a copy, with a copy in the center of the image.
    /// </para>
    /// <para>
    /// The text is painted with <see cref="ImageWatermarkOptions.Color"/>, faded by <see cref="ImageWatermarkOptions.Opacity"/>,
    /// over the pixels, with the arithmetic of the drawings (see <see cref="DrawingCanvas"/>). Every frame of an
    /// animation gets the same watermark. A text whose characters draw nothing, such as spaces, leaves the image unchanged.
    /// The options and the text are validated before any pixel changes; on cancellation, some frames may already be changed.
    /// An image that is not yet upright (a photo with an EXIF orientation) should be turned with <see cref="AutoOrient"/> first.
    /// </para>
    /// </remarks>
    /// <param name="image">The image to modify.</param>
    /// <param name="text">The text of the watermark.</param>
    /// <param name="options">The fonts, the size, the color and the placement of the text.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <exception cref="ArgumentNullException"><paramref name="image"/>, <paramref name="text"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="text"/> is empty, or a character of the text is in none of the fonts.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The watermark is tiled and a copy of the text, with its spacing, is larger than 16777216 pixels.</exception>
    /// <exception cref="InvalidImageContentException">The data of a glyph of the text, or a layout table of a font, is malformed.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The pixels of the image are CMYK, or the outlines of a font use a feature that is not supported.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ImageResourceLimitException">The text exceeds a limit of the fonts, or the working storage would exceed the allocation limit.</exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../samples/Meziantou.Framework.Imaging.Samples/GuideExamples.cs" ref:region="Imaging_AddWatermark" ref:dedent="true">
    /// public static void AddWatermark(string fontPath, string inputPath, string outputPath)
    /// {
    ///     using var image = Image.Load(inputPath);
    ///
    ///     // By default, the text is gray at 30% opacity, along the diagonal, and as large as 80% of the image allows
    ///     image.AddWatermark("DRAFT", new ImageWatermarkOptions(Font.Load(fontPath)));
    ///     image.Save(outputPath);
    /// }
    /// </code>
    /// </example>
    public static void AddWatermark(this Image image, string text, ImageWatermarkOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        ThrowIfCmyk(image.PixelFormat, nameof(AddWatermark));
        var watermark = Watermark.Create(text, options, image.Size);
        image.Draw(watermark, static (canvas, state) => state?.Draw(canvas), cancellationToken);
    }

    /// <summary>Draws a text watermark on one frame, in place. See <see cref="AddWatermark(Image, string, ImageWatermarkOptions, CancellationToken)"/>.</summary>
    /// <param name="frame">The frame to modify.</param>
    /// <param name="text">The text of the watermark.</param>
    /// <param name="options">The fonts, the size, the color and the placement of the text.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/>, <paramref name="text"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="text"/> is empty, or a character of the text is in none of the fonts.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The watermark is tiled and a copy of the text, with its spacing, is larger than 16777216 pixels.</exception>
    /// <exception cref="InvalidImageContentException">The data of a glyph of the text, or a layout table of a font, is malformed.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The pixels of the frame are CMYK, or the outlines of a font use a feature that is not supported.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ImageResourceLimitException">The text exceeds a limit of the fonts, or the working storage would exceed the allocation limit.</exception>
    /// <exception cref="ObjectDisposedException">The frame is no longer attached to a live image.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public static void AddWatermark(this ImageFrame frame, string text, ImageWatermarkOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ThrowIfCmyk(frame.PixelFormat, nameof(AddWatermark));
        var watermark = Watermark.Create(text, options, frame.Size);
        frame.Draw(watermark, static (canvas, state) => state?.Draw(canvas), cancellationToken);
    }

    /// <summary>The text of a watermark, laid out, and where it goes on a canvas of a size.</summary>
    private sealed class Watermark
    {
        private Watermark(Brush brush, TextLayout layout, AffineTransform transform)
        {
            Brush = brush;
            Layout = layout;
            Transform = transform;
        }

        // The brush of the text, and the layout drawn at the origin of the transformation
        public Brush Brush { get; }

        public TextLayout Layout { get; }

        public AffineTransform Transform { get; }

        // Returns null when the text draws nothing
        public static Watermark? Create(string text, ImageWatermarkOptions options, Size size)
        {
            ArgumentException.ThrowIfNullOrEmpty(text);
            ArgumentNullException.ThrowIfNull(options);

            double width = size.Width;
            double height = size.Height;
            var tiled = options.IsTiled;
            var position = tiled ? WatermarkPosition.Center : options.Position;
            // The angles and the box of the turned text come from the rotation that draws it, computed without the
            // trigonometric functions of the platform: the pixels are the same on every platform. A positive angle of
            // AffineTransform turns clockwise on the image, the angle of the options counterclockwise.
            var angle = options.Rotation ?? (position == WatermarkPosition.Center ? DeterministicAngle.Atan2(height, width) * 180 : 0);
            var rotation = AffineTransform.CreateRotation(-angle);
            var cos = Math.Abs(rotation.M11);
            var sin = Math.Abs(rotation.M12);

            var layout = TextLayout.Create(text, new TextStyle(options.Fonts, options.FontSize ?? WatermarkReferenceSize));
            if (layout.HasMissingGlyphs)
                throw new ArgumentException("A character of the text is in none of the fonts of the watermark.", nameof(text));

            if (options.FontSize is null)
            {
                double fontSize;
                if (!tiled && position == WatermarkPosition.Center)
                {
                    // The size whose rotated ink box fits in 80% of the image
                    var ink = layout.InkBounds;
                    if (ink.IsEmpty)
                        return null;

                    var inkWidth = ink.Width / WatermarkReferenceSize;
                    var inkHeight = ink.Height / WatermarkReferenceSize;
                    fontSize = 0.8 * Math.Min(width / ((cos * inkWidth) + (sin * inkHeight)), height / ((sin * inkWidth) + (cos * inkHeight)));
                }
                else
                {
                    fontSize = Math.Sqrt((width * width) + (height * height)) / 20;
                }

                fontSize = Math.Min(fontSize, ImageWatermarkOptions.MaxFontSize);
                if (!(fontSize > 0))
                    return null;

                if (fontSize != WatermarkReferenceSize)
                {
                    layout = TextLayout.Create(text, new TextStyle(options.Fonts, fontSize));
                }
            }

            var bounds = layout.InkBounds;
            if (bounds.IsEmpty)
                return null;

            // The center of the ink at the origin, then the rotation
            var centered = AffineTransform.CreateTranslation(-(bounds.X + (bounds.Width / 2)), -(bounds.Y + (bounds.Height / 2)))
                .Then(rotation);

            if (tiled)
            {
                var spacing = options.TileSpacing ?? bounds.Height;
                var cellWidth = bounds.Width + spacing;
                var rowHeight = bounds.Height + spacing;
                if (!(cellWidth <= ImageWatermarkOptions.MaxLength && 2 * rowHeight <= ImageWatermarkOptions.MaxLength))
                    throw new ArgumentOutOfRangeException(nameof(options), "A copy of the tiled text, with its spacing, is larger than 16777216 pixels.");

                // The tile holds two rows: a copy at its top-left corner, and below it a copy offset by half a cell,
                // split between the two sides of the tile. The ink of the first copy is at the origin of the tile.
                var textBrush = new SolidBrush(options.Color);
                var origin = new PointD(-bounds.X, -bounds.Y);
                var pattern = new PatternBrush(new RectangleD(0, 0, cellWidth, 2 * rowHeight), canvas =>
                {
                    canvas.FillText(textBrush, layout, origin);
                    canvas.FillText(textBrush, layout, new PointD(origin.X - (cellWidth / 2), origin.Y + rowHeight));
                    canvas.FillText(textBrush, layout, new PointD(origin.X + (cellWidth / 2), origin.Y + rowHeight));
                })
                {
                    Opacity = options.Opacity,
                    Transform = AffineTransform.CreateTranslation(-(bounds.Width / 2), -(bounds.Height / 2))
                        .Then(rotation)
                        .Then(AffineTransform.CreateTranslation(width / 2, height / 2)),
                };

                return new Watermark(pattern, layout, transform: default);
            }

            // The box of the rotated ink, against the edges the position names
            var boxWidth = (cos * bounds.Width) + (sin * bounds.Height);
            var boxHeight = (sin * bounds.Width) + (cos * bounds.Height);
            var margin = options.Margin ?? (Math.Min(width, height) / 40);
            var x = position switch
            {
                WatermarkPosition.TopLeft or WatermarkPosition.Left or WatermarkPosition.BottomLeft => margin + (boxWidth / 2),
                WatermarkPosition.TopRight or WatermarkPosition.Right or WatermarkPosition.BottomRight => width - margin - (boxWidth / 2),
                _ => width / 2,
            };
            var y = position switch
            {
                WatermarkPosition.TopLeft or WatermarkPosition.Top or WatermarkPosition.TopRight => margin + (boxHeight / 2),
                WatermarkPosition.BottomLeft or WatermarkPosition.Bottom or WatermarkPosition.BottomRight => height - margin - (boxHeight / 2),
                _ => height / 2,
            };

            var brush = new SolidBrush(options.Color) { Opacity = options.Opacity };
            return new Watermark(brush, layout, centered.Then(AffineTransform.CreateTranslation(x, y)));
        }

        public void Draw(DrawingCanvas canvas)
        {
            if (Brush is PatternBrush)
            {
                canvas.Fill(Brush);
                return;
            }

            canvas.Transform(Transform, this, static (turned, state) => turned.FillText(state.Brush, state.Layout, default));
        }
    }
}
