namespace Meziantou.Framework.Imaging.Typography;

/// <summary>An instance a variable font names, such as "Bold" or "Condensed Light": a value for each of its axes.</summary>
public sealed class FontNamedInstance
{
    internal FontNamedInstance(string name, string? postScriptName, FontVariationSetting[] settings)
    {
        Name = name;
        PostScriptName = postScriptName;
        Settings = settings;
    }

    /// <summary>Gets the name of the instance; empty when the font gives none.</summary>
    public string Name { get; }

    /// <summary>Gets the PostScript name of the instance, or <see langword="null"/> when the font gives none.</summary>
    public string? PostScriptName { get; }

    /// <summary>Gets the value of each axis of the font, in the order of <see cref="Font.VariationAxes"/>.</summary>
    public IReadOnlyList<FontVariationSetting> Settings { get; }

    /// <inheritdoc />
    public override string ToString() => Name;
}
