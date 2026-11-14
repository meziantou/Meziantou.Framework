namespace Meziantou.AspNetCore.Testing.Components.JSInterop;

/// <summary>A component called a JavaScript function that is not set up, in <see cref="JSInteropMode.Strict"/> mode.</summary>
public sealed class UnplannedJSInvocationException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="UnplannedJSInvocationException"/> class.</summary>
    public UnplannedJSInvocationException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="UnplannedJSInvocationException"/> class.</summary>
    /// <param name="message">The message.</param>
    public UnplannedJSInvocationException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="UnplannedJSInvocationException"/> class.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The inner exception.</param>
    public UnplannedJSInvocationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="UnplannedJSInvocationException"/> class.</summary>
    /// <param name="invocation">The call that is not set up.</param>
    public UnplannedJSInvocationException(JSInvocation invocation)
        : base($"The JavaScript function '{invocation?.Identifier}' is not set up. Call ComponentJSInterop.Setup(\"{invocation?.Identifier}\", ...) or enable JavaScript.")
    {
        Invocation = invocation;
    }

    /// <summary>Gets the call that is not set up.</summary>
    public JSInvocation? Invocation { get; }
}
