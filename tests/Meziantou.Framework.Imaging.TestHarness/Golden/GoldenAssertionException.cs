namespace Meziantou.Framework.Imaging.TestHarness.Golden;

/// <summary>Thrown when decoded output does not match a golden fixture. The message contains the full diagnostics.</summary>
public sealed class GoldenAssertionException : Exception
{
    public GoldenAssertionException()
    {
    }

    public GoldenAssertionException(string message)
        : base(message)
    {
    }

    public GoldenAssertionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
