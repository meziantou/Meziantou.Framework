namespace Meziantou.Framework.Fonts;

/// <summary>
/// How a glyph of mathematical layout grows along a direction: its larger forms, from the smallest, and the parts of
/// the assembly that makes it as large as needed.
/// </summary>
internal sealed class MathGlyphConstruction
{
    public MathGlyphConstruction(MathGlyphVariant[] variants, MathGlyphPart[] parts, int assemblyItalicsCorrection)
    {
        Variants = variants;
        AssemblyParts = parts;
        AssemblyItalicsCorrection = assemblyItalicsCorrection;
    }

    /// <summary>Gets the larger forms of the glyph, from the smallest; the glyph itself is usually the first one.</summary>
    public IReadOnlyList<MathGlyphVariant> Variants { get; }

    /// <summary>Gets the parts of the assembly, from the bottom or the left; empty when the glyph has no assembly.</summary>
    public IReadOnlyList<MathGlyphPart> AssemblyParts { get; }

    /// <summary>Gets the italics correction of the assembly, in font units.</summary>
    public int AssemblyItalicsCorrection { get; }
}
