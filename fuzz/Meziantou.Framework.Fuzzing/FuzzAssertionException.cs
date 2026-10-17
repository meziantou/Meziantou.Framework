namespace Meziantou.Framework.Fuzzing;

/// <summary>A property of the code under test that does not hold for an input.</summary>
public sealed class FuzzAssertionException : Exception
{
    public FuzzAssertionException()
    {
    }

    public FuzzAssertionException(string message)
        : base(message)
    {
    }

    public FuzzAssertionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
