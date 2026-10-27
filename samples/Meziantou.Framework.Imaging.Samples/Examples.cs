using System.Runtime.CompilerServices;
using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.Typography;

namespace Meziantou.Framework.Imaging.Samples;

/// <summary>
/// Representative usage of the public API, shown by the package README (its regions), and run by
/// the unit tests (<c>DocumentationExampleTests</c>).
/// </summary>
internal static class Examples
{
    /// <summary>Eager animated resize: every frame and the poster frame are resized atomically; timing is preserved.</summary>
    #region Imaging_ResizeAnimation
    public static void ResizeAnimation(string inputPath, string outputPath)
    {
        using var image = Image.Load(inputPath);
        image.Resize(new ResizeOptions(320, 240) { Mode = ResizeMode.Contain, Filter = ResamplingFilter.Lanczos3 });

        // Same format as the extension; APNG/GIF timing and play count come from the decoded image
        image.Save(outputPath);
    }
    #endregion

    /// <summary>Letterboxing: fits an image inside a box, pads it to exactly that size, and adds a border.</summary>
    #region Imaging_Letterbox
    public static void Letterbox(string inputPath, string outputPath)
    {
        using var image = Image.Load(inputPath);

        // Fits inside 320x240, then pads to exactly 320x240 with black bars, the image centered (ResizeOptions.Anchor).
        // The padding is transparent by default, which a pixel format without alpha (an RGB JPEG) cannot store
        image.Resize(new ResizeOptions(320, 240) { Mode = ResizeMode.Pad, BackgroundColor = new Rgba64(0, 0, 0) });

        // A 4-pixel white frame on every side: 328x248
        image.Pad(4, 4, 4, 4, new Rgba64(65535, 65535, 65535));
        image.Save(outputPath);
    }
    #endregion

    /// <summary>Straightens a tilted scan: turns it around its center, and fills the corners the turn uncovers with white.</summary>
    #region Imaging_Straighten
    public static void Straighten(string inputPath, string outputPath, double degrees)
    {
        using var image = Image.Load(inputPath);

        // The canvas grows to hold the whole turned image. A JPEG image has no alpha: the corners need an opaque color
        image.Rotate(degrees, new TransformOptions { Filter = ResamplingFilter.Lanczos3, BackgroundColor = new Rgba64(65535, 65535, 65535, 65535) });
        image.Save(outputPath);
    }
    #endregion

    /// <summary>Flattens a page photographed in perspective, from the position of its four corners in the photo.</summary>
    #region Imaging_FlattenPage
    public static void FlattenPage(string inputPath, string outputPath, PointD topLeft, PointD topRight, PointD bottomRight, PointD bottomLeft, Size pageSize)
    {
        using var image = Image.Load(inputPath);

        // The page seen in perspective, inverted: from the photo to the page
        var page = new RectangleD(0, 0, pageSize.Width, pageSize.Height);
        var pageToPhoto = ProjectiveTransform.CreateFromQuadrilateral(page, topLeft, topRight, bottomRight, bottomLeft);
        if (!pageToPhoto.TryInvert(out var photoToPage))
            throw new ArgumentException("The corners do not delimit a page.", nameof(topLeft));

        // The canvas is the page: what the photo shows around it is cut
        image.Transform(photoToPage, new TransformOptions { CanvasMode = TransformCanvasMode.Explicit, CanvasSize = pageSize, BackgroundColor = new Rgba64(65535, 65535, 65535, 65535) });
        image.Save(outputPath);
    }
    #endregion

    /// <summary>Auto-crop: trims the uniform background around the content and keeps a margin; returns whether the image changed.</summary>
    #region Imaging_AutoCrop
    public static bool TrimBackground(string inputPath, string outputPath)
    {
        using var image = Image.Load(inputPath);

        // The detection works on the stored pixels: apply the EXIF orientation first
        image.AutoOrient();

        // Read-only: the background color and the bounding box of everything else, in every frame
        var options = new AutoCropOptions { PaddingX = 8, PaddingY = 8, BucketThreshold = 0.945 };
        var analysis = image.AnalyzeAutoCrop(options);
        if (!analysis.Success)
            return false; // no uniform border, or no content of at least 3x3 pixels

        // Keeps 8 pixels around the content. Where the canvas is too small for the margin, it is enlarged and filled with
        // the background color (AutoCropPaddingMode.Contain clamps the margin instead)
        var changed = image.AutoCrop(analysis, options);
        image.Save(outputPath);
        return changed;
    }
    #endregion

    /// <summary>Convolution matrices: a sharpening matrix on a copy, then edge detection with the alpha of the pixels kept.</summary>
    #region Imaging_Convolve
    public static void SharpenAndDetectEdges(string inputPath, string sharpenedPath, string edgesPath)
    {
        using var image = Image.Load(inputPath);

        // The matrix is applied as written, row by row; weights that add up to 1 keep the overall brightness
        var sharpen = new ConvolutionKernel(3, 3, [0, -1, 0, -1, 5, -1, 0, -1, 0]);
        using (var sharpened = image.Clone())
        {
            sharpened.Convolve(new ConvolutionOptions(sharpen));
            sharpened.Save(sharpenedPath);
        }

        // Weights that add up to 0 would also filter alpha down to 0 (transparent black): keep the alpha instead
        var laplacian = new ConvolutionKernel(3, 3, [0, 1, 0, 1, -4, 1, 0, 1, 0]);
        image.Convolve(new ConvolutionOptions(laplacian) { PreserveAlpha = true, EdgeMode = ConvolutionEdgeMode.Mirror });
        image.Save(edgesPath);
    }
    #endregion

    /// <summary>A scan cleaned up by a median and an unsharp mask, then a soft background made by a box blur and a separable matrix.</summary>
    #region Imaging_BlurAndSharpen
    public static void CleanUpScan(string inputPath, string cleanedPath, string backgroundPath)
    {
        using var image = Image.Load(inputPath);

        // The median of every 3x3 square removes isolated specks and keeps edges. The unsharp mask then adds half the
        // difference from a Gaussian blur of deviation 1.5, except where it is under 2% of the range: flat paper stays flat
        image.MedianBlur(1);
        image.Sharpen(1.5, amount: 0.5, threshold: 0.02);
        image.Save(cleanedPath);

        // A box blur costs the same whatever its radius; a separable matrix is applied to the rows, then to the columns
        image.BoxBlur(25, ConvolutionEdgeMode.Mirror);
        image.Convolve(new ConvolutionOptions(ConvolutionKernel.CreateSeparable([0.25, 0.5, 0.25], [0.25, 0.5, 0.25])));
        image.Save(backgroundPath);
    }
    #endregion

    /// <summary>New colors for an image, for a part of it, and for a drawing.</summary>
    #region Imaging_ColorMatrix
    public static void ApplyColorFilters(string photoPath, string iconPath, string outputPath)
    {
        using var photo = Image.Load(photoPath);
        using var icon = Image.Load(iconPath);

        // Matrices are immutable: create them once. Two matrices become one, which is applied once per pixel
        var warm = ColorMatrix.CreateSaturation(0.6).Then(ColorMatrix.CreateSepia(0.4)).Then(ColorMatrix.CreateContrast(1.1));
        var dim = ColorMatrix.CreateBrightness(0.5);
        var accent = ColorMatrix.CreateTint(Colors.Gold);

        // The whole image, every frame of it
        photo.ApplyColorMatrix(warm);

        photo.Draw(canvas =>
        {
            // What is already drawn, inside a clip: the lower third of the photo is darker
            var band = new Rectangle(0, canvas.Height * 2 / 3, canvas.Width, canvas.Height - (canvas.Height * 2 / 3));
            canvas.Clip(band, clipped => clipped.FilterBackdrop(dim));

            // What a callback draws: the icon keeps its shape and its transparency, and gets one color
            canvas.Filter(accent, tinted => tinted.DrawImage(icon.Frames[0], new PointD(8, band.Y + 8)));
        });

        photo.Save(outputPath);
    }
    #endregion

    /// <summary>Image comparison: two files look the same when every frame is similar enough to the expected one.</summary>
    #region Imaging_ImageSimilarity
    public static bool LookTheSame(string expectedPath, string actualPath)
    {
        using var expected = Image.Load(expectedPath);
        using var actual = Image.Load(actualPath);
        if (expected.Size != actual.Size || expected.Frames.Count != actual.Frames.Count)
            return false;

        for (var i = 0; i < expected.Frames.Count; i++)
        {
            // 1 for identical pixels; small differences, such as anti-aliasing, keep the score close to 1
            if (ImageSimilarity.ComputeSsim(expected.Frames[i], actual.Frames[i]) < 0.99)
                return false;

            // 0 for the same structure and brightness, up to 64; the pHash is the most tolerant to small changes
            if (ImageSimilarity.ComputePerceptualHashDistance(expected.Frames[i], actual.Frames[i]) > 4)
                return false;

            // Perceptual metrics: 100 for identical pixels, about 90 when the difference is not visible
            if (ImageSimilarity.ComputeSsimulacra2(expected.Frames[i], actual.Frames[i]) < 90)
                return false;

            // 0 for identical pixels, below about 1 when the difference is not visible, even in a small area
            if (ImageSimilarity.ComputeButteraugliDistance(expected.Frames[i], actual.Frames[i]).MaxDistance > 1)
                return false;
        }

        return true;
    }
    #endregion

