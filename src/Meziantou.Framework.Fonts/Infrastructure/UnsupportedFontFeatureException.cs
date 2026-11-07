namespace Meziantou.Framework.Fonts;

/// <summary>The exception thrown when a valid font uses a feature of its format that the font code does not support.</summary>
#pragma warning disable CA1032 // The exceptions are created by the font code only
public sealed class UnsupportedFontFeatureException : FontException
{
    internal UnsupportedFontFeatureException(string message, string feature)
        : base(message)
    {
        Feature = feature;
    }

    /// <summary>Gets the name of the feature.</summary>
    public string Feature { get; }
}
#pragma warning restore CA1032
