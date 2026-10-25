using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.Framework.JavaScriptEngine;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.JavaScript;
using Meziantou.Framework.WebPlatform.Playwright.Dispatchers;
using Meziantou.Framework.WebPlatform.Playwright.Protocol;

namespace Meziantou.Framework.WebPlatform.Playwright.Server;

/// <summary>
/// The script engine of a browser context: before the scripts of each document run, it saves the built-ins used by the
/// scripts of Playwright, defines the bindings (<c>exposeBinding</c>) and runs the init scripts (<c>addInitScript</c>) of the
/// context and of the page.
/// </summary>
internal sealed class PlaywrightScriptEngine(BrowserContextDispatcher context, JavaScriptScriptEngineOptions options) : JavaScriptScriptEngine(options)
{
    private const string CreateDeferredFunction = "() => { let resolve, reject; const promise = new Promise((a, b) => { resolve = a; reject = b; }); return [promise, resolve, reject]; }";

    protected override void OnDocumentActivated(Document document)
    {
        base.OnDocumentActivated(document);
        if (document.DefaultView is not { } window)
            return;

        var page = context.FindPage(window.BrowsingContext);
        var frame = context.FindFrame(window.BrowsingContext);
        TryEvaluate(window, PlaywrightScripts.SaveGlobalsSnapshot);
        if (frame is not null)
        {
            foreach (var binding in context.GetBindings().Concat(page?.GetBindings() ?? []))
            {
                try
                {
                    InstallBinding(frame, document, binding);
                }
                catch (JsException)
                {
                }
            }
        }

        foreach (var script in context.GetInitScripts().Concat(page?.GetInitScripts() ?? []))
        {
            TryEvaluate(window, script.Source);
        }
    }

    private void TryEvaluate(Window window, JsPreparedScript script)
    {
        try
        {
            Evaluate(window, script);
        }
        catch (JsException)
        {
        }
    }

    private void TryEvaluate(Window window, string source)
    {
        try
        {
            Evaluate(window, source);
        }
        catch (JsException)
        {
        }
    }

    /// <summary>
    /// Defines a binding in a document: a global function that gives its arguments to the client (a <c>bindingCall</c> event)
    /// and returns a promise of the result of the client.
    /// </summary>
    public void InstallBinding(FrameDispatcher frame, Document document, BindingRegistration binding)
    {
        var window = document.DefaultView!;
        var defineFunction = Evaluate(window, "(name, host) => { globalThis[name] = (...args) => host(...args); }");
        var realm = ((JsFunction)defineFunction.AsObject()).Realm;
        var host = realm.CreateFunction(binding.Name, 0, (_, arguments) => OnBindingCalled(frame, document, binding, arguments));
        Call(window, defineFunction, JsValue.Undefined, binding.Name, host);
    }

    private JsValue OnBindingCalled(FrameDispatcher frame, Document document, BindingRegistration binding, ReadOnlySpan<JsValue> arguments)
    {
        var executionContext = frame.GetExecutionContext();
        if (!ReferenceEquals(executionContext.Document, document))
            throw new JsException(JsValue.FromString("The document of the binding was replaced"));

        var serializedArguments = new JsonArray();
        foreach (var argument in arguments)
        {
            serializedArguments.Add(Evaluation.SerializeValue(executionContext, argument));
        }

        var deferred = executionContext.Call(CreateDeferredFunction).AsObject();
        var promise = deferred.GetProperty("0");
        var resolve = deferred.GetProperty("1");
        var reject = deferred.GetProperty("2");

        var call = new BindingCallDispatcher(frame, binding.Name, serializedArguments);
        var target = (Dispatcher?)binding.Page ?? context;
        target.SendEvent("bindingCall", new JsonObject { ["binding"] = call.ToReference() });
        _ = CompleteBindingCallAsync(executionContext, call, resolve, reject);
        return promise;
    }

    private async Task CompleteBindingCallAsync(FrameExecutionContext executionContext, BindingCallDispatcher call, JsValue resolve, JsValue reject)
    {
        try
        {
            var result = await call.Result.ConfigureAwait(true);
            if (executionContext.IsDestroyed)
                return;

            // The serialized result is parsed by the utility script (an evaluation of the identity function)
            using var progress = Progress.CreateInternal();
            var value = await Evaluation.EvaluateHandleAsync(executionContext, "(value) => value", isFunction: true, result.ValueKind == JsonValueKind.Undefined ? default : result, call.Connection, progress).ConfigureAwait(true);
            executionContext.CallFunction(resolve, value);
        }
        catch (PlaywrightError ex)
        {
            if (!executionContext.IsDestroyed)
            {
                var error = executionContext.Call("(message, name, stack) => { const error = new Error(message); error.name = name; if (stack) error.stack = stack; return error; }", ex.Message, ex.ErrorName, ex.ErrorStack ?? "");
                executionContext.CallFunction(reject, error);
            }
        }
        catch (JsException)
        {
        }
    }
}
