using Meziantou.Framework.Imaging.Typography;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The hinted glyphs of a layout that is being created (<see cref="TextLayoutOptions.Hinting"/>): a
/// <see cref="GlyphHinter"/> per font and size, and the outline and the advance of each distinct glyph, decoded once.
/// </summary>
/// <remarks>
/// The engine reads the advances while it shapes (<see cref="TextHinting.Full"/> makes them whole pixels), and gives
/// the outlines to the layout (<see cref="HintedOutlines"/>), which records them instead of decoding the glyphs. The
/// hinters rent their storage from the allocators of the fonts: the instance is disposed when the layout is created.
/// </remarks>
internal sealed class TextGlyphHinter(TextHinting hinting) : IDisposable
{
    private readonly Dictionary<(Font Font, double Size), GlyphHinter> _hinters = [];
    private readonly Dictionary<Font, GlyphScratch> _scratches = [];
    private readonly Dictionary<(Font Font, int Glyph, double Size), (double Advance, int Outline)> _glyphs = [];
    private readonly RecordedOutlines.Recorder _recorder = new();

    /// <summary>
    /// Gets the advance a glyph has once hinted, in pixels, when it is hinted with the instructions of its font, which
    /// make it a whole number of pixels.
    /// </summary>
    /// <returns><see langword="false"/> when the advances of the font are not hinted (<see cref="TextHinting.Light"/>, or a font without instructions).</returns>
    public bool TryGetAdvance(Font font, double size, int glyph, out double advance)
    {
        var hinter = GetHinter(font, size);
        if (hinter.Hinting != GlyphHinting.Full)
        {
            advance = 0;
            return false;
        }

        advance = GetGlyph(font, size, glyph).Advance;
        return true;
    }

    /// <summary>Hints the outline of a glyph, once.</summary>
    public void Add(Font font, double size, int glyph) => GetGlyph(font, size, glyph);

    /// <summary>Gets the outlines hinted so far.</summary>
    public HintedOutlines Build()
    {
        var indexes = new Dictionary<(Font Font, int Glyph, double Size), int>(_glyphs.Count);
        foreach (var (key, value) in _glyphs)
        {
            indexes.Add(key, value.Outline);
        }

        return new HintedOutlines(_recorder.Build(), indexes);
    }

    public void Dispose()
    {
        foreach (var hinter in _hinters.Values)
        {
            hinter.Dispose();
        }

        foreach (var scratch in _scratches.Values)
        {
            scratch.Dispose();
        }

        _hinters.Clear();
        _scratches.Clear();
    }

    private (double Advance, int Outline) GetGlyph(Font font, double size, int glyph)
    {
        if (!_glyphs.TryGetValue((font, glyph, size), out var value))
        {
            var hinter = GetHinter(font, size);
            var budget = new GlyphBudget(font.Face.Data.Limits);
            _recorder.Begin();
            var advance = hinter.DecodeOutline(glyph, _recorder, GetScratch(font), ref budget);
            value = (advance, _recorder.End());
            _glyphs.Add((font, glyph, size), value);
        }

        return value;
    }

    private GlyphHinter GetHinter(Font font, double size)
    {
        if (!_hinters.TryGetValue((font, size), out var hinter))
        {
            var mode = hinting == TextHinting.Full ? GlyphHinting.Full : GlyphHinting.Light;
            hinter = GlyphHinter.Create(font.Face, size, mode, GetScratch(font), font.Face.Data.Environment.CreateAllocator("TextLayout"));
            _hinters.Add((font, size), hinter);
        }

        return hinter;
    }

    private GlyphScratch GetScratch(Font font)
    {
        if (!_scratches.TryGetValue(font, out var scratch))
        {
            scratch = new GlyphScratch(font.Face.Data.Environment.CreateAllocator("TextLayout"));
            _scratches.Add(font, scratch);
        }

        return scratch;
    }
}
