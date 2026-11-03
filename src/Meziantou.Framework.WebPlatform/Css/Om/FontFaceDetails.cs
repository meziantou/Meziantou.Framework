using System.Collections;
using Meziantou.Framework.WebPlatform.Css.Fonts;

namespace Meziantou.Framework.WebPlatform.Css.Om;

/// <summary>The features of a font face (https://drafts.csswg.org/css-font-loading/#fontfacefeatures): it has no member yet.</summary>
/// <seealso href="https://drafts.csswg.org/css-font-loading/#fontfacefeatures"/>
[IdlInterface("FontFaceFeatures", "https://drafts.csswg.org/css-font-loading/#fontfacefeatures")]
public sealed class FontFaceFeatures
{
    internal FontFaceFeatures()
    {
    }
}

/// <summary>A variation axis of a font face (https://drafts.csswg.org/css-font-loading/#fontfacevariationaxis).</summary>
/// <seealso href="https://drafts.csswg.org/css-font-loading/#fontfacevariationaxis"/>
[IdlInterface("FontFaceVariationAxis", "https://drafts.csswg.org/css-font-loading/#fontfacevariationaxis")]
public sealed class FontFaceVariationAxis
{
    internal FontFaceVariationAxis(string name, string axisTag, double minimumValue, double maximumValue, double defaultValue)
    {
        Name = name;
        AxisTag = axisTag;
        MinimumValue = minimumValue;
        MaximumValue = maximumValue;
        DefaultValue = defaultValue;
    }

    /// <summary>Gets the name of the axis in the font.</summary>
    /// <seealso href="https://drafts.csswg.org/css-font-loading/#dom-fontfacevariationaxis-name"/>
    [IdlMember("name", "https://drafts.csswg.org/css-font-loading/#dom-fontfacevariationaxis-name")]
    public string Name { get; }

    /// <summary>Gets the tag of the axis (e.g. <c>wght</c>).</summary>
    /// <seealso href="https://drafts.csswg.org/css-font-loading/#dom-fontfacevariationaxis-axistag"/>
    [IdlMember("axisTag", "https://drafts.csswg.org/css-font-loading/#dom-fontfacevariationaxis-axistag")]
    public string AxisTag { get; }

    /// <summary>Gets the minimum value of the axis.</summary>
    /// <seealso href="https://drafts.csswg.org/css-font-loading/#dom-fontfacevariationaxis-minimumvalue"/>
    [IdlMember("minimumValue", "https://drafts.csswg.org/css-font-loading/#dom-fontfacevariationaxis-minimumvalue")]
    public double MinimumValue { get; }

    /// <summary>Gets the maximum value of the axis.</summary>
    /// <seealso href="https://drafts.csswg.org/css-font-loading/#dom-fontfacevariationaxis-maximumvalue"/>
    [IdlMember("maximumValue", "https://drafts.csswg.org/css-font-loading/#dom-fontfacevariationaxis-maximumvalue")]
    public double MaximumValue { get; }

    /// <summary>Gets the default value of the axis.</summary>
    /// <seealso href="https://drafts.csswg.org/css-font-loading/#dom-fontfacevariationaxis-defaultvalue"/>
    [IdlMember("defaultValue", "https://drafts.csswg.org/css-font-loading/#dom-fontfacevariationaxis-defaultvalue")]
    public double DefaultValue { get; }
}

/// <summary>
/// The variation axes of a font face (https://drafts.csswg.org/css-font-loading/#fontfacevariations), a
/// <c>readonly setlike&lt;FontFaceVariationAxis&gt;</c>: the axes of the <c>fvar</c> table of the font once it is loaded,
/// empty before.
/// </summary>
/// <seealso href="https://drafts.csswg.org/css-font-loading/#fontfacevariations"/>
[IdlInterface("FontFaceVariations", "https://drafts.csswg.org/css-font-loading/#fontfacevariations")]
public sealed class FontFaceVariations : IReadOnlyCollection<FontFaceVariationAxis>
{
    private readonly List<FontFaceVariationAxis> _axes = [];

    internal FontFaceVariations()
    {
    }

    /// <summary>Gets the number of axes.</summary>
    [NotInIdl("The size member of setlike<FontFaceVariationAxis>")]
    public int Count => _axes.Count;

    /// <summary>Returns whether the set contains the axis.</summary>
    [NotInIdl("The has() member of setlike<FontFaceVariationAxis>")]
    public bool Has(FontFaceVariationAxis value) => _axes.Contains(value);

    /// <summary>Replaces the axes with the ones of a loaded font.</summary>
    internal void SetAxes(FontFile file)
    {
        _axes.Clear();
        if (file.Faces is not [var face, ..])
            return;

        foreach (var axis in face.Variations.Axes)
        {
            if (axis.IsHidden)
                continue;

            _axes.Add(new FontFaceVariationAxis(face.GetName(axis.NameId) ?? "", OpenTypeTagToString(axis.Tag), axis.MinValue, axis.MaxValue, axis.DefaultValue));
        }

        static string OpenTypeTagToString(uint tag) => string.Create(4, tag, static (span, value) =>
        {
            for (var i = 0; i < 4; i++)
            {
                span[i] = (char)((value >> (24 - (8 * i))) & 0xFF);
            }
        });
    }

