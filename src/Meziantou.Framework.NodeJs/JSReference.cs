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
        return Host.InvokeMemberAsync(this, methodName, arguments, ResultKind.Json, NodeJsHost.ReadJsonElement, cancellationToken);
    }

    /// <summary>Calls a method of the referenced value, or the referenced function, and deserializes the result.</summary>
    /// <inheritdoc cref="InvokeAsync(string?, IReadOnlyList{JsonNode?}?, CancellationToken)"/>
    /// <exception cref="JsonException">The result cannot be deserialized to <typeparamref name="T"/>.</exception>
    public async Task<T?> InvokeAsync<T>(string? methodName, IReadOnlyList<JsonNode?>? arguments, JsonTypeInfo<T> resultTypeInfo, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resultTypeInfo);

        return await Host.InvokeMemberAsync(this, methodName, arguments, ResultKind.Json, NodeJsHost.CreateResultReader(resultTypeInfo), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Calls a method of the referenced value, or the referenced function, and deserializes the result. Arguments are serialized using reflection.</summary>
    /// <inheritdoc cref="InvokeAsync(string?, IReadOnlyList{JsonNode?}?, CancellationToken)"/>
    /// <exception cref="JsonException">The result cannot be deserialized to <typeparamref name="T"/>.</exception>
    [RequiresUnreferencedCode(NodeJsHost.ReflectionUnreferencedCodeMessage)]
    [RequiresDynamicCode(NodeJsHost.ReflectionDynamicCodeMessage)]
    public async Task<T?> InvokeAsync<T>(string? methodName, object?[]? arguments = null, JsonSerializerOptions? options = null, CancellationToken cancellationToken = default)
    {
        return await Host.InvokeMemberAsync(this, methodName, ArgumentWriter.SerializeArguments(arguments, options), ResultKind.Json, NodeJsHost.CreateResultReader<T>(options), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Calls a method of the referenced value without arguments, or the referenced function, and deserializes the result using reflection.</summary>
    /// <inheritdoc cref="InvokeAsync(string?, IReadOnlyList{JsonNode?}?, CancellationToken)"/>
    /// <exception cref="JsonException">The result cannot be deserialized to <typeparamref name="T"/>.</exception>
    [RequiresUnreferencedCode(NodeJsHost.ReflectionUnreferencedCodeMessage)]
    [RequiresDynamicCode(NodeJsHost.ReflectionDynamicCodeMessage)]
    public Task<T?> InvokeAsync<T>(string? methodName, CancellationToken cancellationToken)
    {
        return InvokeAsync<T>(methodName, arguments: null, options: null, cancellationToken);
    }

    /// <summary>Calls a method of the referenced value, or the referenced function, and ignores its result.</summary>
    /// <inheritdoc cref="InvokeAsync(string?, IReadOnlyList{JsonNode?}?, CancellationToken)"/>
    public Task InvokeVoidAsync(string? methodName, IReadOnlyList<JsonNode?>? arguments = null, CancellationToken cancellationToken = default)
    {
        return Host.InvokeMemberAsync(this, methodName, arguments, ResultKind.Void, NodeJsHost.ReadJsonElement, cancellationToken);
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
        var referenceId = await Host.InvokeMemberAsync(this, methodName, arguments, ResultKind.Reference, NodeJsHost.ReadReferenceId, cancellationToken).ConfigureAwait(false);
        return new JSReference(Host, referenceId);
    }

    /// <summary>Calls a method of the referenced value, or the referenced function, and returns a reference to its result. Arguments are serialized using reflection.</summary>
    /// <inheritdoc cref="InvokeReferenceAsync(string?, IReadOnlyList{JsonNode?}?, CancellationToken)"/>
    [RequiresUnreferencedCode(NodeJsHost.ReflectionUnreferencedCodeMessage)]
    [RequiresDynamicCode(NodeJsHost.ReflectionDynamicCodeMessage)]
    public Task<JSReference> InvokeReferenceAsync(string? methodName, object?[]? arguments, JsonSerializerOptions? options, CancellationToken cancellationToken = default)
    {
        return InvokeReferenceAsync(methodName, ArgumentWriter.SerializeArguments(arguments, options), cancellationToken);
    }

    /// <summary>Creates an instance (<c>new</c>) of the referenced class, or of a class that is a member of the referenced value, and keeps the instance in the Node.js process.</summary>
    /// <param name="memberName">The name of the member of the referenced value that is a class or a constructor function. When <see langword="null"/>, the referenced value is the class.</param>
    /// <param name="arguments">The arguments passed to the constructor.</param>
    /// <param name="cancellationToken">A token to stop waiting for the result. The JavaScript code keeps running.</param>
    /// <returns>A reference to the new instance. Dispose it when the instance is no longer needed.</returns>
    /// <exception cref="NodeJsException">The value is not a constructor, the constructor throws, or the Node.js process exits.</exception>
    public async Task<JSReference> CreateInstanceAsync(string? memberName, IReadOnlyList<JsonNode?>? arguments = null, CancellationToken cancellationToken = default)
    {
        var referenceId = await Host.InvokeMemberAsync(this, memberName, arguments, ResultKind.Reference, NodeJsHost.ReadReferenceId, cancellationToken, construct: true).ConfigureAwait(false);
        return new JSReference(Host, referenceId);
    }

    /// <summary>Creates an instance (<c>new</c>) of the referenced class, or of a class that is a member of the referenced value, and keeps the instance in the Node.js process. Arguments are serialized using reflection.</summary>
    /// <inheritdoc cref="CreateInstanceAsync(string?, IReadOnlyList{JsonNode?}?, CancellationToken)"/>
    [RequiresUnreferencedCode(NodeJsHost.ReflectionUnreferencedCodeMessage)]
    [RequiresDynamicCode(NodeJsHost.ReflectionDynamicCodeMessage)]
    public Task<JSReference> CreateInstanceAsync(string? memberName, object?[]? arguments, JsonSerializerOptions? options, CancellationToken cancellationToken = default)
    {
        return CreateInstanceAsync(memberName, ArgumentWriter.SerializeArguments(arguments, options), cancellationToken);
    }

    /// <summary>Gets the JSON representation of the referenced value (awaited if it is a promise).</summary>
    /// <param name="cancellationToken">A token to stop waiting for the result.</param>
    /// <exception cref="NodeJsException">The value cannot be serialized, or the Node.js process exits.</exception>
    public Task<JsonElement> GetValueAsync(CancellationToken cancellationToken = default)
    {
        return Host.GetReferenceValueAsync(this, NodeJsHost.ReadJsonElement, cancellationToken);
    }

    /// <summary>Gets the referenced value, deserialized from its JSON representation.</summary>
    /// <inheritdoc cref="GetValueAsync(CancellationToken)"/>
    /// <exception cref="JsonException">The result cannot be deserialized to <typeparamref name="T"/>.</exception>
    public async Task<T?> GetValueAsync<T>(JsonTypeInfo<T> resultTypeInfo, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resultTypeInfo);

        return await Host.GetReferenceValueAsync(this, NodeJsHost.CreateResultReader(resultTypeInfo), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Gets the referenced value, deserialized from its JSON representation using reflection.</summary>
    /// <inheritdoc cref="GetValueAsync(CancellationToken)"/>
    /// <exception cref="JsonException">The result cannot be deserialized to <typeparamref name="T"/>.</exception>
    [RequiresUnreferencedCode("JSON deserialization might require types that cannot be statically analyzed. Use the overload that takes a JsonTypeInfo instead.")]
    [RequiresDynamicCode("JSON deserialization might require types that cannot be statically analyzed and might need runtime code generation. Use the overload that takes a JsonTypeInfo instead.")]
    public async Task<T?> GetValueAsync<T>(JsonSerializerOptions? options = null, CancellationToken cancellationToken = default)
    {
        return await Host.GetReferenceValueAsync(this, NodeJsHost.CreateResultReader<T>(options), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Gets the referenced value, deserialized from its JSON representation using reflection.</summary>
    /// <inheritdoc cref="GetValueAsync(CancellationToken)"/>
    /// <exception cref="JsonException">The result cannot be deserialized to <typeparamref name="T"/>.</exception>
    [RequiresUnreferencedCode("JSON deserialization might require types that cannot be statically analyzed. Use the overload that takes a JsonTypeInfo instead.")]
    [RequiresDynamicCode("JSON deserialization might require types that cannot be statically analyzed and might need runtime code generation. Use the overload that takes a JsonTypeInfo instead.")]
    public Task<T?> GetValueAsync<T>(CancellationToken cancellationToken)
    {
        return GetValueAsync<T>(options: null, cancellationToken);
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