    /// <summary>Drawing: gradients, a disk, a filled and stroked curve and a frame on a new image.</summary>
    #region Imaging_DrawShapes
    public static void DrawLandscape(string outputPath)
    {
        using var image = new Image<Rgba32>(256, 160);

        // Brushes, pens and paths are immutable: create them once and reuse them
        var sky = new LinearGradientBrush(new PointD(0, 0), new PointD(0, 160), [new GradientStop(0, new Rgba32(30, 60, 160)), new GradientStop(1, new Rgba32(200, 220, 255))]);
        var sun = new RadialGradientBrush(new PointD(200, 50), 30, [new GradientStop(0, new Rgba32(255, 255, 200)), new GradientStop(1, new Rgba32(255, 180, 0, 0))]);
        var hill = new VectorPathBuilder()
            .MoveTo(0, 160).LineTo(0, 120)
            .CubicBezierTo(60, 70, 120, 150, 256, 100)
            .LineTo(256, 160).Close()
            .Build();
        var outline = new Pen(new Rgba32(20, 60, 20), 3) { LineJoin = LineJoin.Round };
        var frame = new Pen(new Rgba32(0, 0, 0, 128), 2);

        image.Frames[0].Draw(canvas =>
        {
            canvas.Fill(sky); // the whole canvas
            canvas.FillCircle(sun, new PointD(200, 50), 30); // fades to transparent at its edge
            canvas.Fill(new Rgba32(60, 140, 60), hill); // a color converts to a solid brush
            canvas.Stroke(outline, hill);
            canvas.StrokeRectangle(frame, new RectangleD(1, 1, 254, 158)); // centered on its path: from 0 to 2
        });

        image.Save(outputPath);
    }
    #endregion

    /// <summary>Drawing on every frame of an animation, with a static callback (no closure allocation).</summary>
    #region Imaging_DrawOnAnimation
    public static void MarkAnimation(string inputPath, string outputPath)
    {
        using var image = Image.Load(inputPath);
        var marker = new Pen(new Rgba32(255, 255, 0, 200), 4) { LineCap = LineCap.Round };

        // Image.Draw paints every frame and the poster identically; the callback runs once
        image.Draw(marker, static (canvas, pen) =>
        {
            // Coordinates are in pixels, with sub-pixel precision; shapes are clipped to the canvas
            var center = new PointD(canvas.Width / 2.0, canvas.Height / 2.0);
            var radius = Math.Min(canvas.Width, canvas.Height) / 4.0;
            canvas.StrokeCircle(pen, center, radius);
            canvas.StrokeLine(pen, new PointD(center.X - radius, center.Y), new PointD(center.X + radius, center.Y));
        });

        image.Save(outputPath);
    }
    #endregion

    /// <summary>Composing a picture from another image: a clip, a rounded rectangle, a dashed line and an arc.</summary>
    #region Imaging_DrawCard
    public static void ComposeCard(string photoPath, string outputPath)
    {
        using var photo = Image.Load(photoPath);
        using var card = new Image<Rgba32>(320, 120);

        var shade = new LinearGradientBrush(new PointD(0, 0), new PointD(0, 120), [new GradientStop(0, Rgba32.ParseHex("#2B5876")), new GradientStop(1, Rgba32.ParseHex("#4E4376"))]);
        var portrait = new VectorPathBuilder().AddCircle(new PointD(60, 60), 40).Build();
        var ring = new Pen(Colors.White, 3);
        var rule = new Pen(new Rgba32(255, 255, 255, 160), 2) { DashPattern = [6, 4] };
        var gauge = new Pen(Colors.Gold, 6) { LineCap = LineCap.Round };

        card.Frames[0].Draw(canvas =>
        {
            // The pixels outside the rounded corners stay transparent
            canvas.FillRoundedRectangle(shade, new Rectangle(0, 0, 320, 120), 16);

            // The photo is stretched to the square around the circle, and only changes the inside of the circle
            canvas.Clip(portrait, clipped => clipped.DrawImage(photo.Frames[0], new Rectangle(20, 20, 80, 80)));
            canvas.Stroke(ring, portrait);

            // Dashes of 6 pixels and gaps of 4, then three quarters of a circle from its top, clockwise
            canvas.StrokeLine(rule, new PointD(120, 60), new PointD(300, 60));
            canvas.StrokeArc(gauge, new PointD(270, 30), 14, -90, 270);
        });

        card.Save(outputPath);
    }
    #endregion

    /// <summary>Measuring a path to place shapes along it, and turning a shape to the direction of the path.</summary>
    #region Imaging_DrawRoute
    public static void DrawRoute(string outputPath)
    {
        using var image = new Image<Rgba32>(200, 120, Colors.White);
        var route = new VectorPathBuilder().MoveTo(20, 100).CubicBezierTo(60, 10, 120, 110, 176, 34).Build();

        // An arrow head that points to the right, with its tip at the origin
        var arrowHead = new VectorPathBuilder().AddPolygon([new PointD(0, 0), new PointD(-14, -6), new PointD(-14, 6)]).Build();

        // The end of the route and its direction there. The transformation turns the x axis to that direction, then
        // moves the origin to the end of the route.
        var length = route.GetLength();
        _ = route.TryGetPointAtDistance(length, out var tip, out var direction);
        var head = arrowHead.Transform(new AffineTransform(direction.X, direction.Y, -direction.Y, direction.X, tip.X, tip.Y));

        var line = new Pen(Colors.SteelBlue, 3) { DashPattern = [8, 5], LineCap = LineCap.Round };
        image.Draw(canvas =>
        {
            canvas.Stroke(line, route);
            canvas.Fill(Colors.SteelBlue, head);

            // A milestone at each quarter of the way
            for (var i = 1; i <= 3; i++)
            {
                _ = route.TryGetPointAtDistance(length * i / 4, out var milestone, out _);
                canvas.FillCircle(Colors.Crimson, milestone, 4);
            }
        });

        image.Save(outputPath);
    }
    #endregion

    /// <summary>Colors of sRGB drawn on an image of another color space.</summary>
    #region Imaging_DrawColorManaged
    public static void DrawBrandColors(string inputPath, string outputPath)
    {
        // A photograph whose ICC profile is not the one of sRGB (Display P3, Adobe RGB): its samples are not sRGB colors
        using var image = Image.Load(inputPath);
        var brand = Rgba32.ParseHex("#E63946");
        var label = new RectangleD(8, 8, 96, 24);
        var outline = new Pen(Colors.White, 2);

        image.Draw(canvas =>
        {
            // The colors of the callback are colors of sRGB: they are converted to the color space of the image, so
            // that the banner has the color its hexadecimal value means, whatever the profile of the photograph
            canvas.PaintProfile(IccProfile.Srgb, managed =>
            {
                managed.FillRoundedRectangle(brand, label, 6);
                managed.StrokeRoundedRectangle(outline, label, 6);
            });
        });

        // The profile of the photograph is kept: the file still says what its samples are
        image.Save(outputPath);
    }
    #endregion

    /// <summary>A shape with round corners, and its outline moved outward and inward.</summary>
    #region Imaging_DrawSticker
    public static void DrawSticker(string outputPath)
    {
        // A star: a point above the center, at the radius of a tip or of a notch, turned by a tenth of a turn each time
        var center = new PointD(60, 62);
        var points = new PointD[10];
        for (var i = 0; i < points.Length; i++)
        {
            points[i] = AffineTransform.CreateRotation(i * 36, center).Transform(new PointD(center.X, center.Y - (i % 2 == 0 ? 44 : 20)));
        }

        // Its corners become arcs of a circle of radius 4
        var star = new VectorPathBuilder().AddPolygon(points).Build().RoundCorners(4);

        // The outline of the star 6 pixels further out, with round corners: the white border of a sticker
        var border = star.Offset(6, LineJoin.Round);

        // And 5 pixels further in: a line that follows the shape, inside it
        var seam = new Pen(new Rgba32(255, 255, 255, 170), 1.5) { DashPattern = [4, 3] };
        var inner = star.Offset(-5, LineJoin.Round);

        using var image = new Image<Rgba32>(120, 120);
        image.Draw(canvas =>
        {
            canvas.Shadow(new Rgba32(0, 0, 0, 90), new PointD(0, 2), 3, shadowed => shadowed.Fill(Colors.White, border));
            canvas.Fill(Colors.Goldenrod, star);
            canvas.Stroke(seam, inner);
        });

        image.Save(outputPath);
    }
    #endregion

    /// <summary>The path data of an SVG icon, scaled to the image, and a part of its outline.</summary>
    #region Imaging_DrawIcon
    public static string DrawIcon(string outputPath)
    {
        // The path data of an icon of 24 by 24 pixels, as the "d" attribute of an SVG file has it
        var heart = VectorPath.ParseSvgPathData("M12 21.35l-1.45-1.32C5.4 15.36 2 12.28 2 8.5 2 5.42 4.42 3 7.5 3c1.74 0 3.41.81 4.5 2.09C13.09 3.81 14.76 3 16.5 3 19.58 3 22 5.42 22 8.5c0 3.78-3.4 6.86-8.55 11.54L12 21.35z");

        // A path is scaled before it is drawn or cut: its curves stay curves
        var icon = heart.Transform(AffineTransform.CreateScale(4, 4));
        var outline = new Pen(Colors.Crimson, 3) { LineCap = LineCap.Round, LineJoin = LineJoin.Round };

        using var image = new Image<Rgba32>(96, 96, Colors.White);
        image.Draw(canvas =>
        {
            canvas.Fill(Colors.MistyRose, icon);

            // The first three quarters of the outline
            canvas.Stroke(outline, icon.Trim(0, icon.GetLength() * 0.75));
        });

        image.Save(outputPath);
        return icon.ToSvgPathData();
    }
    #endregion

