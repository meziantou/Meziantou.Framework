namespace Meziantou.Framework.Fonts;

/// <summary>The metrics of an AFM file for one writing direction (Adobe Technical Note #5004, section 7.2).</summary>
public sealed class AfmWritingDirection
{
    internal AfmWritingDirection(double? underlinePosition, double? underlineThickness, double? italicAngle, AfmVector? charWidth, bool? isFixedPitch)
    {
        UnderlinePosition = underlinePosition;
        UnderlineThickness = underlineThickness;
        ItalicAngle = italicAngle;
        CharWidth = charWidth;

        // "If absent, its value is assumed to be true if CharWidth is present and false if CharWidth is absent."
        IsFixedPitch = isFixedPitch ?? charWidth is not null;
    }

    /// <summary>Gets the distance from the baseline to the center of the underline: a y displacement for writing direction 0, an x displacement for writing direction 1.</summary>
    public double? UnderlinePosition { get; }

    /// <summary>Gets the stroke width of the underline.</summary>
    public double? UnderlineThickness { get; }

    /// <summary>Gets the angle, in degrees counter-clockwise from the vertical, of the dominant vertical strokes: 0 for a font that is not italic.</summary>
    public double? ItalicAngle { get; }

    /// <summary>Gets the width vector of every character, when all the characters have the same one in this direction.</summary>
    public AfmVector? CharWidth { get; }

    /// <summary>Gets a value indicating whether the font is monospaced in this direction.</summary>
    public bool IsFixedPitch { get; }
}
