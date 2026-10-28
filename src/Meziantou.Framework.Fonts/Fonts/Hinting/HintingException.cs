namespace Meziantou.Framework.Fonts;

/// <summary>
/// The TrueType instructions of a font broke a rule of the specification: the glyph they were run for is drawn
/// without hinting. It never leaves the font code.
/// </summary>
#pragma warning disable CA1064 // It never leaves the font code
internal sealed class HintingException : Exception
{
    public HintingException()
    {
    }

    public HintingException(string message)
        : base(message)
    {
    }

    public HintingException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
#pragma warning restore CA1064
