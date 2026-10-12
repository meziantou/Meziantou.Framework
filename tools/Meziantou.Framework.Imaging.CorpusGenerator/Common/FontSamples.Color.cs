namespace Meziantou.Framework.Imaging.CorpusGenerator.Common;

/// <content>The synthetic color fonts.</content>
internal static partial class FontSamples
{
    public const int ColorA = 2;
    public const int ColorB = 6;
    public const int ColorC = 9;

    /// <summary>
    /// A color font made of rectangles on whole pixels at 16 pixels per em: 1024 units per em, an ascent of 832 and a
    /// descent of 192, two palettes of four colors, and a <c>COLR</c> table of version 0.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description>Palette 0, for light backgrounds: opaque red, opaque green, blue at an opacity of 128 on 255, white at 64 on 255. Palette 1, for dark backgrounds: opaque yellow, opaque magenta, opaque cyan, black at 128 on 255.</description></item>
    /// <item><description>A, advance 1024: three layers, the rectangle (128, 0)-(896, 768) with the first color, the rectangle (256, 128)-(768, 640) with the second one, and the rectangle (384, 256)-(640, 512) with the color of the text. Its own outline is the first rectangle.</description></item>
    /// <item><description>B, advance 512: two layers, the rectangle (64, 0)-(448, 512) with the third color and its upper half, (64, 256)-(448, 512), with the fourth one.</description></item>
    /// <item><description>C, advance 768: the rectangle (0, -192)-(768, 832), without colors of its own.</description></item>
    /// <item><description>Space: advance 512.</description></item>
    /// </list>
    /// </remarks>
    public static FontBuilder ColorBoxes()
    {
        var font = new FontBuilder
        {
            FamilyName = "Meziantou Color Boxes",
            PostScriptName = "MeziantouColorBoxes-Regular",
            UnitsPerEm = 1024,
            Ascender = 832,
            Descender = -192,
            TypoAscender = 832,
            TypoDescender = -192,
            WinAscent = 832,
            WinDescent = 192,
            CapHeight = 768,
            XHeight = 512,
            UnderlinePosition = -128,
            UnderlineThickness = 64,
            StrikeoutPosition = 256,
            StrikeoutSize = 64,
        };

        font.AddGlyph(FontGlyph.Frame(512, 64, 0, 448, 768, 64));
        font.AddGlyph(FontGlyph.Empty(512), ' ');
        font.AddGlyph(FontGlyph.Rectangle(1024, 128, 0, 896, 768), 'A');
        font.AddGlyph(FontGlyph.Rectangle(0, 128, 0, 896, 768));
        font.AddGlyph(FontGlyph.Rectangle(0, 256, 128, 768, 640));
        font.AddGlyph(FontGlyph.Rectangle(0, 384, 256, 640, 512));
        font.AddGlyph(FontGlyph.Rectangle(512, 64, 0, 448, 512), 'B');
        font.AddGlyph(FontGlyph.Rectangle(0, 64, 0, 448, 512));
        font.AddGlyph(FontGlyph.Rectangle(0, 64, 256, 448, 512));
        font.AddGlyph(FontGlyph.Rectangle(768, 0, -192, 768, 832), 'C');

        font.ColorPalettes.Add([(255, 0, 0, 255), (0, 255, 0, 255), (0, 0, 255, 128), (255, 255, 255, 64)]);
        font.ColorPalettes.Add([(255, 255, 0, 255), (255, 0, 255, 255), (0, 255, 255, 255), (0, 0, 0, 128)]);
        font.ColorPaletteTypes = [1, 2];
        font.ColorLayers[ColorA] = [(3, 0), (4, 1), (5, 0xFFFF)];
        font.ColorLayers[ColorB] = [(7, 2), (8, 3)];
        return font;
    }

