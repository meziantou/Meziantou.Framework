using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Meziantou.Framework.NodeJs.Internal;

namespace Meziantou.Framework.NodeJs;

/// <summary>A reference to a value kept in the Node.js process, such as an object that cannot be serialized as JSON.</summary>
/// <remarks>
/// <para>The value is kept until the reference is disposed or the Node.js process exits. A reference can only be used with the process that created it.</para>
/// <para>A <see cref="JSReference"/> converts implicitly to a <see cref="JsonNode"/>, so it can be used as an argument, or nested in a <see cref="JsonObject"/> or a <see cref="JsonArray"/> argument. The function receives the referenced value.</para>
/// </remarks>
/// <example>
/// <code>
/// await using var client = await node.InvokeReferenceAsync("./database.mjs", "connect", ["connection-string"]);
/// var rows = await client.InvokeAsync("query", ["SELECT 1"]);
/// await node.InvokeVoidAsync("./database.mjs", "seed", [client]);
/// </code>
/// </example>
[JsonConverter(typeof(NodeJsArgumentConverter<JSReference>))]
public sealed class JSReference : IAsyncDisposable, IDisposable
{
    private int _disposed;

    internal JSReference(NodeJsHost host, long id)
    {
        Host = host;
        Id = id;
    }

    /// <summary>Gets the host of the Node.js process that keeps the value.</summary>
    public NodeJsHost Host { get; }

    internal long Id { get; }

    internal bool IsDisposed => Volatile.Read(ref _disposed) is 1;

    /// <summary>Calls a method of the referenced value, or the referenced function.</summary>
    /// <param name="methodName">The name of the method, called with the referenced value as <c>this</c>. When the member is not a function, its value is returned. When <see langword="null"/>, the referenced value is called, so it must be a function.</param>
    /// <param name="arguments">The arguments passed to the function.</param>
    /// <param name="cancellationToken">A token to stop waiting for the result. The JavaScript code keeps running.</param>
    /// <returns>The JSON representation of the value returned by the function (awaited if it is a promise).</returns>
    /// <exception cref="NodeJsException">The JavaScript code throws, or the Node.js process exits.</exception>
    public Task<JsonElement> InvokeAsync(string? methodName, IReadOnlyList<JsonNode?>? arguments = null, CancellationToken cancellationToken = default)
    {
        return Host.InvokeMemberAsync(this, methodName, arguments, ResultKind.Json, cancellationToken);
    }

    /// <summary>Calls a method of the referenced value, or the referenced function, and deserializes the result.</summary>
    /// <inheritdoc cref="InvokeAsync(string?, IReadOnlyList{JsonNode?}?, CancellationToken)"/>
    public async Task<T?> InvokeAsync<T>(string? methodName, IReadOnlyList<JsonNode?>? arguments, JsonTypeInfo<T> resultTypeInfo, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resultTypeInfo);

