using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.Framework.JavaScriptEngine;
using Meziantou.Framework.WebPlatform.Playwright.Protocol;
using Meziantou.Framework.WebPlatform.Playwright.Server;

namespace Meziantou.Framework.WebPlatform.Playwright.Dispatchers;

// The actions of the locators (the frames resolve the selector, then act on the element)
internal sealed partial class FrameDispatcher
{
    /// <summary>
    /// Waits for the element that matches a selector, then runs an action on it, again when the element is detached from the
    /// document meanwhile (<c>_retryWithProgressIfNotConnected</c>).
    /// </summary>
    private async Task<T> RetryWithProgressIfNotConnectedAsync<T>(SelectorTarget target, bool waitForFrameVisible, Func<ElementReference, bool, Task<(ActionOutcome Outcome, T Value)>> action, Progress progress)
    {
        progress.AddLog("waiting for " + AsLocator(target.Selector));
        var result = await RetryWithBackoffAsync<(bool HasValue, T Value)>(progress, async () =>
        {
            var resolved = await CallOnSelectorAsync(target, """
                ({ injected, elements }) => {
                  const element = elements[0];
                  let log = '';
                  if (elements.length > 1)
                    log = `  locator resolved to ${elements.length} elements. Proceeding with the first one: ${injected.previewNode(elements[0])}`;
                  else if (element)
                    log = `  locator resolved to ${injected.previewNode(element)}`;
                  return { log, element };
                }
                """, JsValue.Undefined, progress).ConfigureAwait(true);
            if (resolved is null)
                return (false, default);

            var resolvedObject = resolved.Result.AsObject();
            if (resolvedObject.GetProperty("log") is { IsString: true } log && log.AsString().Length > 0)
            {
                progress.AddLog(log.AsString());
            }

            var element = resolvedObject.GetProperty("element");
            if (!element.IsObject)
                return (false, default);

            if (waitForFrameVisible && !resolved.FrameVisible)
            {
                progress.AddLog("  element is inside a hidden frame, retrying");
                return (false, default);
            }

            var (outcome, value) = await action(new ElementReference(resolved.Frame, resolved.Context, element), resolved.FrameVisible).ConfigureAwait(true);
            if (outcome.Kind == "error:notconnected")
            {
                progress.AddLog("element was detached from the DOM, retrying");
                return (false, default);
            }

            ElementActions.AssertDone(outcome);
            return (true, (true, value));
        }).ConfigureAwait(true);
        return result.Value;
    }

    private Task<bool> RetryActionOnSelectorAsync(SelectorTarget target, bool waitForFrameVisible, Func<ElementReference, bool, Task<ActionOutcome>> action, Progress progress)
        => RetryWithProgressIfNotConnectedAsync<bool>(target, waitForFrameVisible, async (element, frameVisible) => (await action(element, frameVisible).ConfigureAwait(true), true), progress);

