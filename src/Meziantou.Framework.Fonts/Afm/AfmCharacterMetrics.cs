namespace Meziantou.Framework.Fonts;

/// <summary>The metrics of one character of an AFM file: a line of its <c>StartCharMetrics</c> section (Adobe Technical Note #5004, section 8).</summary>
public sealed class AfmCharacterMetrics
{
    internal AfmCharacterMetrics(int code, string? name, AfmVector? width, AfmVector? verticalWidth, AfmVector? vVector, AfmBox? boundingBox, IReadOnlyList<AfmLigature> ligatures)
    {
        Code = code;
        Name = name;
        Width = width;
        VerticalWidth = verticalWidth;
        VVector = vVector;
        BoundingBox = boundingBox;
        Ligatures = ligatures;
    }

    /// <summary>Gets the code of the character in the default encoding of the font (<c>C</c> or <c>CH</c>), or −1 when the character is not encoded. In a CID-keyed font, it is the CID.</summary>
    public int Code { get; }

    /// <summary>Gets the PostScript name of the character (<c>N</c>).</summary>
    public string? Name { get; }

    /// <summary>Gets the width vector of the character for writing direction 0 (<c>WX</c>, <c>WY</c>, <c>W</c> and their <c>W0</c> forms).</summary>
    /// <remarks>When the file gives no width for the character, the <see cref="AfmWritingDirection.CharWidth"/> of the direction applies: see <see cref="AfmFontMetrics.GetWidth(AfmCharacterMetrics)"/>.</remarks>
    public AfmVector? Width { get; }

    /// <summary>Gets the width vector of the character for writing direction 1 (<c>W1X</c>, <c>W1Y</c> and <c>W1</c>).</summary>
    public AfmVector? VerticalWidth { get; }

    /// <summary>Gets the vector from the origin of writing direction 0 to the origin of writing direction 1 (<c>VV</c>).</summary>
    public AfmVector? VVector { get; }

    /// <summary>Gets the bounding box of the character (<c>B</c>): all zeros for a character that makes no marks.</summary>
    public AfmBox? BoundingBox { get; }

    /// <summary>Gets the ligatures that start with the character (<c>L</c>), in the order of the file.</summary>
    public IReadOnlyList<AfmLigature> Ligatures { get; }
}
