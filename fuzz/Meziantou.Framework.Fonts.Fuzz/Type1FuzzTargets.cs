using System.Globalization;
using System.Text;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.Imaging.CorpusGenerator.Common;

namespace Meziantou.Framework.Fonts.Fuzz;

/// <summary>
/// Loading a PostScript Type 1 font must only throw <see cref="FontException"/>. A loaded font is an OpenType font with
/// CFF outlines whose charstrings were all converted when it was loaded: every glyph decodes without
/// <see cref="InvalidFontException"/>, with finite coordinates and an advance that is not negative, and the bytes of the
/// converted font load again as the same font (same glyphs, advances and outlines).
/// </summary>
public static class Type1FuzzTargets
{
    private const int MaxCheckedGlyphs = 256;

    private static readonly FontResourceLimits Limits = new()
    {
        MaxEncodedBytes = 4 * 1024 * 1024,
        MaxFontBytes = 16 * 1024 * 1024,
        MaxGlyphComplexity = 200_000,
    };

    private static readonly PooledFontEnvironment SharedEnvironment = new(Limits, 64 * 1024 * 1024);

    // The metrics the files are loaded with when the first byte of the input says so: kerning pairs of the glyphs of the
    // sample font, and the global metrics
    private static readonly Lazy<AfmFontMetrics> Metrics = new(() => AfmFontMetrics.Parse("""
        StartFontMetrics 4.1
        Ascender 750
        Descender -250
        CapHeight 700
        XHeight 500
        StartKernData
        StartKernPairs 3
        KPX A O -50
        KPX O A -40
        KPX A A 10
        EndKernPairs
        EndKernData
        EndFontMetrics
        """));

    // The tokens of the font programs: the keys, the operators of their layout, the signatures of PFA and PFB files
    private static readonly string[] FileTokens =
    [
        "%!PS-AdobeFont-1.0", "%!FontType1", "\uDC80\x01", "\uDC80\x02", "\uDC80\x03",
        "/FontType", "/FontName", "/FontInfo", "/FontMatrix", "/FontBBox", "/Encoding", "StandardEncoding", "/PaintType",
        "/ItalicAngle", "/isFixedPitch", "/UnderlinePosition", "/BlendDesignPositions", "/CIDFontType",
        "/Private", "/Subrs", "/CharStrings", "/lenIV", "/BlueValues", "/OtherSubrs",
        "RD", "-|", "ND", "|-", "NP", "|", "dup", "put", "def", "readonly", "noaccess", "begin", "end", "dict", "array",
        "currentfile", "eexec", "closefile", "cleartomark", "true", "false", "16#FF", "(", ")", "[", "]", "{", "}", "<<", ">>",
    ];