        var result = await InvokeAsync(methodName, arguments, cancellationToken).ConfigureAwait(false);
        return result.Deserialize(resultTypeInfo);
    }

    /// <summary>Calls a method of the referenced value, or the referenced function, and deserializes the result. Arguments are serialized using reflection.</summary>
    /// <inheritdoc cref="InvokeAsync(string?, IReadOnlyList{JsonNode?}?, CancellationToken)"/>
    [RequiresUnreferencedCode(NodeJsHost.ReflectionUnreferencedCodeMessage)]
    [RequiresDynamicCode(NodeJsHost.ReflectionDynamicCodeMessage)]
    public async Task<T?> InvokeAsync<T>(string? methodName, object?[]? arguments = null, JsonSerializerOptions? options = null, CancellationToken cancellationToken = default)
    {
        var result = await InvokeAsync(methodName, ArgumentWriter.SerializeArguments(arguments, options), cancellationToken).ConfigureAwait(false);
        return result.Deserialize<T>(options);
    }

    /// <summary>Calls a method of the referenced value, or the referenced function, and ignores its result.</summary>
    /// <inheritdoc cref="InvokeAsync(string?, IReadOnlyList{JsonNode?}?, CancellationToken)"/>
    public Task InvokeVoidAsync(string? methodName, IReadOnlyList<JsonNode?>? arguments = null, CancellationToken cancellationToken = default)
    {
        return Host.InvokeMemberAsync(this, methodName, arguments, ResultKind.Void, cancellationToken);
    }

    /// <summary>Calls a method of the referenced value, or the referenced function, and ignores its result. Arguments are serialized using reflection.</summary>
    /// <inheritdoc cref="InvokeAsync(string?, IReadOnlyList{JsonNode?}?, CancellationToken)"/>
    [RequiresUnreferencedCode(NodeJsHost.ReflectionUnreferencedCodeMessage)]
    [RequiresDynamicCode(NodeJsHost.ReflectionDynamicCodeMessage)]
    public Task InvokeVoidAsync(string? methodName, object?[]? arguments, JsonSerializerOptions? options, CancellationToken cancellationToken = default)
    {
        return InvokeVoidAsync(methodName, ArgumentWriter.SerializeArguments(arguments, options), cancellationToken);
    }

    /// <summary>Calls a method of the referenced value, or the referenced function, and returns a reference to its result.</summary>
    /// <inheritdoc cref="InvokeAsync(string?, IReadOnlyList{JsonNode?}?, CancellationToken)"/>
    /// <returns>A reference to the value returned by the function (awaited if it is a promise). Dispose it when the value is no longer needed.</returns>
    public async Task<JSReference> InvokeReferenceAsync(string? methodName, IReadOnlyList<JsonNode?>? arguments = null, CancellationToken cancellationToken = default)
    {
        var result = await Host.InvokeMemberAsync(this, methodName, arguments, ResultKind.Reference, cancellationToken).ConfigureAwait(false);
        return new JSReference(Host, result.GetInt64());
    }

    /// <summary>Calls a method of the referenced value, or the referenced function, and returns a reference to its result. Arguments are serialized using reflection.</summary>
    /// <inheritdoc cref="InvokeReferenceAsync(string?, IReadOnlyList{JsonNode?}?, CancellationToken)"/>
    [RequiresUnreferencedCode(NodeJsHost.ReflectionUnreferencedCodeMessage)]
    [RequiresDynamicCode(NodeJsHost.ReflectionDynamicCodeMessage)]
    public Task<JSReference> InvokeReferenceAsync(string? methodName, object?[]? arguments, JsonSerializerOptions? options, CancellationToken cancellationToken = default)
    {
        return InvokeReferenceAsync(methodName, ArgumentWriter.SerializeArguments(arguments, options), cancellationToken);
    }

    /// <summary>Gets the JSON representation of the referenced value (awaited if it is a promise).</summary>
    /// <param name="cancellationToken">A token to stop waiting for the result.</param>
    /// <exception cref="NodeJsException">The value cannot be serialized, or the Node.js process exits.</exception>
    public Task<JsonElement> GetValueAsync(CancellationToken cancellationToken = default)
    {
        return Host.GetReferenceValueAsync(this, cancellationToken);
    }

    /// <summary>Gets the referenced value, deserialized from its JSON representation.</summary>
    /// <inheritdoc cref="GetValueAsync(CancellationToken)"/>
    public async Task<T?> GetValueAsync<T>(JsonTypeInfo<T> resultTypeInfo, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resultTypeInfo);

        var result = await GetValueAsync(cancellationToken).ConfigureAwait(false);
        return result.Deserialize(resultTypeInfo);
    }

    /// <summary>Gets the referenced value, deserialized from its JSON representation using reflection.</summary>
    /// <inheritdoc cref="GetValueAsync(CancellationToken)"/>
    [RequiresUnreferencedCode("JSON deserialization might require types that cannot be statically analyzed. Use the overload that takes a JsonTypeInfo instead.")]
    [RequiresDynamicCode("JSON deserialization might require types that cannot be statically analyzed and might need runtime code generation. Use the overload that takes a JsonTypeInfo instead.")]
    public async Task<T?> GetValueAsync<T>(JsonSerializerOptions? options = null, CancellationToken cancellationToken = default)
    {
        var result = await GetValueAsync(cancellationToken).ConfigureAwait(false);
        return result.Deserialize<T>(options);
    }

    /// <summary>Converts the reference to a <see cref="JsonNode"/>, to use it as an argument or in a <see cref="JsonObject"/> or <see cref="JsonArray"/> argument.</summary>
    public JsonNode ToJsonNode() => JsonValue.Create(this, NodeJsJsonSerializerContext.Default.JSReference)!;

    /// <summary>Converts the reference to a <see cref="JsonNode"/>, to use it as an argument or in a <see cref="JsonObject"/> or <see cref="JsonArray"/> argument.</summary>
    public static implicit operator JsonNode?(JSReference? value) => value?.ToJsonNode();

    /// <summary>Releases the value, so Node.js can collect it.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) is 1)
            return;

        await Host.ReleaseReferenceAsync(Id).ConfigureAwait(false);
    }

    /// <summary>Releases the value, so Node.js can collect it. The release message is sent in the background.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) is 1)
            return;

        Host.ReleaseReference(Id);
    }

    /// <inheritdoc/>
    public override string ToString() => "JSReference(" + Id.ToString(CultureInfo.InvariantCulture) + ")";
}
