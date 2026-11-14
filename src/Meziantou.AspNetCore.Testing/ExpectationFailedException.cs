namespace Meziantou.AspNetCore.Testing;

/// <summary>An assertion of <see cref="Expectations.Expect(Locator)"/> or <see cref="Expectations.Expect(TestPage)"/> is still false after its timeout.</summary>
public sealed class ExpectationFailedException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="ExpectationFailedException"/> class.</summary>
    public ExpectationFailedException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ExpectationFailedException"/> class.</summary>
    /// <param name="message">The message.</param>
    public ExpectationFailedException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ExpectationFailedException"/> class.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The inner exception.</param>
    public ExpectationFailedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
