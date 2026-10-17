using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The position of a glyph, in font units: how far the pen moves after it, and where it is drawn from the pen.</summary>
[StructLayout(LayoutKind.Auto)]
internal struct GlyphPosition
{
    /// <summary>The glyph is not attached.</summary>
    public const byte NoAttachment = 0;

    /// <summary>The glyph is a mark attached to an anchor of another glyph.</summary>
    public const byte MarkAttachment = 1;

    /// <summary>The glyph is connected to another glyph by a cursive attachment.</summary>
    public const byte CursiveAttachment = 2;

    public int XAdvance;
    public int YAdvance;
    public int XOffset;
    public int YOffset;

    /// <summary>The glyph this one is attached to, as a distance in the buffer; 0 when it is not attached.</summary>
    public int AttachmentChain;

    public byte AttachmentType;
}