    /// <summary>A shadow, a blend mode, a gradient mixed in Oklab, a rounded rectangle with a radius per corner and a layer.</summary>
    #region Imaging_DrawBadge
    public static void DrawBadge(string outputPath)
    {
        using var image = new Image<Rgba32>(220, 120, Colors.WhiteSmoke);
        var card = new RectangleD(30, 24, 160, 64);
        var face = new LinearGradientBrush(new PointD(0, 24), new PointD(0, 88), [new GradientStop(0, Rgba32.ParseHex("#4F8EF7")), new GradientStop(1, Rgba32.ParseHex("#2456C8"))]) { ColorSpace = GradientColorSpace.Oklab };
        var shine = new SolidBrush(new Rgba32(255, 255, 255, 90)) { BlendMode = BlendMode.SoftLight };
        var mark = new SolidBrush(Colors.White);

        image.Draw(canvas =>
        {
            // The card and its shadow: black at a third of its opacity, 6 pixels down, blurred
            canvas.Shadow(new Rgba32(0, 0, 0, 85), new PointD(0, 6), 5, shadowed =>
                shadowed.FillRoundedRectangle(face, card, new CornerRadii(18, 18, 4, 18)));

            // A light that lightens the top of the card without hiding it, only where the card is
            canvas.Clip(new VectorPathBuilder().AddRoundedRectangle(card, new CornerRadii(18, 18, 4, 18)).Build(), clipped =>
                clipped.FillEllipse(shine, new PointD(110, 20), 120, 34));

            // Two disks that overlap, faded together: they do not show through each other
            canvas.Layer(0.6, layer =>
            {
                layer.FillCircle(mark, new PointD(96, 56), 16);
                layer.FillCircle(mark, new PointD(118, 56), 16);
            });
        });

        image.Save(outputPath);
    }
    #endregion

    /// <summary>A curve through points with markers at its ends, axes that end with an arrow, an area hatched by a pattern, and an outline on the inside of its path.</summary>
    #region Imaging_DrawChart
    public static void DrawChart(string outputPath)
    {
        using var image = new Image<Rgba32>(240, 140, Colors.White);
        PointD[] values = [new(30, 100), new(70, 60), new(110, 80), new(150, 30), new(190, 50)];

        // Hatching: a tile of 6 by 6 with a stripe, drawn once, repeated, and turned by the transformation of the brush
        var hatch = new PatternBrush(new Rectangle(0, 0, 6, 6), tile => tile.FillRectangle(Colors.LightSteelBlue, new Rectangle(0, 0, 3, 6)))
        {
            Transform = AffineTransform.CreateRotation(45),
        };
        var axis = new Pen(Colors.DimGray, 2) { EndMarker = LineMarker.Arrow };
        var line = new Pen(Colors.Crimson, 3) { StartMarker = LineMarker.Circle, EndMarker = LineMarker.Circle };
        var border = new Pen(Colors.SteelBlue, 2) { Alignment = PenAlignment.Inside };

        // The area under the curve: the curve through the values, closed by the axis
        var area = new VectorPathBuilder().MoveTo(values[0]).CurveTo(values.AsSpan(1)).LineTo(190, 120).LineTo(30, 120).Close().Build();

        image.Draw(canvas =>
        {
            // The outline is inside the area: it does not go over the axis
            canvas.Fill(hatch, area);
            canvas.Stroke(border, area);

            // The head of each arrow has its tip at the end of the line
            canvas.StrokeLine(axis, new PointD(20, 120), new PointD(225, 120));
            canvas.StrokeLine(axis, new PointD(20, 120), new PointD(20, 12));

            // The curve goes smoothly through every value, with a disk at both ends
            canvas.StrokeCurve(line, values);
        });

        image.Save(outputPath);
    }
    #endregion

    /// <summary>A panel of frosted glass over a photo: the backdrop blurred inside a clip, a light out of focus, an inner shadow, and a mask by the luminance of a gradient.</summary>
    #region Imaging_DrawGlass
    public static void DrawGlassPanel(string photoPath, string outputPath)
    {
        using var image = Image.Load(photoPath);
        var bounds = new RectangleD(16, 16, image.Width - 32, 72);
        var panel = new VectorPathBuilder().AddRoundedRectangle(bounds, 14).Build();
        var track = new RectangleD(bounds.X + 16, bounds.Bottom - 26, bounds.Width - 32, 12);
        var fade = new LinearGradientBrush(new PointD(track.X, 0), new PointD(track.Right, 0), [new GradientStop(0, Colors.White), new GradientStop(1, Colors.Black)]);

        image.Draw(canvas =>
        {
            // Frosted glass: what is behind the panel is blurred, then lightened, inside the panel only
            canvas.Clip(panel, glass =>
            {
                glass.BlurBackdrop(6);
                glass.Fill(new Rgba32(255, 255, 255, 70));
            });

            // A light on the panel, out of focus
            canvas.Blur(5, glow => glow.FillCircle(Colors.Gold, new PointD(bounds.X + 30, bounds.Y + 24), 8));

            // The track of a gauge, pressed into the panel: the shadow of its edge falls inside it, from the top
            canvas.InnerShadow(new Rgba32(0, 0, 0, 150), new PointD(0, 2), 1.5, well =>
            {
                well.FillRoundedRectangle(Colors.WhiteSmoke, track, 6);

                // Its level fades out to the right: it is kept by the luminance of a gradient from white to black
                well.Mask(
                    mask => mask.Fill(fade),
                    level => level.FillRoundedRectangle(Colors.SeaGreen, track, 6),
                    MaskMode.Luminance);
            });
        });

        image.Save(outputPath);
    }
    #endregion

    /// <summary>The filters of CSS: frosted glass behind a card (backdrop-filter) and a drop shadow under a badge (filter).</summary>
    #region Imaging_DrawFilters
    public static void DrawFrostedCard(string photoPath, string outputPath)
    {
        using var image = Image.Load(photoPath);
        var card = new RectangleD(24, 24, image.Width - 48, 80);
        var shape = new VectorPathBuilder().AddRoundedRectangle(card, 16).Build();

        // backdrop-filter: blur(16px) saturate(1.4), and filter: drop-shadow(0 4px 12px rgb(0 0 0 / 47%))
        var glass = new FilterChain(new BlurFilter(8), new ColorMatrixFilter(ColorMatrix.CreateSaturation(1.4)));
        var lift = new DropShadowFilter(new Rgba32(0, 0, 0, 120), new PointD(0, 4), 6);

        image.Draw(canvas =>
        {
            // The card starts with what is behind it, blurred, inside its rounded corners, then is lightened
            canvas.Clip(shape, clipped => clipped.BackdropLayer(glass, card, 1, panel => panel.Fill(new Rgba32(255, 255, 255, 60))));

            // The badge and its shadow, as a whole
            canvas.Filter(lift, badge => badge.FillCircle(Colors.Gold, new PointD(card.Right - 30, card.Bottom - 30), 14));
        });

        image.Save(outputPath);
    }
    #endregion

    /// <summary>An SVG filter: a ring that glows, from a blurred and colored copy of its own shape under it.</summary>
    #region Imaging_DrawFilterGraph
    public static void DrawGlowingRing(string outputPath)
    {
        using var image = new Image<Rgba32>(160, 160, new Rgba32(16, 20, 40));

        // <feMorphology in="SourceAlpha" operator="dilate" radius="3"/> <feGaussianBlur stdDeviation="6"/>
        // <feFlood flood-color="#33ccff"/> <feComposite operator="in" in2="(the blur)"/>
        // <feMerge> <feMergeNode in="(the glow)"/> <feMergeNode in="SourceGraphic"/> </feMerge>
        var glow = new FilterGraph(
            new MorphologyPrimitive(FilterMorphologyOperator.Dilate, 3, 3) { Input = FilterInput.SourceAlpha },
            new GaussianBlurPrimitive(6),
            new FloodPrimitive(new Rgba32(51, 204, 255)),
            new CompositePrimitive(FilterCompositeOperator.In, FilterInput.FromResult(1)),
            new MergePrimitive(FilterInput.FromResult(3), FilterInput.SourceGraphic));

        image.Draw(canvas => canvas.Filter(glow, ring => ring.StrokeCircle(new Pen(new Rgba32(255, 255, 255), 6), new PointD(80, 80), 40)));
        image.Save(outputPath);
    }
    #endregion

    /// <summary>An image drawn on a quadrilateral, as a plane seen in perspective: a screenshot on the screen of a photo.</summary>
    #region Imaging_DrawPerspective
    public static void DrawOnScreen(string photoPath, string screenshotPath, string outputPath)
    {
        using var photo = Image.Load(photoPath);
        using var screenshot = Image.Load(screenshotPath);

        // Where the four corners of the screenshot go on the photo, from its top-left corner, clockwise. The
        // screenshot is larger than the screen: smooth sampling reads every pixel of it that a pixel of the photo covers.
        photo.Draw(canvas => canvas.DrawImage(
            screenshot.Frames[0],
            new PointD(40, 30),
            new PointD(180, 44),
            new PointD(176, 132),
            new PointD(36, 150),
            ImageSampling.Smooth));

        photo.Save(outputPath);
    }
    #endregion

