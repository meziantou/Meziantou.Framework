using Meziantou.Framework.JavaScriptEngine;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.JavaScript;

/// <summary>The host hooks of the engine (https://html.spec.whatwg.org/multipage/webappapis.html#javascript-specification-host-hooks).</summary>
internal sealed class JavaScriptHost(JavaScriptScriptEngine owner) : JsHost
{
    /// <summary>
    /// Whether the next compilation of a string is the code of the implementation or of the host (see
    /// <see cref="JavaScriptAdapter.Evaluate"/>), which no content security policy blocks: the check resets it.
    /// </summary>
    public bool SkipNextCompilationCheck { get; set; }

    /// <summary>https://html.spec.whatwg.org/multipage/webappapis.html#the-hostpromiserejectiontracker-implementation</summary>
    public override void PromiseRejectionTracker(JsEngine engine, JsObject promise, JsPromiseRejectionOperation operation) => owner.OnPromiseRejectionTracker(promise, operation);

    /// <summary>https://html.spec.whatwg.org/multipage/webappapis.html#hostgetcodeforeval(argument): the data of a <see cref="TrustedScript"/>.</summary>
    public override string? GetCodeForEval(JsEngine engine, JsValue argument)
    {
        if (argument.IsString)
            return argument.AsString();

        if (argument.TryGetObject() is { } obj && owner.Agent.CurrentRealm is { } realm && realm.Unwrap(obj) is TrustedScript trustedScript)
            return trustedScript.Data;

        return null;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/webappapis.html#hostensurecancompilestrings(realm,-parameterstrings,-bodystring,-codestring,-compilationtype,-parameterargs,-bodyarg)</summary>
    public override void EnsureCanCompileStrings(JsEngine engine, JsRealm realm, IReadOnlyList<string> parameterStrings, string bodyString, string codeString, JsCompilationType compilationType, IReadOnlyList<JsValue> parameterArgs, JsValue bodyArg)
    {
        if (SkipNextCompilationCheck)
        {
            SkipNextCompilationCheck = false;
            return;
        }

        owner.EnsureCanCompileStrings(realm, parameterStrings, bodyString, codeString, compilationType, parameterArgs, bodyArg);
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/webappapis.html#hostmakejobcallback: the incumbent global object and the
    /// active script, restored when the callback is called.
    /// </summary>
    public override JsJobCallback MakeJobCallback(JsEngine engine, JsObject callback) => new(callback, new JobCallbackState(owner.GetIncumbentGlobal(), engine.GetActiveScriptOrModule()));

    /// <summary>https://html.spec.whatwg.org/multipage/webappapis.html#hostcalljobcallback</summary>
    public override JsValue CallJobCallback(JsEngine engine, in JsJobCallback jobCallback, JsValue thisValue, ReadOnlySpan<JsValue> arguments)
    {
        if (jobCallback.HostDefined is not JobCallbackState state)
            return base.CallJobCallback(engine, jobCallback, thisValue, arguments);

        // Prepare to run a callback with the incumbent settings object (the backup incumbent settings object stack), and
        // push the script execution context of the active script
        owner.BackupIncumbentGlobals.Push(state.Incumbent);
        try
        {
            if (state.ActiveScript is null)
                return base.CallJobCallback(engine, jobCallback, thisValue, arguments);

            using var scriptContext = engine.EnterActiveScriptOrModule(state.ActiveScript);
            return base.CallJobCallback(engine, jobCallback, thisValue, arguments);
        }
        finally
        {
            owner.BackupIncumbentGlobals.Pop();
        }
    }

    private sealed record JobCallbackState(GlobalScope? Incumbent, JsScriptOrModule? ActiveScript);

    /// <summary>The exception thrown by a microtask queued by the host is reported to the global object of the current realm.</summary>
    public override void ReportJobException(JsEngine engine, JsException exception) => owner.ReportException(realm: null, exception);
}

/// <summary>The services of the script engine used by the WebIDL bindings.</summary>
internal sealed class WebIdlHost(JavaScriptScriptEngine engine) : IWebIdlHost
{
    public void ReportException(GlobalScope global, Exception exception) => global.ReportException(engine.ExtractErrorInformation(exception));

    public void OnCallbackInvoking() => engine.EnterJavaScript();

    public bool IsScriptRunning => engine.IsScriptRunning;

    /// <summary>Throws when a limit of the engine is exceeded (e.g. the timeout), while WebAssembly code runs.</summary>
    public void CheckInterrupt() => engine.Engine.CheckInterrupt();

    public void OnCallbackInvoked(GlobalScope global)
    {
        engine.ExitJavaScript();
        engine.CleanUpAfterRunningScript(global);
    }
}

/// <summary>
/// The [[HostDefined]] of the scripts and the modules run by the engine: the base URL and the fetch options of the script
/// (https://html.spec.whatwg.org/multipage/webappapis.html#concept-script).
/// </summary>
internal sealed class ScriptHostDefined
{
    /// <summary>Gets the base URL of the script, or null for the code of an event handler (it has no script: the base URL is the one of its document).</summary>
    public required string? BaseUrl { get; init; }

    /// <summary>Gets the fetch options of the script, or null.</summary>
    public ModuleFetchOptions? FetchOptions { get; init; }

    /// <summary>Gets whether the script is the code of an event handler (https://html.spec.whatwg.org/multipage/webappapis.html#getting-the-current-value-of-the-event-handler).</summary>
    public bool IsEventHandler { get; init; }

    /// <summary>Gets whether the script is a module.</summary>
    public bool IsModule { get; init; }

    /// <summary>Gets whether the script is the classic script of the global object of a worker (its URL is the referrer of no fetch).</summary>
    public bool IsGlobalScript { get; init; }
}
