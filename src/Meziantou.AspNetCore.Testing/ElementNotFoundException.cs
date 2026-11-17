namespace Meziantou.AspNetCore.Testing;

/// <summary>No element matches the query.</summary>
public sealed class ElementNotFoundException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="ElementNotFoundException"/> class.</summary>
    public ElementNotFoundException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ElementNotFoundException"/> class.</summary>
    /// <param name="message">The message.</param>
    public ElementNotFoundException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ElementNotFoundException"/> class.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The inner exception.</param>
    public ElementNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
