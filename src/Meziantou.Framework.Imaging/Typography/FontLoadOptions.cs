namespace Meziantou.Framework.Imaging.Typography;

/// <summary>Options for loading a font. Instances are immutable and can be shared.</summary>
public sealed class FontLoadOptions
{
    /// <summary>Gets the default options.</summary>
    public static FontLoadOptions Default { get; } = new();

    /// <summary>
    /// Gets the configuration: the limits that bound the bytes read (<see cref="ImageResourceLimits.MaxEncodedBytes"/>),
    /// the bytes the font keeps (<see cref="ImageResourceLimits.MaxFontBytes"/>) and the work of decoding one of its
    /// glyphs (<see cref="ImageResourceLimits.MaxGlyphComplexity"/>). Defaults to <see cref="ImageConfiguration.Default"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    public ImageConfiguration Configuration
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = ImageConfiguration.Default;
}
