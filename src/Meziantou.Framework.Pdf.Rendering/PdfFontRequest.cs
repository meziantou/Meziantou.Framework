namespace Meziantou.Framework.Pdf.Rendering;

/// <summary>A font the document does not embed, which <see cref="PdfRenderOptions.FontResolver"/> replaces: its name and the style its descriptor gives.</summary>
public sealed class PdfFontRequest
{
    internal PdfFontRequest(string name, string? standardName, bool isBold, bool isItalic, bool isFixedPitch, bool isSerif, bool isSymbolic)
    {
        Name = name;
        StandardName = standardName;
        IsBold = isBold;
        IsItalic = isItalic;
        IsFixedPitch = isFixedPitch;
        IsSerif = isSerif;
        IsSymbolic = isSymbolic;
    }

    /// <summary>Gets the PostScript name of the font (<c>/BaseFont</c>), without the tag of a subset.</summary>
    public string Name { get; }

    /// <summary>Gets the name of the standard font the font is or is an alias of (<c>Helvetica-Bold</c> for <c>Arial,Bold</c>), or <see langword="null"/>.</summary>
    public string? StandardName { get; }

    /// <summary>Gets whether the font is bold.</summary>
    public bool IsBold { get; }

    /// <summary>Gets whether the font is italic.</summary>
    public bool IsItalic { get; }

    /// <summary>Gets whether the glyphs of the font have the same width.</summary>
    public bool IsFixedPitch { get; }

    /// <summary>Gets whether the glyphs of the font have serifs.</summary>
    public bool IsSerif { get; }

    /// <summary>Gets whether the font has other glyphs than the standard Latin characters (Symbol, ZapfDingbats).</summary>
    public bool IsSymbolic { get; }
}
