extern alias UnicodeLibrary;

using Meziantou.Framework.Fonts;
using Meziantou.Framework.WebPlatform.Unicode;
using SharedFontFace = Meziantou.Framework.Fonts.FontFace;
using SharedScript = UnicodeLibrary::Meziantou.Framework.UnicodeScript;
using UnicodeScript = Meziantou.Framework.WebPlatform.Unicode.UnicodeScript;

namespace Meziantou.Framework.WebPlatform.Css.Fonts;

/// <summary>
/// What the shaping of the runs of a text with the shaper of Meziantou.Framework.Fonts needs, kept from a text to the
/// next one by the <see cref="TextShapeCache"/> of a font list: the glyphs, the scratch memory of the outlines, the plans
/// of the fonts and the language systems of the languages. It is used by one shaping at a time.
/// </summary>
internal sealed class ShapingWorkspace : IDisposable
{
    /// <summary>The largest run whose glyph buffer is kept for the next texts.</summary>
    internal const int MaxKeptLength = 4096;

    /// <summary>The number of languages whose language systems are kept.</summary>
    private const int MaxLanguages = 64;

    // A run is as long as the text of the document that has it: the length of the text is not limited here.
    private static readonly FontResourceLimits Limits = new() { MaxFontBytes = FontFile.Environment.Limits.MaxFontBytes, MaxTextGlyphs = Array.MaxLength };

    private static readonly SharedScript[] Scripts = CreateScripts();

    private readonly Dictionary<(string? Language, uint Override), uint[]> _languages = [];

    public GlyphBuffer Buffer { get; private set; } = new(Limits);

    public GlyphScratch Scratch { get; } = new(FontFile.Environment.CreateAllocator(nameof(TextShaper)));

    public ShapingCache Cache { get; } = new();

    /// <summary>The features of the run being shaped.</summary>
    public List<ShapingFeature> Features { get; } = [];

    /// <summary>The number of glyphs of the longest run shaped with the glyph buffer.</summary>
    public int LargestRun { get; set; }

    /// <summary>
    /// Replaces the glyph buffer when a long run made it grow beyond what is kept for the next texts; the plans of the fonts
    /// are kept.
    /// </summary>
    public void TrimBuffer()
    {
        if (LargestRun > MaxKeptLength)
        {
            Buffer = new GlyphBuffer(Limits);
            LargestRun = 0;
        }
    }

    /// <summary>The script of the shaper for a script of the text, by its ISO 15924 code.</summary>
    public static SharedScript GetScript(UnicodeScript script) => (uint)script < (uint)Scripts.Length ? Scripts[(int)script] : SharedScript.Unknown;

    /// <summary>
    /// The tags of the language systems of a run, by order of preference: the one of <c>font-language-override</c>, then
    /// the ones of the content language (https://drafts.csswg.org/css-fonts-4/#font-language-override-prop).
    /// </summary>
    public uint[] GetLanguages(string? language, uint languageOverride)
    {
        if (_languages.TryGetValue((language, languageOverride), out var tags))
            return tags;

        tags = language is null ? [] : OpenTypeLanguage.GetTags(language);
        if (languageOverride != 0)
        {
            tags = [languageOverride, .. tags];
        }

        if (_languages.Count >= MaxLanguages)
        {
            _languages.Clear();
        }

        _languages.Add((language, languageOverride), tags);
        return tags;
    }

    /// <summary>
    /// Gives the characters of the buffer the glyphs of the character map and the advances of the font, without the
    /// layout tables: the shaping of a font whose layout tables cannot be used. The glyphs of an upright run are placed by the
    /// painting, as the ones of the shaper.
    /// </summary>
    public void ShapeWithoutLayout(SharedFontFace face, bool isVertical)
    {
        var buffer = Buffer;
        buffer.CreatePositions();
        var info = buffer.Info;
        var positions = buffer.Positions;
        for (var i = 0; i < info.Length; i++)
        {
            var glyph = info[i].Glyph = face.MapCodePoint(info[i].CodePoint);
            if (isVertical)
            {
                positions[i].YAdvance = -face.GetAdvanceHeight(glyph);
            }
            else
            {
                positions[i].XAdvance = face.GetAdvanceWidth(glyph);
            }
        }
    }

    public void Dispose() => Scratch.Dispose();

    private static SharedScript[] CreateScripts()
    {
        var codes = ScriptCodes.Codes;
        var scripts = new SharedScript[codes.Length];
        for (var i = 0; i < codes.Length; i++)
        {
            scripts[i] = OpenTypeScript.FromIso15924(codes[i]);
        }

        return scripts;
    }
}
