namespace Meziantou.AspNetCore.Testing;

/// <summary>
/// A locator finds more than one element where the action or the assertion needs exactly one (the strict mode of
/// Playwright). Make the locator more specific, or use <see cref="Locator.First"/>, <see cref="Locator.Last"/> or
/// <see cref="Locator.Nth(int)"/>.
/// </summary>
public sealed class StrictModeViolationException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="StrictModeViolationException"/> class.</summary>
    public StrictModeViolationException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="StrictModeViolationException"/> class.</summary>
    /// <param name="message">The message.</param>
    public StrictModeViolationException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="StrictModeViolationException"/> class.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The inner exception.</param>
    public StrictModeViolationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
