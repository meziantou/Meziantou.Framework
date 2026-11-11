using System.Globalization;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.Pdf.Rendering;
using Meziantou.Framework.VectorGraphics;

namespace Meziantou.Framework.Pdf.VectorGraphics.Fuzz;

/// <summary>
/// The PDF of any scene (shapes, gradients, images, clips, masks, texts and their spans, links) opens again, tagged, has one page of the size of the scene, its text can be extracted and its page
/// rendered, and drawing the scene twice gives the same file. A link annotation is written for a group with an absolute
/// hyperlink only.
/// </summary>
public static class PdfVectorGraphicsFuzzTargets
{
    private static readonly string[] Tokens = ["<", "&", "(", ")", "\\", "https://", "mailto:", "a", "W", "i", " ", "ส", "中", "\U0001F600", "́", "\uD800", "\0"];

    // A PNG image of 2 by 2 pixels
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAAEklEQVR4nGP4zwAE/0Ho////AB/uBftt3M6tAAAAAElFTkSuQmCC");

    private static readonly double[] Values = [0, 1, -1, 0.5, 2.5, 10, -10, 50, 100, 1e-9, 1e6, -1e6, double.NaN, double.PositiveInfinity, double.NegativeInfinity, 3.3];

    private static readonly Lazy<SceneFontCollection> Fonts = new(() => new SceneFontCollection(new[] { "LiberationSans-Regular.woff2", "LiberationMono-Regular.woff2" }.SelectMany(name =>
    {
        using var stream = FuzzResources.Open(typeof(PdfVectorGraphicsFuzzTargets).Assembly, name);
        return SceneFontFace.Load(stream);
    })));

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("pdf-vectorgraphics-scene", SceneTarget) { Seeds = GetSeeds, Dictionary = Tokens },
        new("pdf-vectorgraphics-features", FeaturesTarget) { Seeds = GetFeaturesSeeds, Dictionary = Tokens },
    ];

    /// <summary>
    /// The input is a byte of options (fonts, scale), then a list of elements: a byte selects the kind of element and its
    /// options, a byte picks a number, and the text up to the next NUL byte gives its text, hyperlink or family.
    /// </summary>
    private static void SceneTarget(ReadOnlySpan<byte> data)
    {
        var options = FuzzInput.ReadByte(ref data);
        var pdfOptions = new ScenePdfOptions { Fonts = (options & 1) != 0 ? Fonts.Value : null, Scale = 0.25 + ((options >> 1) & 7) / 4.0 };
        var scene = CreateScene(data, out var absoluteLinks, out var hasFilter);
        if (hasFilter)
        {
            try
            {
                scene.ToPdf(pdfOptions);
            }
            catch (NotSupportedException)
            {
                return;
            }

            FuzzAssert.Fail("A scene with a filter was drawn as PDF");
        }

        var document = scene.ToPdf(pdfOptions);
        var bytes = document.ToByteArray();
        FuzzAssert.True(bytes.AsSpan().SequenceEqual(CreateScene(data, out _, out _).ToPdf(pdfOptions).ToByteArray()), "Drawing the scene twice gives different files");

        var reopened = PdfDocument.Open(bytes);
        FuzzAssert.Equal(1, reopened.Pages.Count, "The document does not have one page");
        var page = reopened.Pages[0];
        FuzzAssert.True(Math.Abs(page.MediaBox.Width - (scene.Width * pdfOptions.Scale)) < 1e-6 && Math.Abs(page.MediaBox.Height - (scene.Height * pdfOptions.Scale)) < 1e-6, $"The page is {page.MediaBox.Width} by {page.MediaBox.Height}");
        FuzzAssert.True(page.Annotations.Count <= absoluteLinks, $"The page has {page.Annotations.Count} links for {absoluteLinks} groups with an absolute hyperlink");
        _ = page.ExtractText();
        using var image = page.Render(new PdfRenderOptions { Dpi = 18 });
    }

    /// <summary>
    /// The PDF of a scene of patterns, markers, uses, view boxes, texts with a spacing or on a path, and elements with a
    /// transform or a hyperlink (<c>FeatureSceneBuilder</c>), drawn with or without fonts, opens again with one page, its
    /// text can be extracted and its page rendered, and drawing the scene twice gives the same file. A pattern, a marker
    /// or a use that draws itself does not loop.
    /// </summary>
    private static void FeaturesTarget(ReadOnlySpan<byte> data)
    {
        var options = FuzzInput.ReadByte(ref data);
        var pdfOptions = new ScenePdfOptions { Fonts = (options & 1) != 0 ? Fonts.Value : null };
        var scene = Meziantou.Framework.VectorGraphics.Fuzz.FeatureSceneBuilder.Create(data, allowFilters: false, out _);
        var bytes = scene.ToPdf(pdfOptions).ToByteArray();
        FuzzAssert.True(bytes.AsSpan().SequenceEqual(Meziantou.Framework.VectorGraphics.Fuzz.FeatureSceneBuilder.Create(data, allowFilters: false, out _).ToPdf(pdfOptions).ToByteArray()), "Drawing the scene twice gives different files");
        var reopened = PdfDocument.Open(bytes);
        FuzzAssert.Equal(1, reopened.Pages.Count, "The document does not have one page");
        _ = reopened.Pages[0].ExtractText();
        using var image = reopened.Pages[0].Render(new PdfRenderOptions { Dpi = 18 });
    }

    private static IEnumerable<FuzzSeed> GetFeaturesSeeds()
    {
        // The scenes of the unit tests: patterns, markers, uses, view boxes, spaced texts and texts along a path, with and without fonts
        byte[][] seeds =
        [
            [0, 0, 3, 0, 0 | 4 | 16, 4, 0, 1, 4 | 8 | 16, 0, 2, 4 | 8, 0],
            [1, 3, 5, .. "Spaced and overlined"u8, 0, 4, 0, .. "Text along a path"u8, 0, 4, 1, .. "around"u8, 0],
            [0, 5, 4, 0, 0, 3, 0, 6, 0, 0, 7 | 32, 1, 0, 7 | 16, 0, 0, 7, 5, .. "https://example.com/"u8, 0],
            [1, 9, 7, .. "https://example.com/"u8, 0, 0x80 | 11, 1, .. "link"u8, 0, 8, 3, .. "https://example.com/"u8, 0],
        ];
        for (var i = 0; i < seeds.Length; i++)
        {
            yield return new FuzzSeed("features-" + i.ToString(CultureInfo.InvariantCulture), seeds[i]);
        }
    }

    private static Scene CreateScene(ReadOnlySpan<byte> data, out int absoluteLinks, out bool hasFilter)
    {
        absoluteLinks = 0;
        hasFilter = false;
        var scene = new Scene(100, 80, "Liberation Sans") { Title = "Fuzz", Background = new SceneColor(250, 250, 250) };
        var groups = new Stack<SceneGroup>();
        groups.Push(scene.Root);
        var count = 0;
        while (data.Length >= 2 && count++ < 100)
        {
            var kind = FuzzInput.ReadByte(ref data);
            var value = FuzzInput.Pick(Values, FuzzInput.ReadByte(ref data));
            var text = FuzzText.Decode(FuzzInput.ReadUntil(ref data, 0));
            var parent = groups.Peek();
            var color = new SceneColor((byte)(kind * 37), (byte)(kind * 11), (byte)(kind * 5), (byte)(255 - (kind & 0xC0)));
            ScenePaint paint = (kind & 0x30) switch
            {
                0x20 => new LinearGradientPaint(0, 0, value, 10, [new(value, color), new(0.5, SceneColor.Black), new((kind & 7) / 7.0, SceneColor.White)]) { Spread = (SceneGradientSpread)(kind % 3), Transform = (kind & 8) != 0 ? SceneTransform.Scale(value, 0.5) : null },
                0x30 => new RadialGradientPaint(40, 30, value, 45, value, [new(0, color), new((kind & 7) / 7.0, SceneColor.White)]) { Spread = (SceneGradientSpread)(kind % 3), Transform = (kind & 8) != 0 ? SceneTransform.Rotate(value, 40, 30) : null },
                _ => color,
            };
            SceneElement element;
            switch (kind & 7)
            {
                case 0 when (kind & 0x40) != 0:
                    // A valid image, or the signature of a PNG file followed by the text
                    element = parent.Add(new SceneImage(value, 10, 30, 20, (kind & 8) != 0 ? Png : [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, .. System.Text.Encoding.UTF8.GetBytes(text)]));
                    break;
                case 0:
                    element = parent.Add(new SceneRectangle(value, 10, 30, 20) { Fill = paint, CornerRadius = kind % 9 });
                    break;
                case 1:
                    element = parent.Add(new SceneCircle(50, 40, value) { Stroke = paint, StrokeWidth = kind % 5, StrokeDashArray = (kind & 8) != 0 ? [2, value] : null, StrokeDashOffset = value, LineCap = (SceneLineCap)(kind % 3) });
                    break;
                case 2:
                    element = parent.Add(new SceneLine(0, value, 90, 70) { Stroke = paint, StrokeWidth = value, LineJoin = (SceneLineJoin)(kind % 3) });
                    break;
                case 3:
                    var path = new PathBuilder().MoveTo(10, 10).QuadraticTo(value, 0, 40, 40).ArcTo(value, 20, kind, largeArc: (kind & 8) != 0, sweep: (kind & 16) != 0, 70, 10);
                    if ((kind & 64) != 0)
                    {
                        path.Close().LineTo(5, value);
                    }

                    element = parent.Add(new ScenePath(path.Build()) { Fill = paint, Stroke = SceneColor.Black, StrokeWidth = 1 + (kind % 4), MiterLimit = value, FillRule = (SceneFillRule)(kind % 2) });
                    break;
                case 5 when (kind & 128) != 0:
                    // The text split in two spans: the second has its own font, color and lines
                    element = parent.Add(new SceneText(value, 40, [new SceneTextSpan(text[..(text.Length / 2)]), new SceneTextSpan(text[(text.Length / 2)..]) { Font = new SceneFont(1 + (kind % 30), Weight: 100 * (kind % 11), Family: (kind & 64) != 0 ? "Liberation Mono" : null), Color = color, Decorations = (SceneTextDecorations)(kind % 4) }], new SceneFont(12), SceneColor.Black)
                    {
                        Anchor = (SceneTextAnchor)(kind % 3),
                        Decorations = (SceneTextDecorations)(kind % 3),
                    });
                    break;
                case 4:
                case 5:
                    element = parent.Add(new SceneText(value, 40, text, new SceneFont(1 + (kind % 30), Weight: (kind & 8) != 0 ? 100 * (1 + (kind % 9)) : SceneFont.NormalWeight, Italic: (kind & 16) != 0, Family: (kind & 64) != 0 ? "Liberation Mono" : null), color)
                    {
                        Anchor = (SceneTextAnchor)(kind % 3),
                        Rotation = (kind & 32) != 0 ? value : 0,
                        Decorations = (SceneTextDecorations)(kind % 4),
                    });
                    break;
                case 6:
                    var group = parent.Add(new SceneGroup
                    {
                        Hyperlink = (kind & 8) != 0 ? text : null,
                        Clip = (kind & 16) != 0 ? new SceneBounds(value, 5, 60, 60) : null,
                        Transform = (kind & 32) != 0 ? SceneTransform.Rotate(value, 50, 40).Then(SceneTransform.Scale(0.5, 2)) : null,
                        ClipPath = (kind & 64) != 0 ? new PathBuilder().AddCircle(50, 40, value).Build() : null,
                        ClipRule = (SceneFillRule)(kind % 2),
                    });
                    if (group.Hyperlink is not null && Uri.TryCreate(group.Hyperlink, UriKind.Absolute, out _))
                    {
                        absoluteLinks++;
                    }

                    groups.Push(group);
                    element = group;
                    break;
                default:
                    if (groups.Count > 1)
                    {
                        groups.Pop();
                    }

                    continue;
            }

            element.Opacity = (kind & 0x80) != 0 ? (kind % 11) / 10.0 : 1;

            // The effects: a mask whose content is a rectangle, a blend mode, and sometimes a filter, which PDF cannot draw
            if (kind % 7 == 3)
            {
                element.Mask = new SceneMask { Mode = (SceneMaskMode)(kind % 2) };
                element.Mask.Content.Add(new SceneRectangle(value, 0, 60, 60) { Fill = new LinearGradientPaint(0, 0, 100, 0, [new(0, SceneColor.White), new(1, SceneColor.Black)]) });
            }

            element.BlendMode = (kind & 0x10) != 0 ? (SceneBlendMode)(kind % 16) : SceneBlendMode.Normal;
            if (kind % 31 == 7)
            {
                element.Filter = (kind & 1) != 0 ? new SceneBlurFilter(value) : SceneColorMatrixFilter.Sepia(value);
                hasFilter = true;
            }
        }

        return scene;
    }

    private static IEnumerable<FuzzSeed> GetSeeds()
    {
        byte[][] seeds =
        [
            [0, 0, 6, 0, 1, 3, 0, 2, 4, 0, 3, 8, 0, 4, 2, .. "Hello & (world)"u8, 0],
            [1, 6 | 8 | 32, 4, .. "https://example.com/"u8, 0, 0x80 | 0x20 | 0, 7, 0, 4 | 64, 3, .. "code"u8, 0, 7, 0, 0],
            [3, 0x80 | 6, 2, 0, 0x20 | 3 | 8 | 16, 6, 0, 5 | 32, 15, 0xE0, 0xB8, 0xAA, 0xE4, 0xB8, 0xAD, 0xF0, 0x9F, 0x98, 0x80, 0],
            [0, 3 | 64, 13, 0, 1 | 8, 14, 0, 2, 12, 0, 6 | 16 | 32, 12, 0, 0, 13, 0],
            [0, 6 | 64, 8, 0, 0x40 | 8, 3, 0, 0x30 | 0, 7, 0, 0x40, 2, .. "not an image"u8, 0, 7, 0, 0],
        ];
        for (var i = 0; i < seeds.Length; i++)
        {
            yield return new FuzzSeed("scene-" + i.ToString(CultureInfo.InvariantCulture), seeds[i]);
        }
    }
}
