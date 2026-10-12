using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>A glyph of a text being shaped: its identifier, the characters it comes from, and what shaping knows about it.</summary>
[StructLayout(LayoutKind.Auto)]
internal struct GlyphInfo
{
    // The class of the glyph, with the values of the GDEF glyph class definition as bits. They are the bits a lookup
    // flag uses to ignore a class of glyphs.
    public const int BaseGlyph = 0x02;
    public const int LigatureGlyph = 0x04;
    public const int MarkGlyph = 0x08;
    public const int ClassMask = BaseGlyph | LigatureGlyph | MarkGlyph;

    /// <summary>The glyph was put in place of others by a substitution of several glyphs by one.</summary>
    public const int Ligated = 0x10;

    /// <summary>The glyph is one of several glyphs a substitution put in place of one.</summary>
    public const int Multiplied = 0x20;

    /// <summary>The character is default ignorable: it has no visible form of its own.</summary>
    public const int DefaultIgnorable = 0x01;

    /// <summary>The character is the zero width non-joiner.</summary>
    public const int ZeroWidthNonJoiner = 0x02;

    /// <summary>The character is the zero width joiner.</summary>
    public const int ZeroWidthJoiner = 0x04;

    /// <summary>The glyph was produced by a substitution: it no longer stands for an ignorable character.</summary>
    public const int Substituted = 0x08;

    /// <summary>
    /// The character has no visible form and still separates the glyphs around it for the substitutions: a combining
    /// grapheme joiner that keeps two marks in the order of the text.
    /// </summary>
    public const int Hidden = 0x10;

    /// <summary>The identifier of the glyph; before the characters are mapped, their code point.</summary>
    public int Glyph;

    /// <summary>The index, in the text of the run, of the first character of the cluster the glyph belongs to.</summary>
    public int Cluster;

    /// <summary>The character the glyph comes from; for a glyph made of several characters, the first one.</summary>
    public int CodePoint;

    /// <summary>The features that apply to the glyph: a lookup applies when its mask has a bit of this one.</summary>
    public uint Mask;

    /// <summary>The class of the glyph and how substitutions produced it.</summary>
    public byte Props;

    /// <summary>The mark attachment class of the glyph, from the GDEF table.</summary>
    public byte MarkAttachmentClass;

    /// <summary>The general category of the character.</summary>
    public UnicodeCategory Category;

    /// <summary>The canonical combining class of the character.</summary>
    public byte CombiningClass;

    /// <summary>What the character is for shaping: ignorable, joiner.</summary>
    public byte Flags;

    /// <summary>
    /// The ligature the glyph is or belongs to; 0 for none. A mark with the identifier of a ligature is attached to one
    /// of its components.
    /// </summary>
    public byte LigatureId;

    /// <summary>
    /// For a ligature, the number of its components; for a mark that belongs to a ligature, the component it follows,
    /// from 1; for a glyph of a multiplied sequence, its rank in the sequence, from 1.
    /// </summary>
    public byte LigatureComponent;

    /// <summary>A value of the shaper of the script: for a space the font does not have, the width it stands for.</summary>
    public byte ShaperData;

    public readonly bool IsMark => (Props & MarkGlyph) != 0;

    public readonly bool IsBase => (Props & BaseGlyph) != 0;

    public readonly bool IsLigature => (Props & LigatureGlyph) != 0;

    /// <summary>Gets a value indicating whether the glyph still stands for a default ignorable character.</summary>
    public readonly bool IsIgnorable => (Flags & (DefaultIgnorable | Substituted)) == DefaultIgnorable;
}
