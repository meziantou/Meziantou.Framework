namespace Meziantou.Framework.Fonts;

/// <summary>The base class of the exceptions of the font code.</summary>
/// <remarks>
/// The libraries that use the font code may turn these exceptions into their own at their public boundary.
/// </remarks>
#pragma warning disable CA1032 // The exceptions are created by the font code only
public abstract class FontException : Exception
{
    private protected FontException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
#pragma warning restore CA1032
