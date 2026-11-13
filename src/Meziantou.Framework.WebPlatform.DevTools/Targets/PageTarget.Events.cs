using System.Text.Json.Nodes;
using Meziantou.Framework.JavaScriptEngine;
using Meziantou.Framework.WebPlatform.DevTools.Protocol;
using Meziantou.Framework.WebPlatform.DevTools.Runtime;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Html;
using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.DevTools.Targets;

// The events of the documents of the page: console messages, exceptions, dialogs, file choosers and popups (on the thread of the context)
internal sealed partial class PageTarget
{
    private readonly Lock _dialogLock = new();
    private PendingDialog? _dialog;
    private int _nextExceptionId;

    internal void OnConsoleMessage(FrameState frame, ConsoleMessage message)
    {
        if (frame.MainWorld is not { } context)
            return;

        var type = message.Method switch
        {
            "warn" => "warning",
            "group" => "startGroup",
            "groupCollapsed" => "startGroupCollapsed",
            "groupEnd" => "endGroup",
            "timeLog" => "log",
            var method => method,
        };
        var timestamp = (double)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var callFrame = new JsonObject
        {
            ["functionName"] = "",
            ["scriptId"] = "0",
            ["url"] = message.Location?.Url ?? frame.Url,
            ["lineNumber"] = Math.Max(0, (message.Location?.LineNumber ?? 1) - 1),
            ["columnNumber"] = Math.Max(0, (message.Location?.ColumnNumber ?? 1) - 1),
        };
        Broadcast(session => session.RuntimeEnabled, "Runtime.consoleAPICalled", session =>
        {
            var args = new JsonArray();
            if (message.Arguments.Count == 0)
            {
                args.Add((JsonNode)new JsonObject { ["type"] = "string", ["value"] = message.Text });
            }

            foreach (var argument in message.Arguments)
            {
                JsonObject remoteObject;
                try
                {
                    remoteObject = RemoteObjects.Create(context, context.ToJsValue(argument), session.Objects, group: "console", returnByValue: false);
                }
                catch (Exception ex) when (ex is JsException or ProtocolException or InvalidOperationException)
                {
                    remoteObject = new JsonObject { ["type"] = "undefined" };
                }

                args.Add((JsonNode)remoteObject);
            }

            return new JsonObject
            {
                ["type"] = type,
                ["args"] = args,
                ["executionContextId"] = context.Id,
                ["timestamp"] = timestamp,
                ["stackTrace"] = new JsonObject { ["callFrames"] = new JsonArray(callFrame.DeepClone()) },
            };
        });
    }

