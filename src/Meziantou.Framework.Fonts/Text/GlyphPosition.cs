using System.Runtime.InteropServices;

namespace Meziantou.Framework.Fonts;

/// <summary>The position of a glyph, in font units: how far the pen moves after it, and where it is drawn from the pen.</summary>
[StructLayout(LayoutKind.Auto)]
public struct GlyphPosition
{
    /// <summary>The glyph is not attached.</summary>
    public const byte NoAttachment = 0;

    /// <summary>The glyph is a mark attached to an anchor of another glyph.</summary>
    public const byte MarkAttachment = 1;

    /// <summary>The glyph is connected to another glyph by a cursive attachment.</summary>
    public const byte CursiveAttachment = 2;

    /// <summary>How far the pen moves horizontally after the glyph.</summary>
    public int XAdvance;

    /// <summary>How far the pen moves vertically after the glyph.</summary>
    public int YAdvance;

    /// <summary>How far right of the pen the glyph is drawn.</summary>
    public int XOffset;

    /// <summary>How far above the pen the glyph is drawn.</summary>
    public int YOffset;

    /// <summary>The glyph this one is attached to, as a distance in the buffer; 0 when it is not attached.</summary>
    public int AttachmentChain;

    /// <summary>How the glyph is attached: <see cref="NoAttachment"/>, <see cref="MarkAttachment"/> or <see cref="CursiveAttachment"/>.</summary>
    public byte AttachmentType;
}
