namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The exception thrown when a font does not follow the rules of its format.</summary>
#pragma warning disable CA1032, CA1064 // The exceptions of the font code never leave the libraries that use it: each one converts them to its own
internal sealed class InvalidFontException : FontException
{
    public InvalidFontException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
#pragma warning restore CA1032, CA1064