    /// <summary>
    /// The color boxes font with a <c>COLR</c> table of version 1: A and B are the same layers, written as paints, and
    /// other characters are given a paint by the caller.
    /// </summary>
    /// <param name="paints">The paint of each character that is added to the font; each one has the outline of A and an advance of 1024.</param>
    public static FontBuilder ColorPaints(params (char Character, FontColorPaint Paint)[] paints)
    {
        var font = ColorBoxes();
        font.FamilyName = "Meziantou Color Paints";
        font.PostScriptName = "MeziantouColorPaints-Regular";
        font.ColorLayers.Clear();
        font.ColorPaints[ColorA] = new FontColorPaint.Layers(
            new FontColorPaint.Glyph(3, new FontColorPaint.Solid(0)),
            new FontColorPaint.Glyph(4, new FontColorPaint.Solid(1)),
            new FontColorPaint.Glyph(5, new FontColorPaint.Solid(0xFFFF)));
        font.ColorPaints[ColorB] = new FontColorPaint.Layers(
            new FontColorPaint.Glyph(7, new FontColorPaint.Solid(2)),
            new FontColorPaint.Glyph(8, new FontColorPaint.Solid(3)));
        foreach (var (character, paint) in paints)
        {
            font.ColorPaints[font.AddGlyph(FontGlyph.Rectangle(1024, 128, 0, 896, 768), character)] = paint;
        }

        return font;
    }

    /// <summary>
    /// The color paints font with glyphs that transform, clip and compose paints. Glyph 3 is the outer rectangle of A,
    /// (128, 0)-(896, 768); glyph 4 its middle one, (256, 128)-(768, 640); glyph 5 its inner one, (384, 256)-(640, 512);
    /// glyph 8 the upper half of B, (64, 256)-(448, 512). Colors 0 and 1 are red and green.
    /// </summary>
    /// <remarks>
    /// D: the outer rectangle at half opacity. E: the inner one moved by (128, 128). F: the inner one scaled by 1.5 around
    /// its center. G: the upper half of B turned by a quarter of a turn around its center. H: a fill within two outlines.
    /// I, J, K: compositions (source in, destination out, multiply). L: the color of the text at half opacity. M: a
    /// matrix. N: a skew without angle. O: a paint of an unknown format under a fill. P: a scale around the origin.
    /// </remarks>
    public static FontBuilder ColorTransforms()
    {
        var font = ColorPaints(
            ('D', new FontColorPaint.Glyph(3, new FontColorPaint.Solid(0, 0.5))),
            ('E', new FontColorPaint.Translate(new FontColorPaint.Glyph(5, new FontColorPaint.Solid(1)), 128, 128)),
            ('F', new FontColorPaint.Scale(new FontColorPaint.Glyph(5, new FontColorPaint.Solid(0)), 1.5, 1.5, 512, 384)),
            ('G', new FontColorPaint.Rotate(new FontColorPaint.Glyph(8, new FontColorPaint.Solid(0)), 90, 256, 384)),
            ('H', new FontColorPaint.Glyph(3, new FontColorPaint.Glyph(4, new FontColorPaint.Solid(0)))),
            ('I', new FontColorPaint.Composite(new FontColorPaint.Glyph(3, new FontColorPaint.Solid(0)), 5, new FontColorPaint.Glyph(5, new FontColorPaint.Solid(1)))),
            ('J', new FontColorPaint.Composite(new FontColorPaint.Glyph(5, new FontColorPaint.Solid(0)), 8, new FontColorPaint.Glyph(3, new FontColorPaint.Solid(1)))),
            ('K', new FontColorPaint.Composite(new FontColorPaint.Glyph(5, new FontColorPaint.Solid(0)), 23, new FontColorPaint.Glyph(3, new FontColorPaint.Solid(1)))),
            ('L', new FontColorPaint.Glyph(3, new FontColorPaint.Solid(0xFFFF, 0.5))),
            ('M', new FontColorPaint.Transform(new FontColorPaint.Glyph(5, new FontColorPaint.Solid(0)), 1, 0, 0, 2, 64, -256)),
            ('N', new FontColorPaint.Skew(new FontColorPaint.Glyph(5, new FontColorPaint.Solid(0)), 0, 0)),
            ('O', new FontColorPaint.Layers(new FontColorPaint.Unknown(99), new FontColorPaint.Glyph(4, new FontColorPaint.Solid(1)))),
            ('P', new FontColorPaint.Scale(new FontColorPaint.Glyph(5, new FontColorPaint.Solid(0)), 0.5, 1.5)));
        font.FamilyName = "Meziantou Color Transforms";
        font.PostScriptName = "MeziantouColorTransforms-Regular";
        return font;
    }

