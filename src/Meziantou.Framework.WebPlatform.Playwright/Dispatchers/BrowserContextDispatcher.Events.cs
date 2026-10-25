using System.Text.Json.Nodes;
using Meziantou.Framework.JavaScriptEngine;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Html;
using Meziantou.Framework.WebPlatform.Playwright.Protocol;
using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.Playwright.Dispatchers;

// The callbacks of the browsing contexts of the pages (they run on the thread of the context)
internal sealed partial class BrowserContextDispatcher
{
    private void OnBrowsingContextCreated(BrowsingContext browsingContext)
    {
        if (IsClosed)
            return;

        if (browsingContext.Parent is { } parent)
        {
            if (FindFrame(parent) is { } parentFrame)
            {
                parentFrame.Page.AttachFrame(browsingContext, parentFrame);
            }

            return;
        }

        if (browsingContext.IsAuxiliary)
        {
            // As Playwright, a popup is reported once its initial navigation commits (window.open(url) creates the popup, then
            // navigates it): its URL is the one of the navigation, and its load states are the ones of its document
            var page = RegisterPage(browsingContext, FindPage(browsingContext.CreatorBrowsingContext ?? browsingContext.Opener), report: false);
            _ = ReportPopupAsync(page);
        }
    }

    // The popups whose initial navigation ends without a document (e.g. a 204 response), or that do not navigate (window.open()
    // without URL) are reported when they have no ongoing navigation. The others are reported when their document is activated.
    private async Task ReportPopupAsync(PageDispatcher page)
    {
        // The navigation starts after the browsing context is created (in the task that created it)
        await Task.Yield();
        while (!page.IsReported && !page.IsClosed && page.BrowsingContext.IsNavigating)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10)).ConfigureAwait(true);
        }

        if (!page.IsClosed)
        {
            ReportPage(page);
        }
    }

    private void OnBrowsingContextDestroyed(BrowsingContext browsingContext)
    {
        if (FindFrame(browsingContext) is not { } frame)
            return;

        if (frame.ParentFrame is null)
        {
            frame.Page.OnBrowsingContextClosed();
        }
        else
        {
            frame.Page.DetachFrame(frame);
        }
    }

    private void OnDocumentActivated(BrowsingContext browsingContext, Document document) => FindFrame(browsingContext)?.OnDocumentActivated(document);

    private void OnSameDocumentNavigated(BrowsingContext browsingContext) => FindFrame(browsingContext)?.OnSameDocumentNavigated();

    private void OnConsoleMessage(GlobalScope global, ConsoleMessage message)
    {
        if (global is not Window { BrowsingContext: { } browsingContext } || FindFrame(browsingContext) is not { } frame)
            return;

        var page = frame.Page;
        var type = message.Method switch
        {
            "warn" => "warning",
            "group" => "startGroup",
            "groupCollapsed" => "startGroupCollapsed",
            "groupEnd" => "endGroup",
            "timeLog" => "log",
            var method => method,
        };
        var location = new JsonObject
        {
            ["url"] = message.Location?.Url ?? frame.Url,
            ["lineNumber"] = Math.Max(0, (message.Location?.LineNumber ?? 1) - 1),
            ["columnNumber"] = Math.Max(0, (message.Location?.ColumnNumber ?? 1) - 1),
        };
        var timestamp = (double)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        page.AddConsoleMessage(type, message.Text, location, timestamp);
        if (!IsSubscribed("console", page))
            return;

        var args = new JsonArray();
        var context = frame.GetExecutionContext();
        foreach (var argument in message.Arguments)
        {
            JsValue value;
            try
            {
                value = context.ToJsValue(argument);
            }
            catch (Exception ex) when (ex is JsException or PlaywrightError or InvalidOperationException)
            {
                value = JsValue.Undefined;
            }

            args.Add((JsonNode)frame.CreateHandle(context, value).ToReference());
        }

        SendEvent("console", new JsonObject
        {
            ["type"] = type,
            ["text"] = message.Text,
            ["args"] = args,
            ["location"] = location,
            ["timestamp"] = timestamp,
            ["page"] = page.ToReference(),
        });
    }

    private void OnUnhandledScriptError(GlobalScope global, ScriptError error)
    {
        if (global is not Window { BrowsingContext: { } browsingContext } || FindFrame(browsingContext) is not { } frame)
            return;

        var page = frame.Page;
        var playwrightError = error.Error is JsException jsException ? frame.GetExecutionContext().ToPlaywrightError(jsException)
            : error.Error is JsValue jsValue ? frame.GetExecutionContext().ToPlaywrightError(new JsException(jsValue))
            : new PlaywrightError(error.Message);
        var serialized = PlaywrightConnection.SerializeError(playwrightError);
        page.AddPageError(serialized);
        SendEvent("pageError", new JsonObject
        {
            ["error"] = serialized,
            ["page"] = page.ToReference(),
            ["location"] = new JsonObject
            {
                ["url"] = error.Filename ?? frame.Url,
                ["line"] = error.LineNumber,
                ["column"] = error.ColumnNumber,
            },
        });
    }

    private UserPromptResult OnUserPrompt(Window window, UserPrompt prompt)
    {
        var page = FindPage(window.BrowsingContext);
        var type = prompt.Type switch
        {
            UserPromptType.Alert => "alert",
            UserPromptType.Confirm => "confirm",
            UserPromptType.Prompt => "prompt",
            _ => "beforeunload",
        };

        // A dialog that nobody handles is closed at once: the unloading is accepted, the other dialogs are dismissed
        if (page is null || IsClosed || !IsSubscribed("dialog", page))
            return prompt.Type == UserPromptType.BeforeUnload ? UserPromptResult.Accept() : UserPromptResult.Dismiss;

        var dialog = new DialogDispatcher(this, page, type, prompt.Message, prompt.DefaultValue);
        SendEvent("dialog", new JsonObject { ["dialog"] = dialog.ToReference() });
        page.OpenDialogs.Add(dialog);
        try
        {
            // The script waits for the answer of the client: the thread of the context is blocked (as the scripts of a page
            // that shows a dialog)
            return dialog.WaitForResult(Connection.Closed);
        }
        finally
        {
            page.OpenDialogs.Remove(dialog);
            if (IsSubscribed("dialogClosed", page))
            {
                SendEvent("dialogClosed", new JsonObject { ["dialog"] = dialog.ToReference() });
            }
        }
    }

    private void OnShowFilePicker(HtmlInputElement input)
    {
        if (input.OwnerDocument?.DefaultView?.BrowsingContext is not { } browsingContext || FindFrame(browsingContext) is not { } frame)
            return;

        frame.Page.OnFileChooser(frame, input);
    }
}