    /// <summary>A card turned in three dimensions and seen in perspective, as the 3D transforms of CSS draw it.</summary>
    #region Imaging_Draw3d
    public static void DrawTiltedCard(string outputPath)
    {
        using var image = new Image<Rgba32>(240, 160, Colors.WhiteSmoke);
        var card = new RectangleD(60, 30, 120, 100);
        var face = new LinearGradientBrush(new PointD(60, 0), new PointD(180, 0), [new GradientStop(0, Colors.SteelBlue), new GradientStop(1, Colors.MidnightBlue)]);

        // "perspective: 300px" and "transform: rotateY(40deg)", both around the center of the image: the turn of the
        // card applies first, then the perspective, combined in three dimensions
        var toCenter = Transform3D.CreateTranslation(-120, -80, 0);
        var fromCenter = Transform3D.CreateTranslation(120, 80, 0);
        var perspective = toCenter.Then(Transform3D.CreatePerspective(300)).Then(fromCenter);
        var turn = toCenter.Then(Transform3D.CreateRotationY(40)).Then(fromCenter);

        image.Draw(canvas => canvas.Transform(perspective, viewer => viewer.Transform(turn, plane =>
        {
            // Everything is drawn in the plane of the card: its shadow, its gradient, its border
            plane.Shadow(new Rgba32(0, 0, 0, 90), new PointD(0, 6), 6, shadowed => shadowed.FillRoundedRectangle(face, card, 12));
            plane.StrokeRoundedRectangle(new Pen(Colors.White, 3), card.Inflate(-10, -10), 6);
        })));

        image.Save(outputPath);
    }
    #endregion

    /// <summary>Hard edges for pixel art, and paints mixed in linear light.</summary>
    #region Imaging_DrawRendering
    public static void DrawSprite(string outputPath)
    {
        using var image = new Image<Rgba32>(32, 32, Colors.Black);
        var thin = new Pen(Colors.White, 1);

        image.Draw(canvas =>
        {
            // Without antialiasing, every pixel is the paint or what was there: the colors stay those of the palette
            canvas.Antialiasing(enabled: false, sprite =>
            {
                sprite.FillCircle(Colors.Gold, new PointD(16, 18), 9);
                sprite.FillRectangle(Colors.Crimson, new RectangleD(11.5, 14.5, 9, 3));
            });

            // In linear light, the edge of a thin light line on a dark background is not darker than the line
            canvas.Compositing(CompositingSpace.LinearSrgb, light => light.StrokeLine(thin, new PointD(2, 2.3), new PointD(30, 5.7)));
        });

        image.Save(outputPath);
    }
    #endregion

    /// <summary>Typed row access with a static callback (no closure allocation).</summary>
    #region Imaging_TypedRows
    public static void InvertColors(string path)
    {
        using var image = Image.Load<Rgba32>(path);
        foreach (var frame in image.Frames)
        {
            frame.ProcessPixelRows(static pixels =>
            {
                for (var y = 0; y < pixels.Height; y++)
                {
                    foreach (ref var pixel in pixels.GetRowSpan(y))
                    {
                        pixel = new Rgba32((byte)(255 - pixel.R), (byte)(255 - pixel.G), (byte)(255 - pixel.B), pixel.A);
                    }
                }
            });
        }

        image.Save(path);
    }
    #endregion

    /// <summary>Typed row access passing state explicitly.</summary>
    #region Imaging_TypedRowsState
    public static long ComputeLuminanceSum(Image<Gray16> image)
    {
        var total = new StrongBox<long>();
        image.Frames[0].ProcessPixelRows(total, static (pixels, state) =>
        {
            for (var y = 0; y < pixels.Height; y++)
            {
                foreach (var pixel in pixels.GetRowSpan(y))
                {
                    state.Value += pixel.Value;
                }
            }
        });

        return total.Value;
    }
    #endregion

    /// <summary>Builds an animation from scratch with exact rational timing and saves it as APNG and GIF.</summary>
    #region Imaging_BuildAnimation
    public static void BuildAnimation(string apngPath, string gifPath)
    {
        using var animation = new Image<Rgba32>(64, 64, new Rgba32(255, 0, 0));
        animation.Frames[0].Metadata.Duration = FrameDuration.FromMilliseconds(100);

        var second = animation.AppendFrame(); // creates default animation settings (infinite loop)
        second.Metadata.Duration = new FrameDuration(1, 30); // exactly 1/30 s
        second.ProcessPixelRows(static pixels =>
        {
            for (var y = 0; y < pixels.Height; y++)
            {
                pixels.GetRowSpan(y).Fill(new Rgba32(0, 0, 255));
            }
        });

        animation.Animation!.TotalPlays = 3; // played three times in total

        animation.Save(apngPath, new PngEncoder { AnimationMode = PngAnimationMode.Animated });
        animation.Save(gifPath, new GifEncoder { Quantization = new QuantizeOptions { Dithering = DitheringMode.FloydSteinberg } });
    }
    #endregion

    /// <summary>An animated cursor: an animation whose frames are cursor images with a hotspot, played forever.</summary>
    #region Imaging_AnimatedCursor
    public static Point WriteAndReadAnimatedCursor(string animationPath, string cursorPath)
    {
        using var animation = Image.Load(animationPath);
        animation.Resize(new ResizeOptions(32, 32) { Mode = ResizeMode.Stretch });
        foreach (var frame in animation.Frames)
        {
            frame.Metadata.Hotspot = new Point(4, 2); // the pixel that designates the pointer position
        }

        // An animated cursor always loops: a finite play count is rejected instead of being dropped
        animation.Animation = new AnimationMetadata();
        animation.Save(cursorPath); // ".ani" selects AniEncoder; durations are rounded to sixtieths of a second

        // A hotspot follows its pixel through geometry operations: (4, 2) at 32x32 is (8, 4) at 64x64
        using var cursor = Image.Load(cursorPath);
        cursor.Resize(new ResizeOptions(64, 64) { Mode = ResizeMode.Stretch, AllowUpscaling = true });
        return cursor.Frames[0].Metadata.Hotspot!.Value;
    }
    #endregion

    /// <summary>JPEG stores a single frame: export one frame explicitly and choose how to remove alpha.</summary>
    #region Imaging_ExportJpegFrame
    public static void ExportFrameAsJpeg(string animatedPath, int frameIndex, string jpegPath)
    {
        using var animation = Image.Load(animatedPath);
        using var frame = animation.CloneFrame(frameIndex);
        frame.Save(jpegPath, new JpegEncoder
        {
            Quality = 85,
            BackgroundColor = new Rgba32(255, 255, 255),
            AllowBitDepthReduction = true,
            MetadataHandling = MetadataHandling.DiscardUnsupported,
        });
    }
    #endregion

    /// <summary>A progressive JPEG shows the whole picture early when it is downloaded, and is usually smaller.</summary>
    #region Imaging_SaveProgressiveJpeg
    public static void SaveProgressiveJpeg(string sourcePath, string jpegPath)
    {
        using var image = Image.Load<Rgb24>(sourcePath);
        image.Save(jpegPath, new JpegEncoder
        {
            Quality = 85,
            Process = JpegEncodingProcess.Progressive,
            MetadataHandling = MetadataHandling.DiscardUnsupported,
        });
    }
    #endregion

    /// <summary>WebP output: a lossy photo with its alpha kept losslessly, and a lossless animation published atomically.</summary>
    #region Imaging_SaveWebp
    public static void SaveWebP(string photoPath, string webpPath, string animatedPath, string animatedWebPPath)
    {
        using (var photo = Image.Load(photoPath))
        {
            photo.Save(webpPath, new WebPEncoder
            {
                Compression = WebPCompression.Lossy,
                Quality = 80, // 0-100: size versus fidelity
                Effort = 6,   // 0-9: encoding time versus size
                AllowBitDepthReduction = true, // 16-bit sources are written with 8 bits
            });
        }

        // Every sample is kept, including the colors of fully transparent pixels. An animation needs a seekable
        // destination (the file size is patched at the end): a path, a FileStream or a MemoryStream.
        using var animation = Image.Load(animatedPath);
        animation.Save(animatedWebPPath, new WebPEncoder { Compression = WebPCompression.Lossless });
    }
    #endregion

    /// <summary>QOI output: a lossless still image, and linear-light samples whose label must survive the conversion.</summary>
    #region Imaging_SaveQoi
    public static void SaveQoi(string inputPath, string qoiPath, string linearQoiPath, string pngPath)
    {
        using var image = Image.Load(inputPath);
        using var frame = image.CloneFrame(0); // QOI stores one still image
        frame.Save(qoiPath, new QoiEncoder { AllowBitDepthReduction = true });

        // A QOI colorspace of 1 (linear samples) is reported as a label, never applied: other formats cannot store it, so
        // saving the pixels elsewhere requires an explicit decision instead of silently relabeling them as sRGB
        using var linear = Image.Load(linearQoiPath);
        if (linear.Metadata.TransferFunction == ColorTransferFunction.Linear)
        {
            linear.Save(pngPath, new PngEncoder { MetadataHandling = MetadataHandling.DiscardUnsupported });
        }
    }
    #endregion

    /// <summary>Asset-pipeline output: the BMP, TGA and Netpbm formats, with their explicit choices.</summary>
    #region Imaging_SaveAssetFormats
    public static void SaveAssetFormats(string inputPath, string bmpPath, string tgaPath, string pnmPath)
    {
        using var image = Image.Load(inputPath);
        using var frame = image.CloneFrame(0); // BMP, TGA and Netpbm store one still image

        // BMP: 32-bit with an explicit alpha mask for pixel formats with alpha, 24-bit otherwise. Forcing 24-bit discards
        // alpha, so it needs a background color.
        frame.Save(bmpPath, new BmpEncoder { PixelLayout = BmpPixelLayout.Bgr24, BackgroundColor = new Rgba64(65535, 65535, 65535), AllowBitDepthReduction = true });

        // TGA: run-length packets are lossless and usually smaller on graphic content
        frame.Save(tgaPath, new TgaEncoder { Compression = TgaCompression.RunLength, AllowBitDepthReduction = true });

        // Netpbm: the variant follows the pixel format (PGM, PPM, or PAM when the pixels have alpha), and MAXVAL follows
        // the precision, so 16-bit images keep every bit. A portable bitmap (PBM) is only written on request, with
        // PnmPixelLayout.Bitmap, and only from black and white pixels.
        frame.Save(pnmPath, new PnmEncoder());
    }
    #endregion

