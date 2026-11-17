namespace Meziantou.AspNetCore.Testing.Components.JSInterop;

/// <summary>A call of a JavaScript function by a component.</summary>
/// <param name="Identifier">The identifier of the function.</param>
/// <param name="Arguments">The arguments, as the component passed them (an <c>ElementReference</c> is resolved with <see cref="ComponentTestHost.GetElement"/>).</param>
/// <param name="ResultType">The type of the result the component expects (<see cref="object"/> for a call without result).</param>
public sealed record JSInvocation(string Identifier, IReadOnlyList<object?> Arguments, Type ResultType);
