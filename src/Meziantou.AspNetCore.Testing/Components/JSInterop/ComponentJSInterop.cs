using System.Collections.Concurrent;

namespace Meziantou.AspNetCore.Testing.Components.JSInterop;

/// <summary>
/// Configures the JavaScript interop of the components (<c>IJSRuntime</c>): the results of the calls, and the calls that
/// were made. A call that is not set up calls the JavaScript functions of the page when
/// <see cref="ComponentTestHostOptions.EnableJavaScript"/> is true, and otherwise follows <see cref="Mode"/>.
/// </summary>
public sealed class ComponentJSInterop
{
    private readonly ConcurrentDictionary<string, Func<JSInvocation, object?>> _handlers = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<JSInvocation> _invocations = new();

    internal ComponentJSInterop()
    {
    }

    /// <summary>Gets or sets what a call that is not set up returns when the page does not run JavaScript.</summary>
    public JSInteropMode Mode { get; set; } = JSInteropMode.Loose;

    /// <summary>Gets the calls, in the order they were made.</summary>
    public IReadOnlyList<JSInvocation> Invocations => [.. _invocations];

    /// <summary>Sets the result of the calls of a JavaScript function.</summary>
    /// <param name="identifier">The identifier of the function (<c>"localStorage.getItem"</c>).</param>
    /// <param name="handler">Returns the result of a call. The result is converted to the type the component expects.</param>
    public void Setup(string identifier, Func<JSInvocation, object?> handler)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        ArgumentNullException.ThrowIfNull(handler);
        _handlers[identifier] = handler;
    }

    /// <summary>Sets the result of the calls of a JavaScript function.</summary>
    /// <param name="identifier">The identifier of the function.</param>
    /// <param name="result">The result of the calls.</param>
    public void Setup(string identifier, object? result) => Setup(identifier, _ => result);

    /// <summary>Accepts the calls of a JavaScript function that returns nothing.</summary>
    /// <param name="identifier">The identifier of the function.</param>
    /// <param name="handler">Invoked for each call, or null.</param>
    public void SetupVoid(string identifier, Action<JSInvocation>? handler = null) => Setup(identifier, invocation =>
    {
        handler?.Invoke(invocation);
        return null;
    });

    /// <summary>Gets the calls of a JavaScript function.</summary>
    /// <param name="identifier">The identifier of the function.</param>
    /// <returns>The calls, in the order they were made.</returns>
    public IReadOnlyList<JSInvocation> GetInvocations(string identifier) => [.. _invocations.Where(invocation => string.Equals(invocation.Identifier, identifier, StringComparison.Ordinal))];

    internal void Record(JSInvocation invocation) => _invocations.Enqueue(invocation);

    internal bool TryGetHandler(string identifier, out Func<JSInvocation, object?> handler) => _handlers.TryGetValue(identifier, out handler!);
}
