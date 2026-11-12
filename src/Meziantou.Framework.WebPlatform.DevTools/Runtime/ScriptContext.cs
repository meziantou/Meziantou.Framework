using System.Text.Json.Nodes;
using Meziantou.Framework.JavaScriptEngine;
using Meziantou.Framework.WebPlatform.DevTools.Protocol;
using Meziantou.Framework.WebPlatform.DevTools.Targets;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.JavaScript;

namespace Meziantou.Framework.WebPlatform.DevTools.Runtime;

/// <summary>
/// An execution context of the protocol: the main world of a document of a frame, or an isolated world
/// (<c>Page.createIsolatedWorld</c>, or the world of a script of <c>Page.addScriptToEvaluateOnNewDocument</c>).
/// </summary>
/// <remarks>
/// The script engine has one realm per window: the isolated worlds run in the realm of the main world (as the browsers without
/// isolated worlds), so the scripts of the page can see the changes of their built-ins. The contexts are used on the thread
/// of the browser context only.
/// </remarks>
internal sealed class ScriptContext
{
    private readonly Dictionary<string, JsValue> _functions = new(StringComparer.Ordinal);
    private readonly TaskCompletionSource _destroyed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private JsRealm? _realm;

    public ScriptContext(int id, FrameState frame, string worldName)
    {
        Id = id;
        Frame = frame;
        WorldName = worldName;
        UniqueId = CdpConnection.CreateId();
    }

    public int Id { get; }

    public string UniqueId { get; }

    public FrameState Frame { get; }

    /// <summary>Gets the name of the world: an empty string for the main world.</summary>
    public string WorldName { get; }

    public bool IsDefault => WorldName.Length == 0;

    /// <summary>Gets or sets the document of the context (null until the page runs).</summary>
    public Document? Document { get; set; }

    public bool IsDestroyed => _destroyed.Task.IsCompleted;

    public void Destroy() => _destroyed.TrySetResult();

    public Window Window
    {
        get
        {
            if (IsDestroyed || Document?.DefaultView is not { } window)
                throw new ProtocolException("Cannot find context with specified id");

            return window;
        }
    }

    public JavaScriptScriptEngine Engine => Frame.Target.Context.ScriptEngine;

    /// <summary>Gets the realm of the window (to create the functions of the host).</summary>
    public JsRealm Realm
    {
        get
        {
            if (_realm is null)
            {
                var function = GetFunction("() => {}");
                _realm = ((JsFunction)function.AsObject()).Realm;
            }

            return _realm;
        }
    }

    public JsonObject ToDescription() => new()
    {
        ["id"] = Id,
        ["origin"] = Frame.SecurityOrigin,
        ["name"] = WorldName,
        ["uniqueId"] = UniqueId,
        ["auxData"] = new JsonObject
        {
            ["isDefault"] = IsDefault,
            ["type"] = IsDefault ? "default" : "isolated",
            ["frameId"] = Frame.FrameId,
        },
    };

    /// <summary>Evaluates code in the realm, as an indirect <c>eval</c> (the content security policy does not apply).</summary>
    public JsValue Evaluate(string code)
    {
        try
        {
            return ScriptCache.GetOrParse(code) is { } script ? Engine.Evaluate(Window, script) : Engine.Evaluate(Window, code);
        }
        finally
        {
            Document?.DefaultView?.BrowsingContext?.EventLoop.PerformMicrotaskCheckpoint();
        }
    }

    /// <summary>Returns the function defined by a source text (e.g. <c>(a, b) => a + b</c>), compiled once per context.</summary>
    public JsValue GetFunction(string source)
    {
        if (!_functions.TryGetValue(source, out var function))
        {
            var code = "(" + source + ")";
            function = ScriptCache.GetOrParse(code) is { } script ? Engine.Evaluate(Window, script) : Engine.Evaluate(Window, code);
            _functions[source] = function;
        }

        return function;
    }

    /// <summary>Calls the function defined by a source text, and runs the microtasks.</summary>
    public JsValue Call(string functionSource, params ReadOnlySpan<JsValue> arguments) => CallFunction(GetFunction(functionSource), JsValue.Undefined, arguments);

    public JsValue CallFunction(JsValue function, JsValue thisValue, params ReadOnlySpan<JsValue> arguments)
    {
        try
        {
            return Engine.Call(Window, function, thisValue, arguments);
        }
        finally
        {
            Document?.DefaultView?.BrowsingContext?.EventLoop.PerformMicrotaskCheckpoint();
        }
    }

    /// <summary>
    /// Waits for a value: a promise is awaited (its rejection throws a <see cref="JsException"/>), any other value is returned.
    /// The tasks of the page run meanwhile (the thread of the browser context is not blocked).
    /// </summary>
    public async Task<JsValue> AwaitAsync(JsValue value, CancellationToken cancellationToken)
    {
        if (!value.IsObject || !value.AsObject().TryGetPromiseState(out var state, out var result, out _))
            return value;

        if (state == JsPromiseState.Fulfilled)
            return result;

        var completion = new TaskCompletionSource<JsValue>(TaskCreationOptions.RunContinuationsAsynchronously);
        var resolve = Realm.CreateFunction("", 1, (_, arguments) =>
        {
            completion.TrySetResult(arguments.Length > 0 ? arguments[0] : JsValue.Undefined);
            return JsValue.Undefined;
        });
        var reject = Realm.CreateFunction("", 1, (_, arguments) =>
        {
            completion.TrySetException(new JsException(arguments.Length > 0 ? arguments[0] : JsValue.Undefined));
            return JsValue.Undefined;
        });
        Call("(promise, resolve, reject) => { promise.then(resolve, reject); }", value, resolve, reject);
        var winner = await Task.WhenAny(completion.Task, _destroyed.Task, Task.Delay(Timeout.Infinite, cancellationToken)).ConfigureAwait(true);
        if (winner == completion.Task)
            return await completion.Task.ConfigureAwait(true);

        if (winner == _destroyed.Task)
            throw new ProtocolException("Execution context was destroyed.");

        throw new OperationCanceledException(cancellationToken);
    }

    public JsValue ParseJson(string json) => Call("(json) => JSON.parse(json)", json);

    /// <summary>Serializes a value with <c>JSON.stringify</c> (null for <c>undefined</c> and the functions).</summary>
    public string? Stringify(JsValue value)
    {
        var result = Call("(value) => JSON.stringify(value)", value);
        return result.IsString ? result.AsString() : null;
    }

    public JsValue ToJsValue(object? value) => Engine.ToJsValue(Window, value);

    public object? FromJsValue(JsValue value) => Engine.FromJsValue(Window, value);
}
