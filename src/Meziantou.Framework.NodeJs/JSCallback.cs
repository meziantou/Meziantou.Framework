using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Meziantou.Framework.NodeJs.Internal;

namespace Meziantou.Framework.NodeJs;

/// <summary>A .NET delegate passed as an argument of a call, which JavaScript code receives as an async function.</summary>
/// <remarks>
/// <para>
/// Calling the function sends its arguments to the .NET process, invokes the delegate, and resolves the returned promise with the result of the delegate.
/// The arguments are converted to JSON as the results of calls are, and the delegate receives their JSON representation.
/// The result of the delegate is converted as the arguments of calls are, so it can contain <see cref="JSValue"/>, <see cref="JSReference"/>, and <see cref="JSCallback"/> values.
/// When the delegate throws, the promise is rejected with an <c>Error</c> whose <c>name</c> is the name of the type of the exception, and whose <c>message</c> is its message.
/// </para>
/// <para>
/// The delegate runs on the thread pool, and can be called concurrently and after the call that passed it completes, for instance when the JavaScript code keeps the function as an event handler.
/// The process keeps the delegate as long as the JavaScript function is not garbage collected, or until the process exits or the host is disposed.
/// The <see cref="CancellationToken"/> of the delegate is canceled when the host is disposed, the process exits, or the worker thread that called it exits.
/// </para>
/// <para>
/// A <see cref="JSCallback"/> converts implicitly to a <see cref="JsonNode"/>, so it can be used as an argument, or nested in a <see cref="JsonObject"/> or a <see cref="JsonArray"/> argument.
/// It is not bound to a process, so it can be used in the calls of several hosts. Within a call, the same instance is the same JavaScript function.
/// </para>
/// </remarks>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.NodeJs.Tests/NodeJsHostTests.cs" ref:region="JSCallback_Example" ref:dedent="true">
/// await using var node = await NodeJsHost.StartAsync();
///
/// // A .NET delegate that JavaScript code calls as an async function, e.g. to report progress
/// var progress = new ConcurrentQueue&lt;int&gt;();
/// var onProgress = JSCallback.Create(args =&gt; progress.Enqueue(args[0].GetInt32()));
///
/// // An asynchronous delegate: the promise resolves with its result
/// var getPrice = JSCallback.Create(async (args, cancellationToken) =&gt;
/// {
///     await Task.Delay(10, cancellationToken);
///     return args[0].GetString() is "apple" ? 3 : 5;
/// });
///
/// var total = await node.EvaluateAsync("""
///     const [items, onProgress, getPrice] = args;
///     let total = 0;
///     for (let i = 0; i &lt; items.length; i++) {
///         total += await getPrice(items[i]);
///         await onProgress(i + 1);
///     }
///
///     return total;
///     """, [new JsonArray("apple", "pear"), onProgress, getPrice]);
///
/// Assert.Equal(8, total.GetInt32());
/// Assert.Equal(new[] { 1, 2 }, progress.ToArray());
/// </code>
/// </example>
[JsonConverter(typeof(NodeJsArgumentConverter<JSCallback>))]
public sealed class JSCallback
{
    private readonly Func<IReadOnlyList<JsonElement>, CancellationToken, Task<JsonNode?>> _callback;

    private JSCallback(Func<IReadOnlyList<JsonElement>, CancellationToken, Task<JsonNode?>> callback)
    {
        _callback = callback;
    }

    /// <summary>Creates a function that invokes an asynchronous delegate and returns its result.</summary>
    /// <param name="callback">The delegate. It receives the JSON representation of the arguments of the function, and a token canceled when the host is terminated.</param>
    public static JSCallback Create(Func<IReadOnlyList<JsonElement>, CancellationToken, Task<JsonNode?>> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        return new(callback);
    }

    /// <summary>Creates a function that invokes an asynchronous delegate. The function returns <c>undefined</c>.</summary>
    /// <inheritdoc cref="Create(Func{IReadOnlyList{JsonElement}, CancellationToken, Task{JsonNode?}})"/>
    public static JSCallback Create(Func<IReadOnlyList<JsonElement>, CancellationToken, Task> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        return new(async (arguments, cancellationToken) =>
        {
            await callback(arguments, cancellationToken).ConfigureAwait(false);
            return JSValue.Undefined;
        });
    }

    /// <summary>Creates a function that invokes a delegate and returns its result.</summary>
    /// <param name="callback">The delegate. It receives the JSON representation of the arguments of the function.</param>
    public static JSCallback Create(Func<IReadOnlyList<JsonElement>, JsonNode?> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        return new((arguments, _) => Task.FromResult(callback(arguments)));
    }

    /// <summary>Creates a function that invokes a delegate. The function returns <c>undefined</c>.</summary>
    /// <inheritdoc cref="Create(Func{IReadOnlyList{JsonElement}, JsonNode?})"/>
    public static JSCallback Create(Action<IReadOnlyList<JsonElement>> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        return new((arguments, _) =>
        {
            callback(arguments);
            return Task.FromResult<JsonNode?>(JSValue.Undefined);
        });
    }

    internal Task<JsonNode?> InvokeAsync(IReadOnlyList<JsonElement> arguments, CancellationToken cancellationToken) => _callback(arguments, cancellationToken);

    /// <summary>Converts the callback to a <see cref="JsonNode"/>, to use it as an argument or in a <see cref="JsonObject"/> or <see cref="JsonArray"/> argument.</summary>
    public JsonNode ToJsonNode() => JsonValue.Create(this, NodeJsJsonSerializerContext.Default.JSCallback)!;

    /// <summary>Converts the callback to a <see cref="JsonNode"/>, to use it as an argument or in a <see cref="JsonObject"/> or <see cref="JsonArray"/> argument.</summary>
    public static implicit operator JsonNode?(JSCallback? value) => value?.ToJsonNode();
}