    // The operators of Type 1 charstrings
    private static readonly string[] CharstringTokens =
    [
        "\x01", "\x03", "\x04", "\x05", "\x06", "\x07", "\x08", "\x09", "\x0a", "\x0b", "\x0d", "\x0e", "\x15", "\x16", "\x1e", "\x1f",
        "\x0c\x00", "\x0c\x01", "\x0c\x02", "\x0c\x06", "\x0c\x07", "\x0c\x0c", "\x0c\x10", "\x0c\x11", "\x0c\x21",
    ];

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("fonts-type1", LoadFile) { Seeds = GetFileSeeds, Dictionary = FileTokens },
        new("fonts-type1-charstrings", LoadCharstrings) { Seeds = GetCharstringSeeds, Dictionary = CharstringTokens },
    ];

    /// <summary>Loads a PFA or PFB file: the first byte says whether the metrics of an AFM file are given.</summary>
    private static void LoadFile(ReadOnlySpan<byte> data)
    {
        var withMetrics = (FuzzInput.ReadByte(ref data) & 1) != 0;
        if (!Type1Font.HasSignature(data))
            return;

        FontFace face;
        try
        {
            face = withMetrics ? FontLoader.LoadType1(data, Metrics.Value, SharedEnvironment) : FontLoader.Load(data, SharedEnvironment)[0];
        }
        catch (FontException)
        {
            return;
        }

        CheckFace(face);
    }

    /// <summary>
    /// Loads the sample font with a charstring and a subroutine from the input: an option byte (bit 0: a PFB file,
    /// bit 1: charstrings that are not encrypted), the length of the subroutine 5 and its bytes, then the charstring of
    /// the glyph A, which the accented characters use.
    /// </summary>
    private static void LoadCharstrings(ReadOnlySpan<byte> data)
    {
        var options = FuzzInput.ReadByte(ref data);
        var subroutineLength = Math.Min(FuzzInput.ReadByte(ref data), data.Length);
        var writer = FontType1Writer.Sample(standardEncoding: (options & 4) != 0);
        writer.Subroutines[5] = data[..subroutineLength].ToArray();
        writer.CharStrings[2] = ("A", data[subroutineLength..].ToArray());
        if ((options & 2) != 0)
        {
            writer.LenIV = -1;
        }

        FontFace face;
        try
        {
            face = FontLoader.LoadType1((options & 1) != 0 ? writer.WritePfb() : writer.WritePfa(), metrics: null, SharedEnvironment);
        }
        catch (FontException)
        {
            return;
        }

        CheckFace(face);
    }

    private static void CheckFace(FontFace face)
    {
        FuzzAssert.Equal(FontOutlineKind.Cff, face.Outlines, "A Type 1 font is converted to CFF outlines.");
        var reloaded = FontLoader.Load(face.Data.Bytes.Span, SharedEnvironment);
        FuzzAssert.Equal(1, reloaded.Length, "The converted font has one face.");
        FuzzAssert.Equal(face.GlyphCount, reloaded[0].GlyphCount, "The converted font loads again with the same glyphs.");

        var allocator = SharedEnvironment.CreateAllocator("fuzz");
        using var scratch = new GlyphScratch(allocator);
        for (var glyph = 0; glyph < Math.Min(face.GlyphCount, MaxCheckedGlyphs); glyph++)
        {
            FuzzAssert.True(face.GetAdvanceWidth(glyph) >= 0, "An advance is not negative.");
            FuzzAssert.Equal(face.GetAdvanceWidth(glyph), reloaded[0].GetAdvanceWidth(glyph), "The converted font loads again with the same advances.");
            var outline = Decode(face, glyph, scratch);
            if (outline is not null)
            {
                FuzzAssert.Equal(outline, Decode(reloaded[0], glyph, scratch), "The converted font loads again with the same outlines.");
            }
        }
    }

    /// <summary>Decodes an outline; <see langword="null"/> when it exceeds the budget, which is the only error a converted charstring can have.</summary>
    private static string? Decode(FontFace face, int glyph, GlyphScratch scratch)
    {
        var sink = new RecordingSink();
        var budget = new GlyphBudget(face.Data.Limits);
        try
        {
            face.DecodeOutline(glyph, sink, scratch, ref budget);
        }
        catch (FontResourceLimitException)
        {
            return null;
        }
        catch (InvalidFontException exception)
        {
            FuzzAssert.Fail($"The glyph {glyph} of a converted Type 1 font does not decode: {exception.Message}");
        }

        return sink.ToString();
    }

    private static IEnumerable<FuzzSeed> GetFileSeeds()
    {
        var standard = FontType1Writer.Sample(standardEncoding: true);
        standard.LenIV = -1;
        var light = FontType1Writer.Sample();
        light.Weight = "Light";
        light.FontMatrix = "[0.0005 0 0.0001 0.0005 0 0]";
        (string Name, byte[] Data)[] fonts =
        [
            ("sample.pfa", FontType1Writer.Sample().WritePfa()),
            ("sample.pfb", FontType1Writer.Sample().WritePfb()),
            ("standard.pfb", standard.WritePfb()),
            ("light.pfa", light.WritePfa()),
            ("literal-encoding.pfa", Encoding.Latin1.GetBytes(Encoding.Latin1.GetString(FontType1Writer.Sample(standardEncoding: true).WritePfa()).Replace("/Encoding StandardEncoding def\n", "/Encoding [/.notdef /A /O /Aacute] dup 3 /H put readonly def\n", StringComparison.Ordinal))),
        ];
        foreach (var (name, data) in fonts)
        {
            yield return new FuzzSeed(name, (byte[])[0, .. data]);
            yield return new FuzzSeed("afm-" + name, (byte[])[1, .. data]);
        }
    }

    private static IEnumerable<FuzzSeed> GetCharstringSeeds()
    {
        var sample = FontType1Writer.Sample();
        var subroutine = sample.Subroutines[5];
        for (var i = 1; i < sample.CharStrings.Count; i++)
        {
            var (name, charstring) = sample.CharStrings[i];
            yield return new FuzzSeed(name, (byte[])[(byte)(i % 8), (byte)subroutine.Length, .. subroutine, .. charstring]);
        }
    }

    private sealed class RecordingSink : OutlineSink
    {
        private readonly StringBuilder _path = new();

        public override void MoveTo(double x, double y) => Append('M', x, y);

        public override void LineTo(double x, double y) => Append('L', x, y);

        public override void QuadraticTo(double controlX, double controlY, double x, double y) => Append('Q', controlX, controlY, x, y);

        public override void CubicTo(double control1X, double control1Y, double control2X, double control2Y, double x, double y) => Append('C', control1X, control1Y, control2X, control2Y, x, y);

        public override void Close() => _path.Append('Z');

        public override string ToString() => _path.ToString();

        private void Append(char command, params ReadOnlySpan<double> coordinates)
        {
            _path.Append(command);
            foreach (var coordinate in coordinates)
            {
                FuzzAssert.True(double.IsFinite(coordinate), "An outline has finite coordinates.");
                _path.Append(coordinate.ToString("R", CultureInfo.InvariantCulture)).Append(' ');
            }
        }
    }
}
