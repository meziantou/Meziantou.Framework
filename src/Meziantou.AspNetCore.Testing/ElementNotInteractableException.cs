namespace Meziantou.AspNetCore.Testing;

/// <summary>The element cannot receive the input.</summary>
public sealed class ElementNotInteractableException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="ElementNotInteractableException"/> class.</summary>
    public ElementNotInteractableException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ElementNotInteractableException"/> class.</summary>
    /// <param name="message">The message.</param>
    public ElementNotInteractableException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ElementNotInteractableException"/> class.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The inner exception.</param>
    public ElementNotInteractableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