    /// <summary>
    /// The color paints font with glyphs that fill the outer rectangle of A, (128, 0)-(896, 768), with gradients from
    /// red to green.
    /// </summary>
    /// <remarks>
    /// D: linear, from the left to the right. E: radial, from the center (512, 384) to a radius of 384. F: a sweep around
    /// the center from 0 to 180 degrees. G: a color line from 0.25 to 0.75 that repeats. H: a color line from 0 to 0.5
    /// that reflects. I: a gradient without length. J: stops at one place. K: a stop that is the color of the text at half
    /// opacity. L: as D, with a P1 that is not on the perpendicular of P0 P2. M, N: sweeps whose angles are equal, which
    /// pad and repeat. O: stops at one place that reflect. P: a sweep whose start is after its end.
    /// </remarks>
    public static FontBuilder ColorGradients()
    {
        var redToGreen = new FontColorLine(0, (0, 0, 1), (1, 1, 1));
        var font = ColorPaints(
            ('D', new FontColorPaint.Glyph(3, new FontColorPaint.Linear(redToGreen, 128, 0, 896, 0, 128, 768))),
            ('E', new FontColorPaint.Glyph(3, new FontColorPaint.Radial(redToGreen, 512, 384, 0, 512, 384, 384))),
            ('F', new FontColorPaint.Glyph(3, new FontColorPaint.Sweep(redToGreen, 512, 384, 0, 180))),
            ('G', new FontColorPaint.Glyph(3, new FontColorPaint.Linear(new FontColorLine(1, (0.25, 0, 1), (0.75, 1, 1)), 128, 0, 896, 0, 128, 768))),
            ('H', new FontColorPaint.Glyph(3, new FontColorPaint.Linear(new FontColorLine(2, (0, 0, 1), (0.5, 1, 1)), 128, 0, 896, 0, 128, 768))),
            ('I', new FontColorPaint.Glyph(3, new FontColorPaint.Linear(redToGreen, 128, 0, 128, 0, 128, 768))),
            ('J', new FontColorPaint.Glyph(3, new FontColorPaint.Linear(new FontColorLine(0, (0.5, 0, 1), (0.5, 1, 1)), 128, 0, 896, 0, 128, 768))),
            ('K', new FontColorPaint.Glyph(3, new FontColorPaint.Linear(new FontColorLine(0, (0, 0, 1), (1, 0xFFFF, 0.5)), 128, 0, 896, 0, 128, 768))),
            ('L', new FontColorPaint.Glyph(3, new FontColorPaint.Linear(redToGreen, 128, 0, 896, 768, 128, 768))),
            ('M', new FontColorPaint.Glyph(3, new FontColorPaint.Sweep(redToGreen, 512, 384, 90, 90))),
            ('N', new FontColorPaint.Glyph(3, new FontColorPaint.Sweep(new FontColorLine(1, (0, 0, 1), (1, 1, 1)), 512, 384, 90, 90))),
            ('O', new FontColorPaint.Glyph(3, new FontColorPaint.Linear(new FontColorLine(2, (0.5, 0, 1), (0.5, 1, 1)), 128, 0, 896, 0, 128, 768))),
            ('P', new FontColorPaint.Glyph(3, new FontColorPaint.Sweep(redToGreen, 512, 384, 180, 0))));
        font.FamilyName = "Meziantou Color Gradients";
        font.PostScriptName = "MeziantouColorGradients-Regular";
        return font;
    }