    /// <summary>Fewer colors, in place: at most 64 colors per frame, dithered to hide the bands.</summary>
    #region Imaging_Quantize
    public static void QuantizePhoto(string inputPath, string outputPath)
    {
        using var image = Image.Load(inputPath);

        // Each frame gets its own palette of at most 64 colors (its exact colors when they fit), and Atkinson error
        // diffusion hides the bands of smooth gradients. The pixel format is kept: the image can be saved in any format.
        image.Quantize(new QuantizeOptions { MaxColors = 64, Dithering = DitheringMode.Atkinson });
        image.Save(outputPath);
    }
    #endregion

    /// <summary>One palette for every frame of an animation, computed from all of them.</summary>
    #region Imaging_SharedPalette
    public static void SaveGifWithSharedPalette(string inputPath, string outputPath)
    {
        using var animation = Image.Load(inputPath);

        // The palette of the pixels of every frame and of the poster: 255 colors leave room for the transparent entry
        ImagePalette palette;
        using (var builder = new PaletteBuilder())
        {
            builder.Add(animation);
            palette = builder.Build(maxColors: 255);
        }

        // Every frame maps to the same colors, so they do not flicker from one frame to the next
        animation.Save(outputPath, new GifEncoder
        {
            Quantization = new QuantizeOptions { Palette = palette, Dithering = DitheringMode.FloydSteinberg },
        });
    }
    #endregion

    /// <summary>An 8-bit palette PNG from any image, colors reduced when they do not fit.</summary>
    #region Imaging_SavePngColorReduction
    public static void SavePalettePng(string inputPath, string outputPath)
    {
        using var image = Image.Load(inputPath);

        // At most 256 colors, each with its own alpha (tRNS), in one palette for every frame. An image whose colors fit
        // is stored exactly; the others are reduced and dithered.
        image.Save(outputPath, new PngEncoder
        {
            PixelLayout = PngPixelLayout.Indexed,
            AllowColorReduction = true,
            Quantization = new QuantizeOptions { Dithering = DitheringMode.Atkinson },
        });
    }
    #endregion

    /// <summary>A compact BMP for graphics: 8-bit palette indexes, run-length encoded, with an explicit color-reduction policy.</summary>
    #region Imaging_SaveBmpIndexed
    public static void SaveIndexedBmp(string inputPath, string outputPath)
    {
        using var image = Image.Load(inputPath);
        using var frame = image.CloneFrame(0);

        // 8-bit indexes, run-length encoded (BI_RLE8). The palette holds the colors of the frame exactly when there are at
        // most 256 of them; AllowColorReduction accepts a deterministic palette for the other frames, here with dithering.
        // A BMP palette has no alpha, so non-opaque pixels need a background color.
        frame.Save(outputPath, new BmpEncoder
        {
            PixelLayout = BmpPixelLayout.Indexed8,
            Compression = BmpCompression.RunLength,
            AllowColorReduction = true,
            Quantization = new QuantizeOptions { Dithering = DitheringMode.FloydSteinberg },
            BackgroundColor = new Rgba64(65535, 65535, 65535),
            AllowBitDepthReduction = true,
        });
    }
    #endregion

    /// <summary>A compact PNG for graphics with few colors: palette indexes instead of color samples, stored exactly.</summary>
    #region Imaging_SavePngIndexed
    public static bool SaveIndexedPng(string inputPath, string outputPath)
    {
        using var image = Image.Load(inputPath);
        try
        {
            // One palette (PLTE, plus tRNS when a color is not opaque) for the colors of every frame, and 1-, 2-, 4- or
            // 8-bit indexes. The pixels are stored exactly, alpha included: nothing is ever reduced.
            image.Save(outputPath, new PngEncoder { PixelLayout = PngPixelLayout.Indexed });
            return true;
        }
        catch (UnsupportedImageFeatureException exception) when (exception.Feature is "Color reduction" or "Bit depth reduction")
        {
            // More than 256 colors, or 16-bit samples that 8 bits cannot represent: the save failed before it wrote
            // anything. The default layout follows the pixel format and stores every image.
            image.Save(outputPath, new PngEncoder());
            return false;
        }
    }
    #endregion

    /// <summary>The smallest lossless PNG or APNG: the encoder compresses the candidate encodings and keeps the smallest.</summary>
    #region Imaging_SavePngMinimumSize
    public static void SaveSmallestPng(string inputPath, string outputPath)
    {
        using var image = Image.Load(inputPath);

        // The layout the pixels need (grayscale, a color key, a palette, 8 instead of 16 bits...), the best filters and
        // zlib level, and for an animation the frame regions that changed. The pixels, alpha values, frames, durations and
        // metadata are stored exactly; only the encoding changes, and it takes several times longer.
        image.Save(outputPath, new PngEncoder { Optimization = PngOptimization.MinimumSize });
    }
    #endregion

    /// <summary>A multi-page TIFF document: pages may differ in size and pixel format, and are read one at a time.</summary>
    #region Imaging_TiffPages
    public static Size WriteAndReadTiffDocument(string inputPath, string documentPath)
    {
        using (var pages = ImageCollection.Create(ImageCollectionKind.Pages))
        {
            using var source = Image.Load(inputPath);
            using var first = source.CloneFrame(0); // a page is a still image, never an animation frame
            using var second = first.Clone();
            second.Resize(new ResizeOptions(first.Width / 2, first.Height / 2));

            // Add copies the image: the caller keeps and disposes its own
            pages.Add(first);
            pages.Add(second);
            pages.Save(documentPath, new TiffEncoder { Compression = TiffCompression.Deflate });
        }

        // Loading reads the directory structure only: the pixels of a page are read when that page is decoded
        using var document = ImageCollection.Load(documentPath);
        using var page = document[1].Decode();
        return page.Size; // the second page keeps its own size, never the first one's
    }
    #endregion

    /// <summary>A multi-size icon and a cursor: representations are alternatives, never animation frames.</summary>
    #region Imaging_IcoRepresentations
    public static Point WriteAndReadIcon(string inputPath, string iconPath, string cursorPath)
    {
        using var loaded = Image.Load(inputPath);
        using var source = loaded.CloneFrame(0); // a representation is a still image, never an animation frame
        using var representations = ImageCollection.Create(ImageCollectionKind.Representations);
        foreach (var side in (int[])[16, 32, 256])
        {
            using var sized = source.Clone();
            sized.Resize(new ResizeOptions(side, side) { Mode = ResizeMode.Stretch, AllowUpscaling = true });
            representations.Add(sized, hotspot: new Point(side / 2, side / 2));
        }

        // An icon cannot store a hotspot, so the entry hotspots are unsupported metadata there
        representations.Save(iconPath, new IcoEncoder { MetadataHandling = MetadataHandling.DiscardUnsupported });
        representations.Save(cursorPath, new IcoEncoder { Kind = IconKind.Cursor });

        using var icon = ImageCollection.Load(iconPath);
        using var best = icon.SelectBySize(new Size(24, 24)).Decode(); // the 32x32 representation: nothing is resized
        using var cursor = ImageCollection.Load(cursorPath);
        return cursor.SelectBySize(new Size(16, 16)).Hotspot!.Value;
    }
    #endregion

    /// <summary>Bounded-memory streaming transform: decode, resize and re-encode one frame at a time.</summary>
    #region Imaging_StreamingTransform
    public static async Task StreamingResizeAsync(Stream input, Stream output, CancellationToken cancellationToken)
    {
        await using var reader = await Image.OpenReaderAsync<Rgba32>(input, cancellationToken: cancellationToken);
        var info = reader.Info;
        var size = new Size(info.Width / 2, info.Height / 2);

        await using var writer = Image.CreateWriter<Rgba32>(output, new ImageWriterOptions(size)
        {
            Encoder = new GifEncoder(),
            Animation = info.Animation, // e.g. preserve the number of plays
            Metadata = info.Metadata,
        });

        while (await reader.ReadFrameAsync(cancellationToken) is { } frame)
        {
            using (frame)
            {
                frame.Resize(new ResizeOptions(size) { Mode = ResizeMode.Stretch }, cancellationToken);
                await writer.WriteFrameAsync(frame.Frames[0], cancellationToken);
            }
        }

        await writer.CompleteAsync(cancellationToken); // mandatory: disposing without Complete aborts the output
    }
    #endregion

    /// <summary>Streaming with a reused destination: ReadFrameInto overwrites the pixels of one caller-owned image.</summary>
    #region Imaging_ReadFrameInto
    public static void StreamingWithReusedBuffer(string inputPath, string outputPath)
    {
        using var reader = Image.OpenReader<Rgba32>(inputPath);
        var info = reader.Info;
        var frameCount = info.FrameCount ?? throw new InvalidOperationException("PNG output requires a known frame count.");

        using var writer = Image.CreateWriter<Rgba32>(outputPath, new ImageWriterOptions(info.Size)
        {
            Encoder = new PngEncoder { AnimationMode = PngAnimationMode.Animated },
            ExpectedFrameCount = frameCount,
            Animation = info.Animation,
        });

        if (reader.ReadPosterFrame() is { } poster)
        {
            using (poster)
            {
                writer.WritePosterFrame(poster.Frames[0]);
            }
        }

        using var buffer = new Image<Rgba32>(info.Width, info.Height);
        while (reader.ReadFrameInto(buffer))
        {
            buffer.Grayscale();
            writer.WriteFrame(buffer.Frames[0]);
        }

        writer.Complete(); // publishes outputPath atomically
    }
    #endregion

