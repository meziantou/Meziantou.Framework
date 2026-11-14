namespace Meziantou.AspNetCore.Testing.Components;

/// <summary>No component matches the query.</summary>
public sealed class ComponentNotFoundException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="ComponentNotFoundException"/> class.</summary>
    public ComponentNotFoundException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ComponentNotFoundException"/> class.</summary>
    /// <param name="message">The message.</param>
    public ComponentNotFoundException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ComponentNotFoundException"/> class.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The inner exception.</param>
    public ComponentNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