    /// <summary>
    /// The color paints font with glyphs that have a clip box and glyphs that paint other glyphs.
    /// </summary>
    /// <remarks>
    /// D (glyph 10): the outer rectangle of A, with the clip box (128, 0)-(512, 768). E: the inner rectangle, then the
    /// paint of D. F (glyph 12): a fill without outline, with the clip box (256, 128)-(768, 640). G (glyph 13): the
    /// middle rectangle, then its own paint. H: the paint of D moved by (128, 0). I: the paint of D twice, side by side.
    /// </remarks>
    public static FontBuilder ColorClips()
    {
        var font = ColorPaints(
            ('D', new FontColorPaint.Glyph(3, new FontColorPaint.Solid(0))),
            ('E', new FontColorPaint.Layers(new FontColorPaint.Glyph(5, new FontColorPaint.Solid(1)), new FontColorPaint.ColrGlyph(10))),
            ('F', new FontColorPaint.Solid(1)),
            ('G', new FontColorPaint.Layers(new FontColorPaint.Glyph(4, new FontColorPaint.Solid(0)), new FontColorPaint.ColrGlyph(13))),
            ('H', new FontColorPaint.Translate(new FontColorPaint.ColrGlyph(10), 128, 0)),
            ('I', new FontColorPaint.Layers(new FontColorPaint.ColrGlyph(10), new FontColorPaint.Translate(new FontColorPaint.ColrGlyph(10), 384, 0))));
        font.FamilyName = "Meziantou Color Clips";
        font.PostScriptName = "MeziantouColorClips-Regular";
        font.ColorClipBoxes[10] = (128, 0, 512, 768);
        font.ColorClipBoxes[12] = (256, 128, 768, 640);
        return font;
    }

    public const int BitmapA = 2;
    public const int BitmapB = 3;
    public const int BitmapC = 4;
    public const int BitmapD = 5;