    private async ValueTask<JsonNode?> InvokeActionAsync(string method, JsonElement parameters, Progress progress)
    {
        var engine = Context.Engine;
        switch (method)
        {
            case "click":
                await engine.InvokeAsync(() => RetryActionOnSelectorAsync(CreateTarget(parameters), true, (element, frameVisible) => ElementActions.ClickAsync(element, PointerActionOptions.Parse(parameters), frameVisible, progress), progress)).ConfigureAwait(false);
                return null;

            case "dblclick":
                await engine.InvokeAsync(() => RetryActionOnSelectorAsync(CreateTarget(parameters), true, (element, frameVisible) => ElementActions.DblClickAsync(element, PointerActionOptions.Parse(parameters), frameVisible, progress), progress)).ConfigureAwait(false);
                return null;

            case "hover":
                await engine.InvokeAsync(() => RetryActionOnSelectorAsync(CreateTarget(parameters), true, (element, frameVisible) => ElementActions.HoverAsync(element, PointerActionOptions.Parse(parameters), frameVisible, progress), progress)).ConfigureAwait(false);
                return null;

            case "tap":
                await engine.InvokeAsync(() => RetryActionOnSelectorAsync(CreateTarget(parameters), true, (element, frameVisible) => ElementActions.TapAsync(element, PointerActionOptions.Parse(parameters), frameVisible, progress), progress)).ConfigureAwait(false);
                return null;

            case "fill":
                await engine.InvokeAsync(() => RetryActionOnSelectorAsync(CreateTarget(parameters), true, (element, frameVisible) => ElementActions.FillAsync(element, parameters.GetString("value") ?? "", parameters.GetBoolean("force", defaultValue: false), frameVisible, progress), progress)).ConfigureAwait(false);
                return null;

            case "focus":
                await engine.InvokeAsync(() => RetryActionOnSelectorAsync(CreateTarget(parameters), false, (element, _) => ElementActions.FocusAsync(element, resetSelectionIfNotFocused: false, progress), progress)).ConfigureAwait(false);
                return null;

            case "blur":
                await engine.InvokeAsync(() => RetryActionOnSelectorAsync(CreateTarget(parameters), false, (element, _) => ElementActions.BlurAsync(element, progress), progress)).ConfigureAwait(false);
                return null;

            case "type":
                await engine.InvokeAsync(() => RetryActionOnSelectorAsync(CreateTarget(parameters), false, (element, _) => ElementActions.TypeAsync(element, parameters.GetString("text") ?? "", parameters.GetDouble("delay") ?? 0, progress), progress)).ConfigureAwait(false);
                return null;

            case "press":
                await engine.InvokeAsync(() => RetryActionOnSelectorAsync(CreateTarget(parameters), false, (element, _) => ElementActions.PressAsync(element, parameters.GetRequiredString("key"), parameters.GetDouble("delay") ?? 0, parameters.GetBoolean("noWaitAfter", defaultValue: false), progress), progress)).ConfigureAwait(false);
                return null;

            case "check":
            case "uncheck":
                await engine.InvokeAsync(() => RetryActionOnSelectorAsync(CreateTarget(parameters), true, (element, frameVisible) => ElementActions.SetCheckedAsync(element, method == "check", PointerActionOptions.Parse(parameters), frameVisible, progress), progress)).ConfigureAwait(false);
                return null;

            case "selectOption":
                return await engine.InvokeAsync(async () =>
                {
                    var values = await RetryWithProgressIfNotConnectedAsync(CreateTarget(parameters), true, async (element, frameVisible) =>
                    {
                        var optionsToSelect = CreateOptionsToSelect(element, parameters);
                        return await ElementActions.SelectOptionAsync(element, optionsToSelect, parameters.GetBoolean("force", defaultValue: false), frameVisible, progress).ConfigureAwait(true);
                    }, progress).ConfigureAwait(true);
                    return new JsonObject { ["values"] = values };
                }).ConfigureAwait(false);

            case "setInputFiles":
                await engine.InvokeAsync(() => RetryActionOnSelectorAsync(CreateTarget(parameters), false, (element, _) => ElementActions.SetInputFilesAsync(element, parameters, progress), progress)).ConfigureAwait(false);
                return null;

            case "dragAndDrop":
                await engine.InvokeAsync(() => DragAndDropAsync(parameters, progress)).ConfigureAwait(false);
                return null;

            default:
                throw new PlaywrightError($"Frame.{method} is not supported by the Playwright server of Meziantou.Framework.WebPlatform");
        }
    }

    /// <summary>The options of <c>selectOption</c>: the elements (handles of <c>option</c> elements) and the values, labels or indexes.</summary>
    internal JsValue CreateOptionsToSelect(ElementReference element, JsonElement parameters)
    {
        var context = element.Context;
        var options = new List<JsValue>();
        foreach (var reference in parameters.GetArray("elements"))
        {
            if (reference.GetString("guid") is { } guid && Connection.Find(guid) is JSHandleDispatcher handle)
            {
                options.Add(handle.Value);
            }
        }

        foreach (var option in parameters.GetArray("options"))
        {
            options.Add(context.ParseJson(option.GetRawText()));
        }

        return context.Call("(...options) => options", [.. options]);
    }

    private async Task DragAndDropAsync(JsonElement parameters, Progress progress)
    {
        var source = new SelectorTarget(parameters.GetRequiredString("source"), parameters.GetBoolean("strict") ?? IsStrictByDefault);
        var target = new SelectorTarget(parameters.GetRequiredString("target"), parameters.GetBoolean("strict") ?? IsStrictByDefault);
        var options = PointerActionOptions.Parse(parameters);
        await RetryActionOnSelectorAsync(source, true, (element, frameVisible) => ElementActions.RetryPointerActionAsync(element, "move and down", waitForEnabled: false, point =>
        {
            Page.Mouse.Move(point.X, point.Y, steps: 1);
            Page.Mouse.Down("left");
            return Task.CompletedTask;
        }, options with { Position = PointerActionOptions.Parse(parameters, "sourcePosition").Position }, frameVisible, progress, waitAfter: false), progress).ConfigureAwait(true);

        await RetryActionOnSelectorAsync(target, true, (element, frameVisible) => ElementActions.RetryPointerActionAsync(element, "move and up", waitForEnabled: false, point =>
        {
            Page.Mouse.Move(point.X, point.Y, options.Steps ?? 1);
            Page.Mouse.Up("left");
            return Task.CompletedTask;
        }, options with { Position = PointerActionOptions.Parse(parameters, "targetPosition").Position }, frameVisible, progress, skipActionPreChecks: true, waitAfter: false), progress).ConfigureAwait(true);
    }

    /// <summary>Waits for the navigation the frame started (a navigation request), until its document is committed.</summary>
    internal async Task WaitForPendingNavigationAsync(Progress progress)
    {
        if (_pendingNavigationRequest is null)
            return;

        var waiter = AddNavigationWaiter();
        try
        {
            await progress.RaceAsync(waiter.Task).ConfigureAwait(true);
        }
        catch (PlaywrightError) when (!progress.IsCanceled)
        {
        }
        finally
        {
            _navigationWaiters.Remove(waiter);
        }
    }
}