    internal void OnUnhandledScriptError(FrameState frame, ScriptError error)
    {
        if (frame.MainWorld is not { } context)
            return;

        JsValue value;
        try
        {
            value = error.Error switch
            {
                JsException jsException => jsException.Value,
                JsValue jsValue => jsValue,
                _ => context.Call("(message) => new Error(message)", error.Message),
            };
        }
        catch (Exception ex) when (ex is JsException or ProtocolException)
        {
            return;
        }

        var exceptionId = Interlocked.Increment(ref _nextExceptionId);
        Broadcast(session => session.RuntimeEnabled, "Runtime.exceptionThrown", session =>
        {
            var details = RemoteObjects.CreateExceptionDetails(context, value, session.Objects, exceptionId);
            if (details["url"] is null)
            {
                details["url"] = error.Filename ?? frame.Url;
                details["lineNumber"] = Math.Max(0, error.LineNumber - 1);
                details["columnNumber"] = Math.Max(0, error.ColumnNumber - 1);
            }

            return new JsonObject { ["timestamp"] = (double)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), ["exceptionDetails"] = details };
        });
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Dialogs
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// A script shows a dialog: the clients are notified, and the script waits for the answer of a client
    /// (<c>Page.handleJavaScriptDialog</c>, handled on another thread): the thread of the context is blocked meanwhile.
    /// </summary>
    internal UserPromptResult OnUserPrompt(FrameState frame, UserPrompt prompt)
    {
        var defaultResult = prompt.Type == UserPromptType.BeforeUnload ? UserPromptResult.Accept() : UserPromptResult.Dismiss;
        if (!GetSessions().Any(session => session.PageEnabled))
            return defaultResult;

        var type = prompt.Type switch
        {
            UserPromptType.Alert => "alert",
            UserPromptType.Confirm => "confirm",
            UserPromptType.Prompt => "prompt",
            _ => "beforeunload",
        };
        var dialog = new PendingDialog();
        lock (_dialogLock)
        {
            _dialog = dialog;
        }

        Broadcast(session => session.PageEnabled, "Page.javascriptDialogOpening", new JsonObject
        {
            ["url"] = frame.Url,
            ["frameId"] = frame.FrameId,
            ["message"] = prompt.Message ?? "",
            ["type"] = type,
            ["hasBrowserHandler"] = false,
            ["defaultPrompt"] = prompt.DefaultValue ?? "",
        });

        (bool Accept, string? PromptText) answer;
        try
        {
            answer = dialog.Completion.Task.Wait(Timeout.Infinite, Browser.Connection.Closed) ? dialog.Completion.Task.Result : (false, null);
        }
        catch (OperationCanceledException)
        {
            answer = (prompt.Type == UserPromptType.BeforeUnload, null);
        }
        finally
        {
            lock (_dialogLock)
            {
                if (ReferenceEquals(_dialog, dialog))
                {
                    _dialog = null;
                }
            }
        }

        Broadcast(session => session.PageEnabled, "Page.javascriptDialogClosed", new JsonObject
        {
            ["frameId"] = frame.FrameId,
            ["result"] = answer.Accept,
            ["userInput"] = answer.PromptText ?? "",
        });

        if (!answer.Accept)
            return UserPromptResult.Dismiss;

        return prompt.Type == UserPromptType.Prompt ? UserPromptResult.Accept(answer.PromptText ?? prompt.DefaultValue ?? "") : UserPromptResult.Accept();
    }

    /// <summary>Answers the open dialog (<c>Page.handleJavaScriptDialog</c>, on any thread).</summary>
    public void HandleDialog(bool accept, string? promptText)
    {
        PendingDialog? dialog;
        lock (_dialogLock)
        {
            dialog = _dialog;
            _dialog = null;
        }

        if (dialog is null)
            throw new ProtocolException("No dialog is showing");

        dialog.Completion.TrySetResult((accept, promptText));
    }

    private void CancelDialog()
    {
        PendingDialog? dialog;
        lock (_dialogLock)
        {
            dialog = _dialog;
            _dialog = null;
        }

        dialog?.Completion.TrySetResult((false, null));
    }

    private sealed class PendingDialog
    {
        public TaskCompletionSource<(bool Accept, string? PromptText)> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    // ----------------------------------------------------------------------------------------------------------------
    // File choosers and popups
    // ----------------------------------------------------------------------------------------------------------------

    internal void OnFileChooser(FrameState frame, HtmlInputElement input)
    {
        Broadcast(session => session.InterceptFileChooser, "Page.fileChooserOpened", new JsonObject
        {
            ["frameId"] = frame.FrameId,
            ["mode"] = input.Multiple ? "selectMultiple" : "selectSingle",
            ["backendNodeId"] = Nodes.GetId(input),
        });
    }

    internal void OnWindowOpen(PageTarget popup)
    {
        Broadcast(session => session.PageEnabled, "Page.windowOpen", new JsonObject
        {
            ["url"] = popup.BrowsingContext?.Document.Url ?? "about:blank",
            ["windowName"] = popup.BrowsingContext?.Name ?? "",
            ["windowFeatures"] = new JsonArray(),
            ["userGesture"] = false,
        });
    }
}
