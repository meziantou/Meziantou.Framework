using System.Globalization;
using System.Text;
using Meziantou.Framework.Fonts;
using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Fonts;

/// <summary>
/// A TrueType or OpenType font embedded in the documents that use it: a composite font (<c>Type0</c>, ISO 32000-2,
/// 9.7) whose codes are the glyph identifiers (<c>Identity-H</c>), with the subset of the glyphs each document uses.
/// </summary>
/// <remarks>
/// The text is shaped (<see cref="TextShaper"/>) in runs of one script and one direction, ordered by the Unicode
/// Bidirectional Algorithm: the glyphs are shown from left to right, with the positions the shaping gives them.
/// </remarks>
internal sealed class EmbeddedPdfFont : PdfFont
{
    // An adjustment smaller than this, in thousandths of the font size, is not written
    private const double Epsilon = 0.0005;

    // The longest text a glyph maps to in the ToUnicode CMap: a longer one is a replacement text
    private const int MaxMappedTextLength = 32;

    private readonly double _scale;

    public EmbeddedPdfFont(FontFace face)
    {
        if (face.Outlines is not (FontOutlineKind.TrueType or FontOutlineKind.Cff))
            throw new UnsupportedPdfFeatureException($"The outlines of the font '{face.FamilyName}' cannot be embedded: only TrueType and CFF outlines can");

        if (face.TryGetTable(FontTags.Os2, out var os2) && os2.Length >= 10)
        {
            // The embedding permissions (OpenType specification, "OS/2", fsType): restricted license, or bitmaps only
            var type = (os2.Span[8] << 8) | os2.Span[9];
            if ((type & 0x000F) == 0x0002 || (type & 0x0200) != 0)
                throw new UnsupportedPdfFeatureException($"The license of the font '{face.FamilyName}' does not allow embedding its outlines");
        }

        Face = face;
        _scale = 1000d / face.UnitsPerEm;
        PostScriptName = CreatePostScriptName(face);
        var ascent = face.UseTypoMetrics ? face.TypoAscender : face.HorizontalAscender;
        var descent = face.UseTypoMetrics ? face.TypoDescender : face.HorizontalDescender;
        Ascent = ascent * _scale;
        Descent = Math.Min(0, descent * _scale);
        CapHeight = (face.CapHeight > 0 ? face.CapHeight : ascent) * _scale;
    }

    public FontFace Face { get; }

    public override string PostScriptName { get; }

    public override double Ascent { get; }

    public override double Descent { get; }

    public override double CapHeight { get; }

    public override bool CanEncode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        foreach (var rune in text.EnumerateRunes())
        {
            if (Face.MapCodePoint(rune.Value) == 0 && !IsInvisible(rune))
                return false;
        }

