using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.Framework.JavaScriptEngine;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Playwright.Protocol;
using Meziantou.Framework.WebPlatform.Playwright.Server;

namespace Meziantou.Framework.WebPlatform.Playwright.Dispatchers;

/// <summary>A handle to an element of a page (<c>ElementHandle</c>): the actions and the queries of the element.</summary>
internal sealed class ElementHandleDispatcher(FrameDispatcher frame, FrameExecutionContext context, JsValue value, string preview)
    : JSHandleDispatcher(frame, "ElementHandle", "handle", context, value, preview)
{
    public ElementReference Reference => new(Frame, Context, Value);

    private SelectorTarget ScopeTarget => new(":scope", Strict: false, Reference);

    protected override async ValueTask<JsonNode?> InvokeElementAsync(string method, JsonElement parameters, Progress progress)
    {
        var engine = BrowserContext.Engine;
        var element = Reference;
        switch (method)
        {
            case "click":
                await engine.InvokeAsync(async () => ElementActions.AssertDone(await ElementActions.ClickAsync(element, PointerActionOptions.Parse(parameters), frameVisible: true, progress).ConfigureAwait(true))).ConfigureAwait(false);
                return null;

            case "dblclick":
                await engine.InvokeAsync(async () => ElementActions.AssertDone(await ElementActions.DblClickAsync(element, PointerActionOptions.Parse(parameters), frameVisible: true, progress).ConfigureAwait(true))).ConfigureAwait(false);
                return null;

            case "hover":
                await engine.InvokeAsync(async () => ElementActions.AssertDone(await ElementActions.HoverAsync(element, PointerActionOptions.Parse(parameters), frameVisible: true, progress).ConfigureAwait(true))).ConfigureAwait(false);
                return null;

            case "tap":
                await engine.InvokeAsync(async () => ElementActions.AssertDone(await ElementActions.TapAsync(element, PointerActionOptions.Parse(parameters), frameVisible: true, progress).ConfigureAwait(true))).ConfigureAwait(false);
                return null;

            case "fill":
                await engine.InvokeAsync(async () => ElementActions.AssertDone(await ElementActions.FillAsync(element, parameters.GetString("value") ?? "", parameters.GetBoolean("force", defaultValue: false), frameVisible: true, progress).ConfigureAwait(true))).ConfigureAwait(false);
                return null;

            case "focus":
                await engine.InvokeAsync(async () => ElementActions.AssertDone(await ElementActions.FocusAsync(element, resetSelectionIfNotFocused: false, progress).ConfigureAwait(true))).ConfigureAwait(false);
                return null;

            case "type":
                await engine.InvokeAsync(async () => ElementActions.AssertDone(await ElementActions.TypeAsync(element, parameters.GetString("text") ?? "", parameters.GetDouble("delay") ?? 0, progress).ConfigureAwait(true))).ConfigureAwait(false);
                return null;

            case "press":
                await engine.InvokeAsync(async () => ElementActions.AssertDone(await ElementActions.PressAsync(element, parameters.GetRequiredString("key"), parameters.GetDouble("delay") ?? 0, parameters.GetBoolean("noWaitAfter", defaultValue: false), progress).ConfigureAwait(true))).ConfigureAwait(false);
                return null;

            case "check":
            case "uncheck":
                await engine.InvokeAsync(async () => ElementActions.AssertDone(await ElementActions.SetCheckedAsync(element, method == "check", PointerActionOptions.Parse(parameters), frameVisible: true, progress).ConfigureAwait(true))).ConfigureAwait(false);
                return null;

            case "selectOption":
                return await engine.InvokeAsync(async () =>
                {
                    var (outcome, values) = await ElementActions.SelectOptionAsync(element, Frame.CreateOptionsToSelect(element, parameters), parameters.GetBoolean("force", defaultValue: false), frameVisible: true, progress).ConfigureAwait(true);
                    ElementActions.AssertDone(outcome);
                    return new JsonObject { ["values"] = values };
                }).ConfigureAwait(false);

            case "setInputFiles":
                await engine.InvokeAsync(async () => ElementActions.AssertDone(await ElementActions.SetInputFilesAsync(element, parameters, progress).ConfigureAwait(true))).ConfigureAwait(false);
                return null;

            case "selectText":
                await engine.InvokeAsync(async () => ElementActions.AssertDone(await ElementActions.SelectTextAsync(element, parameters.GetBoolean("force", defaultValue: false), progress).ConfigureAwait(true))).ConfigureAwait(false);
                return null;

            case "scrollIntoViewIfNeeded":
                await engine.InvokeAsync(async () => ElementActions.AssertDone(await ElementActions.ScrollIntoViewIfNeededAsync(element, progress).ConfigureAwait(true))).ConfigureAwait(false);
                return null;

            case "waitForElementState":
                await engine.InvokeAsync(async () => ElementActions.AssertDone(await ElementActions.WaitForElementStateAsync(element, parameters.GetRequiredString("state"), progress).ConfigureAwait(true))).ConfigureAwait(false);
                return null;

            case "boundingBox":
                return await engine.InvokeAsync(() =>
                {
                    var result = new JsonObject();
                    if (ElementActions.GetBoundingBox(element) is { } box)
                    {
                        result["value"] = box;
                    }

                    return result;
                }).ConfigureAwait(false);

            case "screenshot":
                return await engine.InvokeAsync(async () =>
                {
                    ElementActions.AssertDone(await ElementActions.ScrollIntoViewIfNeededAsync(element, progress).ConfigureAwait(true));
                    if (Context.FromJsValue(Value) is not Element domElement)
                        throw new PlaywrightError("Node is not an element");

                    var screenshot = await Screenshotter.TakeElementScreenshotAsync(Frame.Page, domElement, parameters, progress).ConfigureAwait(true);
                    return new JsonObject { ["binary"] = Convert.ToBase64String(screenshot) };
                }).ConfigureAwait(false);

            case "contentFrame":
                return await engine.InvokeAsync(() =>
                {
                    var result = new JsonObject();
                    var browsingContext = Context.FromJsValue(Value) switch
                    {
                        HtmlIFrameElement iframe => iframe.ContentWindow?.BrowsingContext,
                        HtmlFrameElement frameElement => frameElement.ContentWindow?.BrowsingContext,
                        _ => null,
                    };
                    if (BrowserContext.FindFrame(browsingContext) is { } contentFrame)
                    {
                        result["frame"] = contentFrame.ToReference();
                    }

                    return result;
                }).ConfigureAwait(false);

            case "ownerFrame":
                return await engine.InvokeAsync(() =>
                {
                    var result = new JsonObject();
                    if (Context.FromJsValue(Value) is Node node && BrowserContext.FindFrame(node.OwnerDocument?.DefaultView?.BrowsingContext ?? (node as Document)?.DefaultView?.BrowsingContext) is { } ownerFrame)
                    {
                        result["frame"] = ownerFrame.ToReference();
                    }

                    return result;
                }).ConfigureAwait(false);

            case "dispatchEvent":
            case "getAttribute":
            case "inputValue":
            case "textContent":
            case "innerText":
            case "innerHTML":
            case "isVisible":
            case "isHidden":
            case "isEnabled":
            case "isDisabled":
            case "isEditable":
            case "isChecked":
                return await Frame.InvokeOnScopeAsync(method, parameters, ScopeTarget, progress).ConfigureAwait(false);

            case "querySelector":
                return await engine.InvokeAsync(async () =>
                {
                    var target = new SelectorTarget(parameters.GetRequiredString("selector"), parameters.GetBoolean("strict") ?? false, element);
                    var resolved = await Frame.CallOnSelectorAsync(target, "({ elements }) => elements[0]", JsValue.Undefined, progress).ConfigureAwait(true);
                    var result = new JsonObject();
                    if (resolved is not null && resolved.Result.IsObject)
                    {
                        result["element"] = resolved.Frame.CreateHandle(resolved.Context, resolved.Result).ToReference();
                    }

                    return result;
                }).ConfigureAwait(false);

            case "querySelectorAll":
                return await engine.InvokeAsync(async () =>
                {
                    var elements = await Frame.QueryAllElementsAsync(parameters.GetRequiredString("selector"), progress, element).ConfigureAwait(true);
                    return new JsonObject { ["elements"] = new JsonArray([.. elements.Select(e => (JsonNode)Frame.CreateHandle(Context, e).ToReference())]) };
                }).ConfigureAwait(false);

            case "evalOnSelector":
                return await engine.InvokeAsync(async () =>
                {
                    var target = new SelectorTarget(parameters.GetRequiredString("selector"), parameters.GetBoolean("strict") ?? false, element);
                    var resolved = await Frame.CallOnSelectorAsync(target, "({ elements }) => elements[0]", JsValue.Undefined, progress).ConfigureAwait(true);
                    if (resolved is null || !resolved.Result.IsObject)
                        throw new PlaywrightError($"Failed to find element matching selector \"{target.Selector}\"");

                    var handle = resolved.Frame.CreateHandle(resolved.Context, resolved.Result);
                    try
                    {
                        return new JsonObject { ["value"] = await Evaluation.EvaluateAsync(resolved.Context, parameters.GetRequiredString("expression"), parameters.GetBoolean("isFunction"), GetArgument(parameters), Connection, progress, handle).ConfigureAwait(true) };
                    }
                    finally
                    {
                        handle.Dispose();
                    }
                }).ConfigureAwait(false);

            case "evalOnSelectorAll":
                return await engine.InvokeAsync(async () =>
                {
                    var elements = await Frame.QueryAllElementsAsync(parameters.GetRequiredString("selector"), progress, element).ConfigureAwait(true);
                    var array = Context.Call("(...elements) => elements", [.. elements]);
                    var handle = new JSHandleDispatcher(Frame, Context, array, "JSHandle@array");
                    try
                    {
                        return new JsonObject { ["value"] = await Evaluation.EvaluateAsync(Context, parameters.GetRequiredString("expression"), parameters.GetBoolean("isFunction"), GetArgument(parameters), Connection, progress, handle).ConfigureAwait(true) };
                    }
                    finally
                    {
                        handle.Dispose();
                    }
                }).ConfigureAwait(false);

            case "waitForSelector":
                return await engine.InvokeAsync(async () =>
                {
                    var target = new SelectorTarget(parameters.GetRequiredString("selector"), parameters.GetBoolean("strict") ?? false, element);
                    var found = await Frame.WaitForSelectorAsync(target, parameters.GetString("state") ?? "visible", omitReturnValue: false, progress).ConfigureAwait(true);
                    var result = new JsonObject();
                    if (found is not null)
                    {
                        result["element"] = found.Frame.CreateHandle(found.Context, found.Value).ToReference();
                    }

                    return result;
                }).ConfigureAwait(false);

            default:
                return await base.InvokeElementAsync(method, parameters, progress).ConfigureAwait(false);
        }
    }
}
