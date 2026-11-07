using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Playwright.Protocol;

namespace Meziantou.Framework.WebPlatform.Playwright.Dispatchers;

/// <summary>A registration that the client can dispose (e.g. an init script, a binding).</summary>
internal sealed class DisposableDispatcher(Dispatcher parent, Action dispose) : Dispatcher(parent, "Disposable", "disposable", initializer: null)
{
    public override ValueTask<JsonNode?> InvokeAsync(string method, JsonElement parameters, Progress progress)
    {
        if (method != "dispose")
            return base.InvokeAsync(method, parameters, progress);

        dispose();
        Dispose();
        return ValueTask.FromResult<JsonNode?>(null);
    }
}

/// <summary>The tracing of a context: the traces are not recorded (the calls succeed, without trace).</summary>
internal sealed class TracingDispatcher(Dispatcher parent) : Dispatcher(parent, "Tracing", "tracing", initializer: null)
{
    public override ValueTask<JsonNode?> InvokeAsync(string method, JsonElement parameters, Progress progress)
    {
        return method switch
        {
            "tracingStart" or "tracingStop" or "tracingGroup" or "tracingGroupEnd" => ValueTask.FromResult<JsonNode?>(null),
            "tracingStartChunk" => ValueTask.FromResult<JsonNode?>(new JsonObject { ["traceName"] = parameters.GetString("name") ?? "trace" }),
            "tracingStopChunk" => ValueTask.FromResult<JsonNode?>(parameters.GetString("mode") == "entries" ? new JsonObject { ["entries"] = new JsonArray() } : new JsonObject()),
            _ => throw new PlaywrightError($"Tracing.{method} is not supported by the Playwright server of Meziantou.Framework.WebPlatform"),
        };
    }
}

/// <summary>A dialog shown by a page (<c>alert()</c>, <c>confirm()</c>, <c>prompt()</c>, <c>beforeunload</c>), answered by the client.</summary>
internal sealed class DialogDispatcher : Dispatcher
{
    private readonly TaskCompletionSource<UserPromptResult> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public DialogDispatcher(BrowserContextDispatcher context, PageDispatcher page, string type, string message, string defaultValue)
        : base(context, "Dialog", "dialog", new JsonObject { ["page"] = page.ToReference(), ["type"] = type, ["message"] = message, ["defaultValue"] = defaultValue })
    {
        DialogType = type;
    }

    public string DialogType { get; }

    public override ValueTask<JsonNode?> InvokeAsync(string method, JsonElement parameters, Progress progress)
    {
        switch (method)
        {
            case "accept":
                if (!_result.TrySetResult(UserPromptResult.Accept(parameters.GetString("promptText"))))
                    throw new PlaywrightError("Cannot accept dialog which is already handled!");

                return ValueTask.FromResult<JsonNode?>(null);

            case "dismiss":
                if (!_result.TrySetResult(UserPromptResult.Dismiss))
                    throw new PlaywrightError("Cannot dismiss dialog which is already handled!");

                return ValueTask.FromResult<JsonNode?>(null);

            default:
                return base.InvokeAsync(method, parameters, progress);
        }
    }

    /// <summary>Closes the dialog without answer (the page is closed): the unloading is accepted, the other dialogs are dismissed.</summary>
    public void Close() => _result.TrySetResult(DialogType == "beforeunload" ? UserPromptResult.Accept() : UserPromptResult.Dismiss);

    /// <summary>Blocks the thread of the page until the client answers (or the connection is closed).</summary>
    public UserPromptResult WaitForResult(CancellationToken cancellationToken)
    {
        try
        {
            return _result.Task.WaitAsync(cancellationToken).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            return UserPromptResult.Dismiss;
        }
    }
}

/// <summary>A call of a binding (<c>exposeBinding</c>) by a page: the client resolves or rejects it.</summary>
internal sealed class BindingCallDispatcher(FrameDispatcher frame, string name, JsonArray args) : Dispatcher(frame.Page, "BindingCall", "bindingCall", new JsonObject { ["frame"] = frame.ToReference(), ["name"] = name, ["args"] = args })
{
    private readonly TaskCompletionSource<JsonElement> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Gets the result of the call: the serialized argument of <c>resolve</c>, or a <see cref="PlaywrightError"/> for <c>reject</c>.</summary>
    public Task<JsonElement> Result => _result.Task;

    public override ValueTask<JsonNode?> InvokeAsync(string method, JsonElement parameters, Progress progress)
    {
        switch (method)
        {
            case "resolve":
                _result.TrySetResult(parameters.TryGet("result", out var result) ? result.Clone() : default);
                Dispose();
                return ValueTask.FromResult<JsonNode?>(null);

            case "reject":
                var error = parameters.TryGet("error", out var serializedError) && serializedError.TryGet("error", out var details)
                    ? new PlaywrightError(details.GetString("name") ?? PlaywrightError.GenericName, details.GetString("message") ?? "") { ErrorStack = details.GetString("stack") }
                    : new PlaywrightError("Binding call was rejected");
                _result.TrySetException(error);
                Dispose();
                return ValueTask.FromResult<JsonNode?>(null);

            default:
                return base.InvokeAsync(method, parameters, progress);
        }
    }

    public void Cancel() => _result.TrySetException(PlaywrightError.TargetClosed());
}