    /// <summary>AVIF output: a lossy photo, a lossless image and an animation written as an image sequence.</summary>
    #region Imaging_SaveAvif
    public static void SaveAvif(string photoPath, string avifPath, string losslessPath, string animatedPath, string animatedAvifPath)
    {
        using (var photo = Image.Load(photoPath))
        {
            // Lossy: Y'CbCr 4:2:0 by default. The alpha plane, when the image has one, is always stored losslessly.
            photo.Save(avifPath, new AvifEncoder
            {
                Compression = AvifCompression.Lossy,
                Quality = 80, // 0-100: size versus fidelity
                Effort = 6,   // 0-9: encoding time versus size
                AllowBitDepthReduction = true, // 16-bit sources are written with 12 bits
            });

            // The default is lossless: every sample of an 8-bit image is decoded back exactly. AVIF stores at most 12 bits:
            // a 16-bit image needs a bit depth, which is the consent to round its samples.
            photo.Save(losslessPath, new AvifEncoder { BitDepth = 12 });
        }

        // An animation is written as an image sequence, with its exact durations and its play count. It needs a seekable
        // destination (the index of the images is written last): a path, a FileStream or a MemoryStream.
        using var animation = Image.Load(animatedPath);
        animation.Save(animatedAvifPath, new AvifEncoder());
    }
    #endregion

    /// <summary>AVIF input: a still image or an animation is decoded, then saved in another format.</summary>
    #region Imaging_LoadAvif
    public static string ConvertAvif(string avifPath, string pngPath)
    {
        using var image = Image.Load(avifPath);

        // The color description of the file (primaries, transfer characteristics, matrix and range code points) is a
        // label, as an ICC profile is: the matrix and the range were applied to produce RGB, nothing else was converted.
        var description = $"{image.Width}x{image.Height} {image.PixelFormat}, frames: {image.Frames.Count}, color: {image.Metadata.Cicp}";

        // PNG keeps 16-bit samples, alpha and animations, without loss, and the primaries and the transfer
        // characteristics of the description (cICP chunk), so the samples of an HDR image keep their meaning.
        image.Save(pngPath, new PngEncoder { MetadataHandling = MetadataHandling.Strict });
        return description;
    }
    #endregion

    /// <summary>HEIC output: a photo that every HEIC reader opens, a lossless image and an animation written as an image sequence.</summary>
    #region Imaging_SaveHeic
    public static void SaveHeic(string photoPath, string heicPath, string losslessPath, string animatedPath, string animatedHeicPath)
    {
        using (var photo = Image.Load(photoPath))
        {
            // Lossy 4:2:0 with 8 bits is the Main Still Picture profile of HEVC, which every HEIC reader decodes.
            // The alpha plane, when the image has one, is always stored losslessly.
            photo.Save(heicPath, new HeicEncoder
            {
                Compression = HeicCompression.Lossy,
                Quality = 80, // 0-100: size versus fidelity
                Effort = 4,   // 0-9: encoding time versus size
                AllowBitDepthReduction = true, // 16-bit sources are written with 12 bits
            });

            // The default is lossless: every sample of an 8-bit image is decoded back exactly. The R'G'B' samples are
            // coded as they are (4:4:4), which is a profile of the range extensions of HEVC: fewer readers decode it.
            photo.Save(losslessPath, new HeicEncoder { AllowBitDepthReduction = true });
        }

        // An animation is written as an image sequence, with its exact durations and its play count. It needs a seekable
        // destination (the index of the images is written last): a path, a FileStream or a MemoryStream.
        using var animation = Image.Load(animatedPath);
        animation.Save(animatedHeicPath, new HeicEncoder { Compression = HeicCompression.Lossy });
    }
    #endregion

    /// <summary>HEIC input: the photo of a phone is decoded, turned upright and saved as a JPEG file.</summary>
    #region Imaging_LoadHeic
    public static string ConvertHeic(string heicPath, string jpegPath)
    {
        using var image = Image.Load(heicPath);
        var description = $"{image.Width}x{image.Height} {image.PixelFormat}, orientation: {image.Metadata.Orientation}";

        // A camera stores the image as its sensor reads it, and the rotation to apply as a property of the file: the
        // decoder reports it as the orientation of the image and moves no pixel. AutoOrient applies it, so that the
        // JPEG file needs no orientation.
        image.AutoOrient();

        // The ICC profile of the photo (Display P3 for a recent phone) and its EXIF block are stored in the JPEG file
        image.Save(jpegPath, new JpegEncoder { Quality = 90 });
        return description;
    }
    #endregion

    /// <summary>JPEG XL output: lossless files, 16-bit samples and animations included, lossy and progressive files, and what a JPEG XL file declares.</summary>
    #region Imaging_SaveJpegxl
    public static string SaveJpegXl(string inputPath, string jxlPath, string lossyPath, string animatedPath, string animatedJxlPath)
    {
        using (var image = Image.Load(inputPath))
        {
            // Lossless by default: every pixel format is stored as it is (no bit depth reduction, no flattening, and the
            // colors of fully transparent pixels are kept). The effort changes the size and the encoding time, never the pixels.
            image.Save(jxlPath, new JpegXlEncoder { Effort = 7 });

            // Lossy on request: the quality sets the quantization, and the alpha channel stays exact. A progressive file
            // can be shown before it is complete.
            image.Save(lossyPath, new JpegXlEncoder { Compression = JpegXlCompression.Lossy, Quality = 90, Progressive = true });
        }

        // Frames are written as they come: the destination does not need to be seekable, whatever the frame count.
        using (var animation = Image.Load(animatedPath))
        {
            animation.Save(animatedJxlPath);
        }

        // Lossy (VarDCT) and lossless (Modular) files are decoded, whatever wrote them.
        var info = Image.Identify(jxlPath);
        return $"{info.Format} {info.Size.Width}x{info.Size.Height} {info.PixelFormat}, {info.BitsPerComponent} bits";
    }
    #endregion

    /// <summary>JPEG 2000 output: a lossless JP2 file, a lossy codestream, and what a JPEG 2000 file declares.</summary>
    #region Imaging_SaveJpeg2000
    public static string SaveJpeg2000(string inputPath, string jp2Path, string codestreamPath)
    {
        using var image = Image.Load(inputPath);

        // Lossless by default: every pixel format is stored as it is, 16-bit samples and the colors of fully
        // transparent pixels included. A JP2 file stores the ICC profile, the resolution, EXIF and XMP.
        image.Save(jp2Path);

        // Lossy: the quality sets the quantization, on the scale of the JPEG encoder; the opacity stays exact.
        // A codestream alone (.j2k, .j2c, .jpc) stores no metadata: a strict save rejects an image that has some.
        image.Save(codestreamPath, new Jpeg2000Encoder
        {
            Compression = Jpeg2000Compression.Lossy,
            Quality = 80,
            Container = Jpeg2000Container.Codestream,
            MetadataHandling = MetadataHandling.DiscardUnsupported,
        });

        // Files, codestreams and the high-throughput block coder (HTJ2K) are decoded, whatever wrote them.
        var info = Image.Identify(jp2Path);
        return $"{info.Format} {info.Size.Width}x{info.Size.Height} {info.PixelFormat}, {info.BitsPerComponent} bits";
    }
    #endregion

    /// <summary>OpenEXR: floating-point pixels in and out, and the integer version of a high dynamic range image.</summary>
    #region Imaging_OpenExr
    public static Image<Rgba32> ConvertOpenExr(string exrPath, string pizPath)
    {
        // An OpenEXR file loads as RgbaHalf (half samples) or RgbaFloat (float or 32-bit integer samples), in linear light
        using var hdr = Image.Load(exrPath);

        // Lossless: the samples are written as they are, values above 1 included. PIZ is often the smallest
        hdr.Save(pizPath, new OpenExrEncoder { Compression = OpenExrCompression.Piz });

        // Integer pixels cannot hold the highlights: a typed load compresses them with a tone mapping
        return Image.Load<Rgba32>(exrPath, new ImageDecodeOptions
        {
            Conversion = new PixelConversionOptions { ToneMapping = new HdrToneMappingOptions() },
        });
    }
    #endregion

    /// <summary>Progressive reading: the renderings of a frame before it is complete.</summary>
    #region Imaging_ProgressiveRead
    public static int ShowWhileReading(Stream input, Action<Image<Rgba32>, FrameRefinement> show)
    {
        using var reader = Image.OpenReader<Rgba32>(input);
        using var image = new Image<Rgba32>(reader.Info.Width, reader.Info.Height);
        var renderings = 0;

        // Each call writes a full image: coarse at first (Downsampling 8, then 4, 2), then the frame itself (IsFinal).
        // A file that is not progressive, or another format, gives its frame at once. An input that ends early gives the
        // renderings it has, then throws: truncation is never a clean end.
        while (reader.ReadFrameRefinementInto(image) is { } refinement)
        {
            show(image, refinement);
            renderings++;
            if (refinement.IsFinal)
                break;
        }

        return renderings;
    }
    #endregion

