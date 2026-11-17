namespace Meziantou.AspNetCore.Testing.Components;

/// <summary>A component threw an exception that no error boundary handled, while rendering or handling an event.</summary>
public sealed class ComponentException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="ComponentException"/> class.</summary>
    public ComponentException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ComponentException"/> class.</summary>
    /// <param name="message">The message.</param>
    public ComponentException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ComponentException"/> class.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The exception thrown by the component.</param>
    public ComponentException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