    /// <inheritdoc/>
    [NotInIdl("The iterator of setlike<FontFaceVariationAxis>")]
    public IEnumerator<FontFaceVariationAxis> GetEnumerator() => _axes.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>A color palette of a font face (https://drafts.csswg.org/css-font-loading/#fontfacepalette): its colors.</summary>
/// <seealso href="https://drafts.csswg.org/css-font-loading/#fontfacepalette"/>
[IdlInterface("FontFacePalette", "https://drafts.csswg.org/css-font-loading/#fontfacepalette")]
public sealed class FontFacePalette : IReadOnlyList<string>
{
    private readonly string[] _colors;

    internal FontFacePalette(string[] colors, bool usableWithLightBackground, bool usableWithDarkBackground)
    {
        _colors = colors;
        UsableWithLightBackground = usableWithLightBackground;
        UsableWithDarkBackground = usableWithDarkBackground;
    }

    /// <summary>Gets the number of colors of the palette.</summary>
    /// <seealso href="https://drafts.csswg.org/css-font-loading/#dom-fontfacepalette-length"/>
    [IdlMember("length", "https://drafts.csswg.org/css-font-loading/#dom-fontfacepalette-length")]
    public uint Length => (uint)_colors.Length;

    /// <summary>Gets the color at the given index.</summary>
    /// <seealso href="https://drafts.csswg.org/css-font-loading/#fontfacepalette"/>
    [IdlMember("getter", "https://drafts.csswg.org/css-font-loading/#fontfacepalette")]
    [System.Runtime.CompilerServices.IndexerName("ItemAt")]
    public string this[uint index] => _colors[index];

    /// <summary>Gets the color at the given index.</summary>
    [NotInIdl]
    [System.Runtime.CompilerServices.IndexerName("ItemAt")]
    public string this[int index] => _colors[index];

    /// <summary>Gets whether the palette is designed for a light background.</summary>
    /// <seealso href="https://drafts.csswg.org/css-font-loading/#dom-fontfacepalette-usablewithlightbackground"/>
    [IdlMember("usableWithLightBackground", "https://drafts.csswg.org/css-font-loading/#dom-fontfacepalette-usablewithlightbackground")]
    public bool UsableWithLightBackground { get; }

    /// <summary>Gets whether the palette is designed for a dark background.</summary>
    /// <seealso href="https://drafts.csswg.org/css-font-loading/#dom-fontfacepalette-usablewithdarkbackground"/>
    [IdlMember("usableWithDarkBackground", "https://drafts.csswg.org/css-font-loading/#dom-fontfacepalette-usablewithdarkbackground")]
    public bool UsableWithDarkBackground { get; }

    /// <summary>Gets the number of colors of the palette.</summary>
    [NotInIdl]
    public int Count => _colors.Length;

    /// <inheritdoc/>
    [NotInIdl]
    public IEnumerator<string> GetEnumerator() => ((IEnumerable<string>)_colors).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// The color palettes of a font face (https://drafts.csswg.org/css-font-loading/#fontfacepalettes). The <c>CPAL</c> table of
/// the fonts is not exposed: the list is empty.
/// </summary>
/// <seealso href="https://drafts.csswg.org/css-font-loading/#fontfacepalettes"/>
[IdlInterface("FontFacePalettes", "https://drafts.csswg.org/css-font-loading/#fontfacepalettes")]
public sealed class FontFacePalettes : IReadOnlyList<FontFacePalette>
{
    private readonly List<FontFacePalette> _palettes = [];

    internal FontFacePalettes()
    {
    }

    /// <summary>Gets the number of palettes.</summary>
    /// <seealso href="https://drafts.csswg.org/css-font-loading/#dom-fontfacepalettes-length"/>
    [IdlMember("length", "https://drafts.csswg.org/css-font-loading/#dom-fontfacepalettes-length")]
    public uint Length => (uint)_palettes.Count;

    /// <summary>Gets the palette at the given index.</summary>
    /// <seealso href="https://drafts.csswg.org/css-font-loading/#fontfacepalettes"/>
    [IdlMember("getter", "https://drafts.csswg.org/css-font-loading/#fontfacepalettes")]
    [System.Runtime.CompilerServices.IndexerName("ItemAt")]
    public FontFacePalette this[uint index] => _palettes[(int)index];

    /// <summary>Gets the palette at the given index.</summary>
    [NotInIdl]
    [System.Runtime.CompilerServices.IndexerName("ItemAt")]
    public FontFacePalette this[int index] => _palettes[index];

    /// <summary>Gets the number of palettes.</summary>
    [NotInIdl]
    public int Count => _palettes.Count;

    /// <inheritdoc/>
    [NotInIdl]
    public IEnumerator<FontFacePalette> GetEnumerator() => _palettes.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
