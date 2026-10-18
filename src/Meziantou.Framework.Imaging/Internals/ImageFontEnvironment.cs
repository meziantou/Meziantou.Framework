using Meziantou.Framework.Imaging.Typography;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The environment of the fonts of the library: the limits of an <see cref="ImageConfiguration"/>, and the temporary
/// buffers of the font code charged to its allocation scopes.
/// </summary>
internal sealed class ImageFontEnvironment : FontEnvironment
{
    public ImageFontEnvironment(ImageConfiguration configuration)
    {
        Configuration = configuration;
        var limits = configuration.Limits;
        Limits = new FontResourceLimits
        {
            MaxEncodedBytes = limits.MaxEncodedBytes,
            MaxFontBytes = limits.MaxFontBytes,
            MaxGlyphComplexity = limits.MaxGlyphComplexity,
            MaxTextGlyphs = limits.MaxTextGlyphs,
        };
    }

    /// <summary>Gets the configuration.</summary>
    public ImageConfiguration Configuration { get; }

    public override FontResourceLimits Limits { get; }

    public override AllocationScope CreateAllocator(string name) => AllocationScope.Create(Configuration, name);

    /// <summary>Gets the configuration a font was loaded with (<see cref="FontLoadOptions.Configuration"/>).</summary>
    public static ImageConfiguration GetConfiguration(FontFace face) => face.Data.Environment is ImageFontEnvironment environment ? environment.Configuration : ImageConfiguration.Default;
}
