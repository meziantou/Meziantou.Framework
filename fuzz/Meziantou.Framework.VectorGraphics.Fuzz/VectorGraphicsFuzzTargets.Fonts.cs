using System.Globalization;
using System.Xml;
using Meziantou.Framework.Fonts;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.VectorGraphics.Fuzz;

public static partial class VectorGraphicsFuzzTargets
{
    private static string[] FontFiles => ["LiberationSans-Regular.woff2", "LiberationSans-Bold.woff2", "LiberationMono-Regular.woff2", "NotoSansThai-Regular.ttf"];

    private static string[] FontFileTokens => ["wOF2", "wOFF", "OTTO", "true", "ttcf", "\0\u0001\0\0", "cmap", "head", "hhea", "hmtx", "maxp", "name", "OS/2", "post", "glyf", "loca", "CFF ", "GSUB", "GPOS", "fvar"];

    private static string[] FontFamilyTokens => ["Liberation Sans", "liberation mono", "Noto Sans Thai", ",", "\"", "'", "\\", " ", "serif", "monospace", "<", "&", "]]>", "ส", "ั", "中", "\U0001F600", "‍", "️", "́", "a", "i"];

    private static readonly Lazy<SceneFontCollection> Fonts = new(() => new SceneFontCollection(FontFiles.SelectMany(file => SceneFontFace.Load(ReadResource(file)))));

