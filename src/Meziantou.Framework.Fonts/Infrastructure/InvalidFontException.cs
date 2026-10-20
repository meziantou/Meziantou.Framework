namespace Meziantou.Framework.Fonts;

/// <summary>The exception thrown when a font does not follow the rules of its format.</summary>
#pragma warning disable CA1032 // The exceptions are created by the font code only
public sealed class InvalidFontException : FontException
{
    internal InvalidFontException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
#pragma warning restore CA1032