    /// <summary>A JPEG file stored in a JPEG XL file and rebuilt, without decoding its pixels.</summary>
    #region Imaging_TranscodeJpeg
    public static bool RecompressJpeg(string jpegPath, string jxlPath, string rebuiltPath)
    {
        // No pixel is decoded: the JPEG XL file holds the coefficients of the JPEG file. A file that cannot be stored
        // exactly (four components, a sampling factor of 4, arithmetic coding) throws; nothing is approximated.
        JpegXlTranscoder.RecompressJpeg(jpegPath, jxlPath);

        // The file is an image like any other...
        using (var image = Image.Load(jxlPath))
        {
            _ = image.PixelFormat;
        }

        // ...and gives the JPEG file back, byte for byte. A JPEG XL file made from pixels returns false.
        return JpegXlTranscoder.TryReconstructJpeg(jxlPath, rebuiltPath)
            && File.ReadAllBytes(jpegPath).AsSpan().SequenceEqual(File.ReadAllBytes(rebuiltPath));
    }
    #endregion

    /// <summary>Identify without decoding pixels; unknown values stay null.</summary>
    #region Imaging_Identify
    public static string Describe(Stream stream)
    {
        var info = Image.Identify(stream, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan });
        var frames = info.FrameCount?.ToString(CultureInfo.InvariantCulture) ?? "unknown";
        return $"{info.Format} {info.Size} {info.PixelFormat}, frames: {frames}, orientation: {info.Metadata.Orientation}";
    }
    #endregion

    /// <summary>Precision-preserving conversion with an explicit alpha policy.</summary>
    #region Imaging_ConvertPrecision
    public static void ConvertToGray16(string inputPath, string outputPath)
    {
        using var image = Image.Load<Rgba64>(inputPath);
        image.AutoOrient();
        using var gray = image.CloneAs<Gray16>(new PixelConversionOptions
        {
            BackgroundColor = new Rgba64(65535, 65535, 65535),
            DiscardIncompatibleColorProfile = true,
        });
        gray.Save(outputPath, new PngEncoder());
    }
    #endregion

    /// <summary>Floating-point pixels: highlights above the white are compressed or clipped before integers store them.</summary>
    #region Imaging_FloatingPoint
    public static Image<Rgba64> ToneMapHighlights(Image<RgbaFloat> hdr, string pngPath)
    {
        // Integer pixels cannot store the highlights: compress them with a tone mapping, or clip them
        var toneMapped = hdr.CloneAs<Rgba64>(new PixelConversionOptions
        {
            ToneMapping = new HdrToneMappingOptions { Operator = HdrToneMappingOperator.Reinhard },
        });

        // Encoders store integers too: the encoder says how to reduce the samples
        hdr.Save(pngPath, new PngEncoder
        {
            FloatingPointConversion = new PixelConversionOptions { OutOfRangeSamples = OutOfRangeSampleHandling.Clip },
            MetadataHandling = MetadataHandling.DiscardUnsupported,
        });
        return toneMapped;
    }
    #endregion

    /// <summary>Copies raw pixels in and out; the library never wraps caller memory.</summary>
    #region Imaging_RawPixels
    public static byte[] RoundTripRawPixels(ReadOnlySpan<byte> rgba, int width, int height)
    {
        using var image = Image.ImportPixelBytes<Rgba32>(rgba, width, height);
        image.Crop(new Rectangle(0, 0, width / 2, height));
        var result = new byte[image.Width * image.Height * 4];
        image.Frames[0].CopyPixelBytesTo(result);
        return result;
    }
    #endregion

    /// <summary>Handling the error taxonomy.</summary>
    #region Imaging_ErrorHandling
    public static Image? TryLoad(string path, ImageConfiguration configuration)
    {
        try
        {
            return Image.Load(path, new ImageDecodeOptions { Configuration = configuration });
        }
        catch (UnknownImageFormatException)
        {
            return null; // not an image
        }
        catch (UnsupportedImageFeatureException ex)
        {
            Console.Error.WriteLine($"Unsupported {ex.Format} feature: {ex.Feature}");
            return null;
        }
        catch (ImageResourceLimitException ex)
        {
            Console.Error.WriteLine($"Limit {ex.Kind} exceeded ({ex.Requested} > {ex.Limit})");
            return null;
        }
        catch (InvalidImageContentException)
        {
            return null; // corrupted or truncated file
        }
    }
    #endregion

    /// <summary>Metadata editing with explicit save policies.</summary>
    #region Imaging_EditMetadata
    public static void EditMetadata(string path)
    {
        using var image = Image.Load(path);
        image.Metadata.TextEntries.Add(new ImageTextEntry("Title", "Sunset"));
        image.Metadata.Resolution = new ImageResolution(300, 300);
        image.Save(path, new PngEncoder { MetadataHandling = MetadataHandling.Strict });
    }
    #endregion

    /// <summary>Explicit color management of an image: convert the pixels to sRGB.</summary>
    #region Imaging_ConvertToSrgb
    public static void ConvertToSrgb(string inputPath, string outputPath)
    {
        using var image = Image.Load(inputPath);

        // Decoders keep the embedded profile as a label; converting the colors is always explicit.
        // An image without profile is already sRGB (sGray for grayscale pixels): nothing is converted then.
        var grayscale = image.PixelFormat is PixelFormat.Gray8 or PixelFormat.Gray16;
        image.ConvertColorProfile(grayscale ? IccProfile.SrgbGray : IccProfile.Srgb);

        // Untagged pixels are read as sRGB: the label can be removed for formats that cannot store a profile
        image.Metadata.IccProfile = null;
        image.Save(outputPath);
    }
    #endregion

    /// <summary>A high dynamic range image is converted to sRGB by choosing how its highlights are compressed.</summary>
    #region Imaging_ConvertHdrToSrgb
    public static void ConvertHdrToSrgb(string avifPath, string pngPath)
    {
        using var image = Image.Load(avifPath);

        // The image has no ICC profile: its color description (BT.2020 primaries with PQ samples, for
        // example) is what the colors are converted from. PQ and HLG samples are only converted when
        // the options say how the light above the white of the result is compressed.
        var options = new IccColorTransformOptions
        {
            ToneMapping = new HdrToneMappingOptions { Operator = HdrToneMappingOperator.Bt2390 },
        };
        var grayscale = image.PixelFormat is PixelFormat.Gray8 or PixelFormat.Gray16;
        image.ConvertColorProfile(grayscale ? IccProfile.SrgbGray : IccProfile.Srgb, options);

        // The pixels are sRGB ones: the color description and the light levels went with the conversion
        image.Save(pngPath, new PngEncoder { MetadataHandling = MetadataHandling.Strict });
    }
    #endregion

    /// <summary>A CMYK JPEG is only loaded with a color conversion.</summary>
    #region Imaging_LoadCmykJpeg
    public static Image LoadCmykJpeg(string path)
    {
        // Default options: converted to sRGB from the CMYK profile of the file, or from
        // IccProfile.Cgats001CompatibleCmyk when it has none. Images that are not CMYK are loaded as usual.
        var conversion = new CmykConversionOptions
        {
            Transform = new IccColorTransformOptions { Intent = IccRenderingIntent.Perceptual },
        };

        // The pixels are RGB, labeled with the destination profile (image.Metadata.IccProfile)
        return Image.Load(path, new ImageDecodeOptions { CmykConversion = conversion });
    }
    #endregion

    /// <summary>Color conversion of sample buffers between two ICC profiles.</summary>
    #region Imaging_ConvertSamples
    public static byte[] ConvertSamplesToSrgb(IccProfile sourceProfile, ReadOnlySpan<byte> samples)
    {
        // A transform is immutable and thread-safe: create it once for a pair of profiles and reuse it
        var options = new IccColorTransformOptions { Intent = IccRenderingIntent.Perceptual };
        var transform = IccColorTransform.Create(sourceProfile, IccProfile.Srgb, options);

        // Interleaved samples without alpha: 1 (gray), 3 (RGB) or 4 (CMYK) per source color, 3 per sRGB color
        var converted = new byte[samples.Length / transform.SourceChannelCount * transform.DestinationChannelCount];
        transform.Convert(samples, converted);
        return converted;
    }
    #endregion

    /// <summary>Load a font file and read its names and its metrics at a size.</summary>
    #region Imaging_LoadFont
    public static string DescribeFont(string path)
    {
        // TrueType, OpenType, WOFF and WOFF2 are detected from the content of the file.
        var font = Font.Load(path);
        var metrics = font.GetMetrics(size: 16);
        var hasEuro = font.ContainsGlyph(new Rune('€'));
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{font.FamilyName} {font.SubfamilyName}: {font.GlyphCount} glyphs, ascent {metrics.Ascent:0.##}, descent {metrics.Descent:0.##}, euro sign: {hasEuro}");
    }
    #endregion

    /// <summary>Variable fonts: draw a word at five weights of one font file.</summary>
    #region Imaging_VariableFont
    public static void DrawWeights(string variableFontPath, string outputPath)
    {
        var font = Font.Load(variableFontPath);

        // The axes say what can vary, and in what range
        var weight = font.VariationAxes.First(axis => axis.Tag == "wght");

        using var image = new Image<Rgba32>(220, 200, new Rgba32(255, 255, 255));
        image.Draw(canvas =>
        {
            for (var i = 0; i < 5; i++)
            {
                // An instance is a font: it shares the data of the font it comes from
                var value = weight.Minimum + ((weight.Maximum - weight.Minimum) * i / 4);
                var instance = font.WithVariations(new FontVariationSetting("wght", value));
                canvas.FillText(new Rgba32(0, 0, 0), "Variable", instance, size: 32, new PointD(10, i * 38));
            }
        });

        image.Save(outputPath);
    }
    #endregion

    /// <summary>Text: measure a text with a layout, then fill and outline it on an image of its size.</summary>
    #region Imaging_DrawText
    public static void DrawLabel(string fontPath, string outputPath)
    {
        var font = Font.Load(fontPath);

        // A layout holds the glyphs of a text and their positions: it measures the text and is what is drawn
        var layout = TextLayout.Create("Hello, world!\nBonjour", font, size: 32);

        const int Margin = 8;
        using var image = new Image<Rgba32>((int)Math.Ceiling(layout.Width) + (2 * Margin), (int)Math.Ceiling(layout.Height) + (2 * Margin), new Rgba32(255, 255, 255));
        var outline = new Pen(new Rgba32(0, 0, 0), 1) { LineJoin = LineJoin.Round };
        image.Draw(canvas =>
        {
            // The origin is the top-left corner of the layout; its first baseline is layout.Baseline below
            var origin = new PointD(Margin, Margin);
            canvas.FillText(new Rgba32(30, 60, 160), layout, origin);
            canvas.StrokeText(outline, layout, origin);

            // A line under the first line of text, at its baseline
            canvas.StrokeLine(outline, new PointD(Margin, Margin + layout.Baseline + 3), new PointD(Margin + layout.Width, Margin + layout.Baseline + 3));
        });

        image.Save(outputPath);
    }
    #endregion

    /// <summary>Text: place texts by a point of their box, centered in a button and on a common baseline.</summary>
    #region Imaging_PlaceText
    public static void DrawPayButton(string fontPath, string outputPath)
    {
        var font = Font.Load(fontPath);
        var label = TextLayout.Create("Pay now", font, size: 20);
        var caption = TextLayout.Create("Total", font, size: 16);
        var amount = TextLayout.Create("42.00", font, size: 24);

        using var image = new Image<Rgba32>(240, 120, new Rgba32(255, 255, 255));
        image.Draw(canvas =>
        {
            // GetOrigin gives the top-left corner to draw a layout at so that a point of its box lands on an anchor.
            // The ink box centers what is drawn, the tail of the y included; the layout box would center the line
            var button = new RectangleD(20, 64, 200, 40);
            canvas.FillRoundedRectangle(new Rgba32(30, 60, 160), button, radius: 8);
            canvas.FillText(new Rgba32(255, 255, 255), label, label.GetOrigin(button, TextHorizontalAnchor.Center, TextVerticalAnchor.Center, TextBounds.Ink));

            // Two sizes on one baseline: the caption from the left, the amount aligned to the right
            var baseline = 44;
            canvas.FillText(new Rgba32(90, 90, 90), caption, caption.GetOrigin(new PointD(20, baseline), TextHorizontalAnchor.Left, TextVerticalAnchor.Baseline));
            canvas.FillText(new Rgba32(0, 0, 0), amount, amount.GetOrigin(new PointD(220, baseline), TextHorizontalAnchor.Right, TextVerticalAnchor.Baseline));
        });

        image.Save(outputPath);
    }
    #endregion

    /// <summary>Text: a wrapped and justified paragraph whose parts have their own style.</summary>
    #region Imaging_DrawParagraph
    public static void DrawParagraph(string fontPath, string outputPath)
    {
        var font = Font.Load(fontPath);
        var regular = new TextStyle(font, 18);
        var emphasis = new TextStyle(font, 18) { Brush = new SolidBrush(new Rgba32(180, 30, 30)), Decorations = TextDecorations.Underline };

        // A span is not a word or a line: the layout wraps the whole text, and shapes the letters of two spans together
        var text = new RichTextBuilder()
            .Append("Text is wrapped where ", regular)
            .Append("Unicode", emphasis)
            .Append(" allows a line to break, and each line is stretched to the width of the paragraph.", regular)
            .Build();
        var layout = TextLayout.Create(text, new TextLayoutOptions { MaxWidth = 300, Alignment = TextAlignment.Justify });

        using var image = new Image<Rgba32>(320, (int)Math.Ceiling(layout.Height) + 20, new Rgba32(255, 255, 255));

        // The brush of the call paints the spans that have none
        image.Draw(canvas => canvas.FillText(new Rgba32(0, 0, 0), layout, new PointD(10, 10)));
        image.Save(outputPath);
    }
    #endregion

    /// <summary>A transformation of the canvas: a text repeated along a diagonal, over an image.</summary>
    #region Imaging_DrawWatermark
    public static void DrawWatermark(string fontPath, string inputPath, string outputPath)
    {
        using var image = Image.Load(inputPath);
        var layout = TextLayout.Create("DRAFT", Font.Load(fontPath), size: image.Width / 6.0);
        var ink = new SolidBrush(new Rgba32(255, 255, 255, 110));
        var center = new PointD(image.Width / 2.0, image.Height / 2.0);

        image.Draw(canvas =>
        {
            // Everything the callback draws is turned around the center of the image: the text is drawn as usual
            canvas.Transform(AffineTransform.CreateRotation(-30, center), turned =>
            {
                for (var row = -1; row <= 1; row++)
                {
                    var middle = new PointD(center.X, center.Y + (row * layout.Height * 1.5));
                    turned.FillText(ink, layout, layout.GetOrigin(middle, TextHorizontalAnchor.Center, TextVerticalAnchor.Center));
                }
            });
        });

        image.Save(outputPath);
    }
    #endregion

    /// <summary>A title fitted to a box, with a highlighted word, an outlined word and an icon in the line.</summary>
    #region Imaging_DrawFittedText
    public static double DrawTitleCard(string fontPath, string iconPath, string outputPath)
    {
        var font = Font.Load(fontPath);
        using var icon = Image.Load(iconPath);
        using var image = new Image<Rgba32>(320, 120, Rgba32.ParseHex("#14213D"));

        // The styles: a word on a background, a word with a contour around its letters, and the rest
        var title = new TextStyle(font, 64) { Brush = new SolidBrush(Colors.White) };
        var highlight = new TextStyle(font, 64) { Brush = new SolidBrush(Rgba32.ParseHex("#14213D")), BackgroundBrush = new SolidBrush(Rgba32.ParseHex("#FCA311")) };
        var contour = new TextStyle(font, 64) { Brush = new SolidBrush(Colors.White), Outline = new Pen(Rgba32.ParseHex("#FCA311"), 2) { Alignment = PenAlignment.Outside, LineJoin = LineJoin.Round } };

        // Room for the icon in the line, as tall as a capital letter and standing on the baseline
        var capHeight = font.GetMetrics(64).CapHeight;
        var text = new RichTextBuilder()
            .AppendPlaceholder(capHeight, capHeight, capHeight, title)
            .Append(" Draw ", title)
            .Append("more", highlight)
            .Append(" with ", title)
            .Append("less", contour)
            .Build();

        // The largest size at which the text fits the card, less its margins: every length of the styles has it
        var box = new RectangleD(16, 12, image.Width - 32, image.Height - 24);
        var layout = TextLayout.CreateToFit(text, box.Width, box.Height, new TextLayoutOptions { Alignment = TextAlignment.Center });
        var origin = new PointD(box.X, box.Y + ((box.Height - layout.Height) / 2));

        image.Draw(canvas =>
        {
            canvas.FillText(Colors.White, layout, origin);

            // What the placeholder made room for, where the layout put it
            var place = layout.Placeholders[0];
            canvas.DrawImage(icon.Frames[0], new RectangleD(origin.X + place.X, origin.Y + place.Y, place.Width, place.Height), ImageSampling.Smooth);
        });

        image.Save(outputPath);
        return layout.Scale;
    }
    #endregion

    /// <summary>Text in vertical lines: Japanese set from the top to the bottom, in columns that go to the left.</summary>
    #region Imaging_DrawVerticalText
    public static void DrawVerticalText(string fontPath, string outputPath)
    {
        var style = new TextStyle(Font.Load(fontPath), 24);

        // Lines go down and follow each other to the left, and are wrapped at the height of the layout. The ideographs
        // and the kana are upright, with the vertical forms the font has for the brackets and the punctuation; the
        // Latin letters are on their side.
        var layout = TextLayout.Create(
            "「日本語」のテキスト。ABC 東京、日本。",
            style,
            new TextLayoutOptions { WritingMode = TextWritingMode.VerticalRightToLeft, MaxHeight = 160 });

        // The layout is as wide as its columns: the first one is on its right
        using var image = new Image<Rgba32>((int)Math.Ceiling(layout.Width) + 20, 180, new Rgba32(255, 255, 255));
        image.Draw(canvas => canvas.FillText(new Rgba32(0, 0, 0), layout, new PointD(10, 10)));
        image.Save(outputPath);
    }
    #endregion

    /// <summary>Text: a line of text that follows a curve.</summary>
    #region Imaging_DrawTextOnPath
    public static void DrawTextOnPath(string fontPath, string outputPath)
    {
        var font = Font.Load(fontPath);
        var layout = TextLayout.Create("Text follows the path it is given", font, size: 20);

        // An arch: the text is centered on it, its baseline 4 pixels above the path
        var arch = new VectorPathBuilder().MoveTo(20, 150).CubicBezierTo(100, 10, 300, 10, 380, 150).Build();
        var options = new TextPathOptions { Alignment = TextPathAlignment.Center, BaselineOffset = 4 };

        using var image = new Image<Rgba32>(400, 170, new Rgba32(255, 255, 255));
        image.Draw(canvas =>
        {
            canvas.Stroke(new Pen(new Rgba32(200, 200, 200), 1), arch);
            canvas.FillText(new Rgba32(30, 60, 160), layout, arch, options);
        });

        image.Save(outputPath);
    }
    #endregion
}