    private static byte[] ReadResource(string name)
    {
        using var stream = FuzzResources.Open(typeof(VectorGraphicsFuzzTargets).Assembly, name);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>
    /// <see cref="SceneFontFace.Load(ReadOnlySpan{byte})"/> gives the faces of a font file, numbered in order and keeping
    /// the bytes of the file, or throws a <see cref="FontException"/>.
    /// </summary>
    private static void FontFaceTarget(ReadOnlySpan<byte> data)
    {
        IReadOnlyList<SceneFontFace> faces;
        try
        {
            faces = SceneFontFace.Load(data);
        }
        catch (FontException)
        {
            return;
        }

        FuzzAssert.True(faces.Count > 0, "A font file has no face");
        for (var i = 0; i < faces.Count; i++)
        {
            FuzzAssert.Equal(i, faces[i].Index, "The faces are not numbered in order");
            FuzzAssert.True(faces[i].Data.Span.SequenceEqual(data), "A face does not keep the bytes of the file");
            FuzzAssert.True(faces[i].Weight is >= 1 and <= 1000, $"The weight {faces[i].Weight} is out of range");
            FuzzAssert.True(faces[i].FamilyName is not null, "A face has no family name");
        }
    }

    /// <summary>
    /// The input is a byte of options (bold, italic, embedded fonts, size), the family of the text up to a NUL byte, and the
    /// text. The fonts resolve the family to a face of the collection, the faces of the other families follow it once each,
    /// and a family the collection does not have resolves to the default one. The width of a text is the sum of the widths
    /// of its runs, each measured with its face alone; a measurer with a family measures the texts that name none with it.
    /// The SVG of a scene that uses the family is a well-formed document and is deterministic.
    /// </summary>
    private static void FontsTarget(ReadOnlySpan<byte> data)
    {
        var options = FuzzInput.ReadByte(ref data);
        var family = FuzzText.Decode(FuzzInput.ReadUntil(ref data, 0));
        var text = FuzzText.Decode(data);
        var fonts = Fonts.Value;
        var font = new SceneFont(1 + (options >> 3), Bold: (options & 1) != 0, Italic: (options & 2) != 0, Family: family);

        var faces = fonts.GetFaces(font);
        FuzzAssert.True(ReferenceEquals(fonts.GetFace(font), faces[0]), "GetFace is not the first face of GetFaces");
        FuzzAssert.Equal(fonts.Families.Count, faces.Count, "GetFaces does not have one face per family");
        FuzzAssert.Equal(fonts.Families.Count, faces.Select(face => face.FamilyName).Distinct(StringComparer.OrdinalIgnoreCase).Count(), "GetFaces has a family twice");
        FuzzAssert.True(faces.All(face => fonts.Faces.Contains(face)), "GetFaces returned a face of another collection");
        // The names of a list are its words, unescaped: a family none of them can name resolves to the default family
        var words = string.Join(' ', family.Replace("\\", "", StringComparison.Ordinal).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (!fonts.Families.Any(name => words.Contains(name, StringComparison.OrdinalIgnoreCase)))
        {
            FuzzAssert.Equal(fonts.DefaultFamily, faces[0].FamilyName, $"The unknown family {FuzzAssert.Format(family)} does not resolve to the default family");
        }

        var measurer = new FontFaceTextMeasurer(fonts);
        var metrics = measurer.Measure(text, font);
        FuzzAssert.True(double.IsFinite(metrics.Width) && metrics.Width >= 0, $"The width {metrics.Width} is not a finite positive number");
        FuzzAssert.True(metrics.Ascent >= 0 && metrics.Descent >= 0 && double.IsFinite(metrics.Height), "The ascent or the descent is negative");

        var expected = 0.0;
        var position = 0;
        foreach (var (range, face) in SceneFontCollection.SplitRuns(text, faces))
        {
            var (offset, length) = range.GetOffsetAndLength(text.Length);
            FuzzAssert.Equal(position, offset, "The runs do not cover the text in order");
            position += length;
            FuzzAssert.True(length > 0, "A run is empty");
            expected += new FontFaceTextMeasurer(new SceneFontCollection(face)).Measure(text[range], font with { Family = null }).Width;
        }

        FuzzAssert.Equal(text.Length, position, "The runs do not cover the text");
        FuzzAssert.True(Math.Abs(expected - metrics.Width) <= 1e-9 * (1 + expected), $"The width {metrics.Width} is not the sum {expected} of the widths of the runs");
        if (family.Length > 0)
        {
            FuzzAssert.Equal(metrics, measurer.WithFontFamily(family).Measure(text, font with { Family = null }), "WithFontFamily does not measure with the family");
        }

        var scene = new Scene(100, 100, measurer.FontFamily) { Fonts = fonts };
        scene.Root.Add(new SceneText(0, 50, text, font, SceneColor.Black));
        scene.Root.Add(new SceneText(0, 80, text, font with { Family = null }, SceneColor.Black));
        var svgOptions = new SvgRenderOptions { EmbedFonts = (options & 4) != 0 };
        var svg = SvgSceneRenderer.Render(scene, svgOptions);
        FuzzAssert.Equal(svg, SvgSceneRenderer.Render(scene, svgOptions), "Rendering the scene twice gives different outputs");
        try
        {
            var document = new XmlDocument { XmlResolver = null };
            using var reader = XmlReader.Create(new StringReader(svg), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            document.Load(reader);
        }
        catch (XmlException exception)
        {
            FuzzAssert.Fail($"The SVG is not well-formed: {exception.Message}");
        }
    }

    private static IEnumerable<FuzzSeed> GetFontFaceSeeds()
    {
        foreach (var file in FontFiles)
        {
            var bytes = ReadResource(file);
            yield return new FuzzSeed("font-" + Path.GetFileNameWithoutExtension(file), bytes);
            yield return new FuzzSeed("truncated-" + Path.GetFileNameWithoutExtension(file), bytes.AsMemory(0, bytes.Length / 2));
        }

        yield return new FuzzSeed("empty", Array.Empty<byte>());
        yield return FuzzSeed.FromText("text", "not a font");
    }

    private static IEnumerable<FuzzSeed> GetFontsSeeds()
    {
        (byte Options, string Family, string Text)[] seeds =
        [
            (10 << 3, "", "The quick brown fox"),
            ((12 << 3) | 1, "Liberation Mono", "var x = 1;"),
            ((20 << 3) | 2 | 4, "'Unknown', \"liberation mono\", monospace", "code"),
            (16 << 3, "Noto Sans Thai", "ab สวัสดี"),
            ((8 << 3) | 4, "Arial, sans-serif", "中文 \U0001F600 é ש"),
            (5 << 3, "\"</style><x>&amp;\"", "<tag> & \"quotes\""),
            (0, "Liberation Sans", ""),
        ];
        for (var i = 0; i < seeds.Length; i++)
        {
            yield return FuzzSeed.FromText("fonts-" + i.ToString(CultureInfo.InvariantCulture), [seeds[i].Options, .. FuzzText.Encode(seeds[i].Family), 0], seeds[i].Text);
        }

        // The texts the unit tests measure
        var source = FuzzResources.ReadText(typeof(VectorGraphicsFuzzTargets).Assembly, "TextMeasurerTests.cs");
        var index = 0;
        foreach (System.Text.RegularExpressions.Match match in StringLiteral().Matches(source))
        {
            yield return FuzzSeed.FromText("test-" + index.ToString(CultureInfo.InvariantCulture), [(byte)((12 << 3) | (index & 3)), .. "Liberation Mono"u8, 0], Unescape(match.Groups["value"].Value));
            index++;
        }
    }
}