        return true;
    }

    /// <summary>Gets the width of a glyph in the <c>W</c> array of the font, in glyph space units.</summary>
    public double GetWidth(int glyph) => Math.Round(Face.GetAdvanceWidth(glyph) * _scale, 3, MidpointRounding.AwayFromZero);

    /// <summary>Gets the font size units of a value of the font.</summary>
    public double Scale(int value) => value * _scale;

    /// <summary>Encodes the glyphs of the characters, without shaping: the codes are the glyph identifiers on two bytes.</summary>
    internal override byte[] Encode(string text)
    {
        var result = new List<byte>(text.Length * 2);
        foreach (var rune in text.EnumerateRunes())
        {
            var glyph = Face.MapCodePoint(rune.Value);
            if (glyph == 0)
            {
                if (IsInvisible(rune))
                    continue;

                throw CreateMissingGlyphException(rune, nameof(text));
            }

            result.Add((byte)(glyph >> 8));
            result.Add((byte)glyph);
        }

        return [.. result];
    }

    internal override PdfReference GetReference(PdfDocument document) => GetUsage(document).Reference;

    /// <summary>Gets what a document uses of the font, creating the font dictionary on first use.</summary>
    internal EmbeddedFontUsage GetUsage(PdfDocument document)
    {
        if (!document.EmbeddedFonts.TryGetValue(this, out var usage))
        {
            usage = new EmbeddedFontUsage(this, document.Objects.Add(new PdfDictionary()));
            document.EmbeddedFonts.Add(this, usage);
        }

        return usage;
    }

    /// <summary>Shapes a text: the glyphs from left to right, with the adjustments of their positions.</summary>
    /// <exception cref="ArgumentException">A character of the text has no glyph in the font.</exception>
    internal ShapedText Shape(string text)
    {
        var glyphs = new List<ShapedGlyph>(text.Length);
        var mappings = new List<(int Glyph, string Text)>(text.Length);
        if (text.Length == 0)
            return new ShapedText([], 0, [], NeedsActualText: false);

        var levels = new byte[text.Length];
        UnicodeBidi.ResolveLevels(text, UnicodeBidiDirection.Auto, levels);
        var runs = new List<(int Start, int Length, UnicodeScript Script)>();
        var runLevels = new List<byte>();
        foreach (var scriptRun in Unicode.EnumerateScriptRuns(text))
        {
            var end = scriptRun.Index + scriptRun.Length;
            var start = scriptRun.Index;
            while (start < end)
            {
                var next = start + 1;
                while (next < end && levels[next] == levels[start])
                {
                    next++;
                }

                runs.Add((start, next - start, scriptRun.Script));
                runLevels.Add(levels[start]);
                start = next;
            }
        }

        var order = new int[runs.Count];
        UnicodeBidi.GetVisualOrder(runLevels.ToArray(), order);
        var needsActualText = false;
        var buffer = new GlyphBuffer(Face.Data.Environment.Limits);
        var pen = 0d;
        var previousEnd = 0d;
        foreach (var index in order)
        {
            var (start, length, script) = runs[index];
            var isRightToLeft = (runLevels[index] & 1) != 0;
            needsActualText |= isRightToLeft;
            buffer.Clear();
            TextShaper.AddText(buffer, text.AsSpan(start, length), start);
            TextShaper.Shape(buffer, Face, script, [], isRightToLeft, []);

            var info = buffer.Info;
            var positions = buffer.Positions;
            var clusters = new SortedDictionary<int, int>();
            foreach (var item in info)
            {
                clusters[item.Cluster] = clusters.GetValueOrDefault(item.Cluster) + 1;
            }

            var clusterStarts = clusters.Keys.ToArray();
            for (var i = 0; i < info.Length; i++)
            {
                var glyph = info[i].Glyph;
                var cluster = info[i].Cluster;
                var position = positions[i];
                if (glyph == 0)
                {
                    var rune = Rune.GetRuneAt(text, cluster);
                    if (!IsInvisible(rune))
                        throw CreateMissingGlyphException(rune, nameof(text));

                    // A character without glyph that is not shown: only its advance is kept
                    pen += position.XAdvance * _scale;
                    continue;
                }

                var clusterIndex = Array.BinarySearch(clusterStarts, cluster);
                var clusterEnd = clusterIndex + 1 < clusterStarts.Length ? clusterStarts[clusterIndex + 1] : start + length;
                if (clusters[cluster] == 1 && clusterEnd - cluster <= MaxMappedTextLength)
                {
                    mappings.Add((glyph, text[cluster..clusterEnd]));
                }
                else
                {
                    needsActualText = true;
                }

                // The position of the glyph from the position after the previous glyph: TJ moves the next glyph by
                // minus the adjustment (9.4.3)
                var target = pen + (position.XOffset * _scale);
                glyphs.Add(new ShapedGlyph(glyph, Round(previousEnd - target), Round(position.YOffset * _scale)));
                previousEnd = target + GetWidth(glyph);
                pen += position.XAdvance * _scale;
            }
        }

        return new ShapedText([.. glyphs], Round(previousEnd - pen), [.. mappings], needsActualText);
    }

    private protected override double GetTextWidthInGlyphUnits(string text)
    {
        var shaped = Shape(text);
        var width = -shaped.TrailingAdjustment;
        foreach (var glyph in shaped.Glyphs)
        {
            width += GetWidth(glyph.Glyph) - glyph.Adjustment;
        }

        return width;
    }

    private static double Round(double value)
    {
        var rounded = Math.Round(value, 3, MidpointRounding.AwayFromZero);
        return Math.Abs(rounded) < Epsilon ? 0 : rounded;
    }

    /// <summary>Indicates whether a character is not shown, so that a font without its glyph can still show the text: controls and formatting characters.</summary>
    private static bool IsInvisible(Rune rune) => Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control or UnicodeCategory.Format;

    private ArgumentException CreateMissingGlyphException(Rune rune, string parameterName)
    {
        return new ArgumentException($"The font '{PostScriptName}' has no glyph for the character U+{rune.Value:X4}", parameterName);
    }

    /// <summary>Gets the PostScript name of a face, or a name made of the letters and digits of its family and style.</summary>
    private static string CreatePostScriptName(FontFace face)
    {
        if (!string.IsNullOrEmpty(face.PostScriptName))
            return Sanitize(face.PostScriptName);

        var name = Sanitize(face.FamilyName);
        var style = Sanitize(face.SubfamilyName);
        if (name.Length == 0)
            return "Font";

        return style.Length == 0 ? name : name + "-" + style;
    }

    private static string Sanitize(string value)
    {
        var builder = new StringBuilder(Math.Min(value.Length, 63));
        foreach (var c in value)
        {
            if (builder.Length < 63 && c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_' or '.')
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }
}