    /// <summary>
    /// A font whose glyphs are images, in two strikes of 16 and 32 pixels per em: 1024 units per em, an ascent of 832
    /// and a descent of 192. At the size of a strike, a pixel of an image is a pixel of the canvas.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description>A, advance 1024. At 16 pixels per em, an image of 12 by 12 pixels, 2 pixels from the origin, on the baseline: red, with a green square of 4 pixels in its middle and a last row of blue at an opacity of 128 on 255. At 32 pixels per em, 24 by 24 pixels, 4 pixels from the origin: yellow, with a magenta square of 8 pixels in its middle.</description></item>
    /// <item><description>B, advance 512. At 16 pixels per em, 6 by 8 pixels, 1 pixel from the origin: cyan, with a transparent first pixel. At 32 pixels per em, 12 by 16 pixels, 2 pixels from the origin: the color (0, 128, 255).</description></item>
    /// <item><description>C, advance 768: the rectangle (0, -192)-(768, 832), without image.</description></item>
    /// <item><description>D, advance 1024: the images of A. In the <c>sbix</c> table, it refers to them.</description></item>
    /// <item><description>A, B and D also have an outline, the rectangle (128, 0)-(896, 768) or (64, 0)-(448, 512), for what does not draw images.</description></item>
    /// </list>
    /// </remarks>
    /// <param name="useColorBitmapTables">Whether the images are in <c>CBLC</c> and <c>CBDT</c> tables instead of an <c>sbix</c> table.</param>
    /// <param name="indexFormat">The format of the index subtables of the <c>CBLC</c> table.</param>
    /// <param name="imageFormat">The format of the images of the <c>CBDT</c> table.</param>
    public static FontBuilder BitmapBoxes(bool useColorBitmapTables = false, int indexFormat = 1, int imageFormat = 17)
    {
        var font = new FontBuilder
        {
            FamilyName = useColorBitmapTables ? "Meziantou Color Bitmaps" : "Meziantou Bitmap Boxes",
            PostScriptName = useColorBitmapTables ? "MeziantouColorBitmaps-Regular" : "MeziantouBitmapBoxes-Regular",
            UnitsPerEm = 1024,
            Ascender = 832,
            Descender = -192,
            TypoAscender = 832,
            TypoDescender = -192,
            WinAscent = 832,
            WinDescent = 192,
            CapHeight = 768,
            XHeight = 512,
            UseColorBitmapTables = useColorBitmapTables,
        };

        font.AddGlyph(FontGlyph.Frame(512, 64, 0, 448, 768, 64));
        font.AddGlyph(FontGlyph.Empty(512), ' ');
        font.AddGlyph(FontGlyph.Rectangle(1024, 128, 0, 896, 768), 'A');
        font.AddGlyph(FontGlyph.Rectangle(512, 64, 0, 448, 512), 'B');
        font.AddGlyph(FontGlyph.Rectangle(768, 0, -192, 768, 832), 'C');
        font.AddGlyph(FontGlyph.Rectangle(1024, 128, 0, 896, 768), 'D');

        var small = new FontBitmapStrike { PixelsPerEm = 16, IndexFormat = indexFormat, ImageFormat = imageFormat };
        var large = new FontBitmapStrike { PixelsPerEm = 32, IndexFormat = indexFormat, ImageFormat = imageFormat };
        var smallA = CreateBitmap(12, 12, static (x, y) => x is >= 4 and < 8 && y is >= 4 and < 8 ? (0, 255, 0, 255) : y == 11 ? (0, 0, 255, 128) : (255, 0, 0, 255));
        var largeA = CreateBitmap(24, 24, static (x, y) => x is >= 8 and < 16 && y is >= 8 and < 16 ? (255, 0, 255, 255) : (255, 255, 0, 255));
        var smallB = CreateBitmap(6, 8, static (x, y) => x == 0 && y == 0 ? (0, 0, 0, 0) : (0, 255, 255, 255));
        var largeB = CreateBitmap(12, 16, static (_, _) => (0, 128, 255, 255));

        // The index formats that give every image the metrics of the subtable hold images of one size: B has the
        // image of A in these fonts.
        var hasOneSize = useColorBitmapTables && indexFormat is 2 or 5;
        small.Glyphs.Add(new FontBitmapGlyph(BitmapA, 2, 0, 12, 12, smallA));
        large.Glyphs.Add(new FontBitmapGlyph(BitmapA, 4, 0, 24, 24, largeA));
        small.Glyphs.Add(hasOneSize ? new FontBitmapGlyph(BitmapB, 2, 0, 12, 12, smallA) : new FontBitmapGlyph(BitmapB, 1, 0, 6, 8, smallB));
        large.Glyphs.Add(hasOneSize ? new FontBitmapGlyph(BitmapB, 4, 0, 24, 24, largeA) : new FontBitmapGlyph(BitmapB, 2, 0, 12, 16, largeB));

        // The index format 2 is for a range of glyphs that all have an image: C, which has none, ends it.
        if (!(useColorBitmapTables && indexFormat == 2))
        {
            small.Glyphs.Add(new FontBitmapGlyph(BitmapD, 2, 0, 12, 12, smallA) { DuplicateOf = useColorBitmapTables ? null : BitmapA });
            large.Glyphs.Add(new FontBitmapGlyph(BitmapD, 4, 0, 24, 24, largeA) { DuplicateOf = useColorBitmapTables ? null : BitmapA });
        }

        font.BitmapStrikes.Add(small);
        font.BitmapStrikes.Add(large);
        return font;
    }

    /// <summary>Makes a PNG image from the color of each pixel: red, green, blue and alpha.</summary>
    public static byte[] CreateBitmap(int width, int height, Func<int, int, (int R, int G, int B, int A)> getColor)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var (r, g, b, a) = getColor(x, y);
                pixels[(((y * width) + x) * 4) + 0] = (byte)r;
                pixels[(((y * width) + x) * 4) + 1] = (byte)g;
                pixels[(((y * width) + x) * 4) + 2] = (byte)b;
                pixels[(((y * width) + x) * 4) + 3] = (byte)a;
            }
        }

        return FontPng.Write(width, height, pixels);
    }
}
