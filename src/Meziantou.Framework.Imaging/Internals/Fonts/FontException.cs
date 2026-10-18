namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The base class of the exceptions of the font code.</summary>
/// <remarks>
/// The font code is shared by several libraries: each one turns these exceptions into its own at its public boundary.
/// </remarks>
#pragma warning disable CA1032, CA1064 // The exceptions of the font code never leave the libraries that use it: each one converts them to its own
internal abstract class FontException : Exception
{
    protected FontException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
#pragma warning restore CA1032, CA1064
