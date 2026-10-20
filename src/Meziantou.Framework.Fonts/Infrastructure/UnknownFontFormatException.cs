namespace Meziantou.Framework.Fonts;

/// <summary>The exception thrown when data does not start with the signature of a font format.</summary>
#pragma warning disable CA1032 // The exceptions are created by the font code only
public sealed class UnknownFontFormatException : FontException
{
    internal UnknownFontFormatException(string message)
        : base(message)
    {
    }
}
#pragma warning restore CA1032
