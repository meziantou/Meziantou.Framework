namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The exception thrown when data does not start with the signature of a font format.</summary>
#pragma warning disable CA1032, CA1064 // The exceptions of the font code never leave the libraries that use it: each one converts them to its own
internal sealed class UnknownFontFormatException : FontException
{
    public UnknownFontFormatException(string message)
        : base(message)
    {
    }
}
#pragma warning restore CA1032, CA1064
