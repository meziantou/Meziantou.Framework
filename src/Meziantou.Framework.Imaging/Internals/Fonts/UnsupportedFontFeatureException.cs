namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The exception thrown when a valid font uses a feature of its format that the font code does not support.</summary>
#pragma warning disable CA1032, CA1064 // The exceptions of the font code never leave the libraries that use it: each one converts them to its own
internal sealed class UnsupportedFontFeatureException : FontException
{
    public UnsupportedFontFeatureException(string message, string feature)
        : base(message)
    {
        Feature = feature;
    }

    /// <summary>Gets the name of the feature.</summary>
    public string Feature { get; }
}
#pragma warning restore CA1032, CA1064
