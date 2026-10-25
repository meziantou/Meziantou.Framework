using System.Text.Json.Nodes;
using Meziantou.Framework.JavaScriptEngine;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.JavaScript;
using Meziantou.Framework.WebPlatform.Playwright.Protocol;

namespace Meziantou.Framework.WebPlatform.Playwright.Server;

/// <summary>
/// The execution context of a document of a frame (the main world of Playwright): the scripts of Playwright injected in it,
/// and the calls of functions in its realm. It is destroyed when the frame navigates to another document. It is used on the
/// thread of the browser context only.
/// </summary>
internal sealed class FrameExecutionContext
{
    private readonly Dictionary<string, JsValue> _functions = new(StringComparer.Ordinal);
    private readonly TaskCompletionSource _destroyed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Func<JsonObject> _injectedScriptOptions;
    private JsValue _injectedScript;
    private JsValue _utilityScript;
    private JsRealm? _realm;

    public FrameExecutionContext(Document document, JavaScriptScriptEngine engine, Func<JsonObject> injectedScriptOptions)
    {
        Document = document;
        Engine = engine;
        _injectedScriptOptions = injectedScriptOptions;
    }

    public Document Document { get; }

    public Window Window => Document.DefaultView ?? throw PlaywrightError.TargetClosed("Execution context was destroyed, most likely because of a navigation");

    public JavaScriptScriptEngine Engine { get; }

    public bool IsDestroyed => _destroyed.Task.IsCompleted;

    /// <summary>Gets a task completed when the context is destroyed.</summary>
    public Task Destroyed => _destroyed.Task;

    public void Destroy() => _destroyed.TrySetResult();

    private void EnsureNotDestroyed()
    {
        if (IsDestroyed || Document.DefaultView is null)
            throw new PlaywrightError("Execution context was destroyed, most likely because of a navigation");
    }

    /// <summary>Gets the injected script of Playwright (created on first use).</summary>
    public JsValue InjectedScript
    {
        get
        {
            EnsureNotDestroyed();
            if (_injectedScript.IsUndefined)
            {
                var factory = Engine.Evaluate(Window, PlaywrightScripts.InjectedScript);
                _injectedScript = CallFunction(factory, ParseJson(_injectedScriptOptions().ToJsonString()));
            }

            return _injectedScript;
        }
    }

    /// <summary>Gets the utility script of Playwright (created on first use).</summary>
    public JsValue UtilityScript
    {
        get
        {
            EnsureNotDestroyed();
            if (_utilityScript.IsUndefined)
            {
                var factory = Engine.Evaluate(Window, PlaywrightScripts.UtilityScript);
                _utilityScript = CallFunction(factory);
            }

            return _utilityScript;
        }
    }

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

    /// <summary>Returns the function defined by a source text (e.g. <c>(a, b) => a + b</c>), compiled once per context.</summary>
    public JsValue GetFunction(string source)
    {
        EnsureNotDestroyed();
        if (!_functions.TryGetValue(source, out var function))
        {
            function = Engine.Evaluate(Window, "(" + source + ")");
            _functions[source] = function;
        }

        return function;
    }

    /// <summary>Calls the function defined by a source text, and runs the microtasks.</summary>
    public JsValue Call(string functionSource, params ReadOnlySpan<JsValue> arguments) => CallFunction(GetFunction(functionSource), arguments);

    /// <summary>Calls the function defined by a source text with the injected script as first argument.</summary>
    public JsValue CallInjected(string functionSource, params ReadOnlySpan<JsValue> arguments)
    {
        var injectedScript = InjectedScript;
        var allArguments = new JsValue[arguments.Length + 1];
        allArguments[0] = injectedScript;
        arguments.CopyTo(allArguments.AsSpan(1));
        return CallFunction(GetFunction(functionSource), allArguments);
    }

    public JsValue CallFunction(JsValue function, params ReadOnlySpan<JsValue> arguments)
    {
        EnsureNotDestroyed();
        try
        {
            return Engine.Call(Window, function, JsValue.Undefined, arguments);
        }
        finally
        {
            Document.DefaultView?.BrowsingContext?.EventLoop.PerformMicrotaskCheckpoint();
        }
    }

    /// <summary>Calls a function, and waits for the promise it returns (the microtasks and the tasks of the page run meanwhile).</summary>
    public async Task<JsValue> CallAsync(string functionSource, Progress progress, params JsValue[] arguments)
    {
        var result = Call(functionSource, arguments);
        return await AwaitAsync(result, progress).ConfigureAwait(true);
    }

    /// <summary>Calls a function with the injected script as first argument, and waits for the promise it returns.</summary>
    public async Task<JsValue> CallInjectedAsync(string functionSource, Progress progress, params JsValue[] arguments)
    {
        var result = CallInjected(functionSource, arguments);
        return await AwaitAsync(result, progress).ConfigureAwait(true);
    }

    /// <summary>Waits for a value: a promise is awaited (its rejection throws a <see cref="JsException"/>), any other value is returned.</summary>
    public async Task<JsValue> AwaitAsync(JsValue value, Progress progress)
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
        var winner = await Task.WhenAny(completion.Task, _destroyed.Task, Task.Delay(Timeout.Infinite, progress.CancellationToken)).ConfigureAwait(true);
        if (winner == completion.Task)
            return await completion.Task.ConfigureAwait(true);

        if (winner == _destroyed.Task)
            throw new PlaywrightError("Execution context was destroyed, most likely because of a navigation");

        throw progress.GetCancellationError();
    }

    public JsValue ParseJson(string json) => Call("(json) => JSON.parse(json)", json);

    /// <summary>Serializes a value with <c>JSON.stringify</c> (null for <c>undefined</c>).</summary>
    public string? Stringify(JsValue value)
    {
        var result = Call("(value) => JSON.stringify(value)", value);
        return result.IsString ? result.AsString() : null;
    }

    public JsValue ToJsValue(object? value) => Engine.ToJsValue(Window, value);

    public object? FromJsValue(JsValue value) => Engine.FromJsValue(Window, value);

    /// <summary>Returns the value of a property of an object.</summary>
    public static JsValue GetProperty(JsValue value, string name) => value.IsObject ? value.AsObject().GetProperty(name) : JsValue.Undefined;

    /// <summary>Converts an exception thrown by a script to an error of the protocol (its message, name and stack).</summary>
    public PlaywrightError ToPlaywrightError(JsException exception)
    {
        var value = exception.Value;
        if (value.IsObject && !IsDestroyed)
        {
            try
            {
                var details = Call("(e) => [String(e && e.message !== undefined ? e.message : e), String(e && e.name || 'Error'), String(e && e.stack || '')]", value).AsObject();
                var message = details.GetProperty("0").AsString();
                var name = details.GetProperty("1").AsString();
                var stack = details.GetProperty("2").AsString();
                return new PlaywrightError(name == "Error" ? PlaywrightError.GenericName : name, message, exception) { ErrorStack = stack.Length > 0 ? stack : null };
            }
            catch (JsException)
            {
            }
        }

        return new PlaywrightError(exception.Message, exception);
    }
}
