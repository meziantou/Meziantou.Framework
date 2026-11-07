namespace Meziantou.Framework.Imaging.Typography;

/// <summary>An axis a variable font varies along: its tag, its name and the range of its values.</summary>
/// <remarks>
/// The registered axes are the weight (<c>wght</c>, where 400 is regular and 700 bold), the width (<c>wdth</c>, a
/// percentage of the normal width), the slant (<c>slnt</c>, in degrees), the italic (<c>ital</c>, 0 or 1) and the
/// optical size (<c>opsz</c>, in points). A font can define others, with a tag in uppercase letters.
/// </remarks>
public sealed class FontVariationAxis
{
    internal FontVariationAxis(string tag, string name, double minimum, double @default, double maximum, bool isHidden)
    {
        Tag = tag;
        Name = name;
        Minimum = minimum;
        Default = @default;
        Maximum = maximum;
        IsHidden = isHidden;
    }

    /// <summary>Gets the tag of the axis: four characters, such as <c>wght</c>.</summary>
    public string Tag { get; }

    /// <summary>Gets the name the font gives to the axis, such as "Weight"; its tag when the font gives none.</summary>
    public string Name { get; }

    /// <summary>Gets the smallest value of the axis.</summary>
    public double Minimum { get; }

    /// <summary>Gets the value of the axis in the default instance of the font.</summary>
    public double Default { get; }

    /// <summary>Gets the largest value of the axis.</summary>
    public double Maximum { get; }

    /// <summary>Gets a value indicating whether the font asks that the axis is not shown in a user interface.</summary>
    public bool IsHidden { get; }

    /// <inheritdoc />
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Tag} ({Name}): {Minimum} to {Maximum}, {Default} by default");
}
