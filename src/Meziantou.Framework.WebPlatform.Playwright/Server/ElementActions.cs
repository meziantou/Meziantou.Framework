using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.Framework.JavaScriptEngine;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Playwright.Dispatchers;
using Meziantou.Framework.WebPlatform.Playwright.Protocol;

namespace Meziantou.Framework.WebPlatform.Playwright.Server;

/// <summary>The outcome of an attempt of an action on an element (the results of <c>dom.ts</c> of the Playwright server).</summary>
internal sealed record ActionOutcome(string Kind, string? Detail = null)
{
    public static ActionOutcome Done { get; } = new("done");

    public static ActionOutcome NotConnected { get; } = new("error:notconnected");

    public bool IsDone => Kind == "done";

    /// <summary>Converts the value returned by the injected script: <c>undefined</c> (the checks passed), a string, or an object with <c>missingState</c> or <c>hitTargetDescription</c>.</summary>
    public static ActionOutcome? FromJsValue(JsValue value)
    {
        if (value.IsUndefined || value.IsNull)
            return null;

        if (value.IsString)
            return new ActionOutcome(value.AsString());

        if (value.IsObject)
        {
            var obj = value.AsObject();
            if (obj.GetProperty("missingState") is { IsString: true } missingState)
                return new ActionOutcome("missingState", missingState.AsString());

            if (obj.GetProperty("hitTargetDescription") is { IsString: true } hitTarget)
                return new ActionOutcome("hitTarget", hitTarget.AsString());
        }

        return null;
    }
}

/// <summary>The options of a pointer action (click, hover, tap...).</summary>
internal sealed record PointerActionOptions
{
    public bool Force { get; init; }

    public bool Trial { get; init; }

    public bool NoWaitAfter { get; init; }

    public (double X, double Y)? Position { get; init; }

    public string? Scroll { get; init; }

    public IReadOnlyList<string>? Modifiers { get; init; }

    public string Button { get; init; } = "left";

    public int ClickCount { get; init; } = 1;

    public double Delay { get; init; }

    public int? Steps { get; init; }

    public static PointerActionOptions Parse(JsonElement parameters, string positionName = "position") => new()
    {
        Force = parameters.GetBoolean("force", defaultValue: false),
        Trial = parameters.GetBoolean("trial", defaultValue: false),
        NoWaitAfter = parameters.GetBoolean("noWaitAfter", defaultValue: false),
        Position = parameters.TryGet(positionName, out var position) ? (position.GetDouble("x") ?? 0, position.GetDouble("y") ?? 0) : null,
        Scroll = parameters.GetString("scroll"),
        Modifiers = parameters.TryGet("modifiers", out _) ? [.. parameters.GetArray("modifiers").Select(m => m.GetString()!)] : null,
        Button = parameters.GetString("button") ?? "left",
        ClickCount = parameters.GetInt32("clickCount") ?? 1,
        Delay = parameters.GetDouble("delay") ?? 0,
        Steps = parameters.GetInt32("steps"),
    };
}

/// <summary>
/// The actions on the elements (<c>dom.ts</c> of the Playwright server): each attempt waits for the actionability checks
/// (visible, enabled, stable, editable...), scrolls the element into view, computes the point of the action, checks that the
/// element receives the input at that point, then injects the input of the user. The attempts are retried with a backoff.
/// </summary>
internal static class ElementActions
{
    private static readonly int[] RetryWaitTimes = [0, 20, 100, 100, 500];

    /// <summary>Calls a function of the injected script with the node (<c>evaluateInUtility</c>): a destroyed context is <c>error:notconnected</c>.</summary>
    public static async Task<JsValue> EvaluateInUtilityAsync(ElementReference element, string functionSource, Progress progress, params JsValue[] arguments)
    {
        if (element.Context.IsDestroyed)
            return "error:notconnected";

        try
        {
            var allArguments = new JsValue[arguments.Length + 1];
            allArguments[0] = element.Value;
            arguments.CopyTo(allArguments, 1);
            return await element.Context.CallInjectedAsync(functionSource, progress, allArguments).ConfigureAwait(true);
        }
        catch (JsException ex)
        {
            var error = element.Context.ToPlaywrightError(ex);
            throw new PlaywrightError(error.ErrorName, error.Message, ex) { ErrorStack = error.ErrorStack, IsNonRetriable = true };
        }
        catch (PlaywrightError ex) when (!ex.IsNonRetriable && element.Context.IsDestroyed)
        {
            return "error:notconnected";
        }
    }

    /// <summary>Throws when the outcome of an action is not "done" (<c>assertDone</c>, <c>throwRetargetableDOMError</c>).</summary>
    public static void AssertDone(ActionOutcome outcome)
    {
        if (outcome.IsDone)
            return;

        if (outcome.Kind == "error:notconnected")
            throw new PlaywrightError("Element is not attached to the DOM");

        throw new PlaywrightError("Unexpected result: " + outcome.Kind);
    }

    /// <summary>Retries an action until it is done (<c>_retryAction</c>): the outcomes that describe a missing state are logged and retried.</summary>
    public static async Task<ActionOutcome> RetryActionAsync(string actionName, Func<int, Task<ActionOutcome>> action, bool force, bool trial, Progress progress)
    {
        var retry = 0;
        while (true)
        {
            if (retry > 0)
            {
                progress.AddLog($"retrying {actionName} action{(trial ? " (trial run)" : "")}");
                var timeout = RetryWaitTimes[Math.Min(retry - 1, RetryWaitTimes.Length - 1)];
                if (timeout > 0)
                {
                    progress.AddLog($"  waiting {timeout.ToString(CultureInfo.InvariantCulture)}ms");
                    await progress.WaitAsync(TimeSpan.FromMilliseconds(timeout)).ConfigureAwait(true);
                }
            }
            else
            {
                progress.AddLog($"attempting {actionName} action{(trial ? " (trial run)" : "")}");
            }

            progress.ThrowIfCanceled();
            var result = await action(retry).ConfigureAwait(true);
            retry++;
            switch (result.Kind)
            {
                case "error:notvisible":
                    if (force)
                        throw new PlaywrightError("Element is not visible") { IsNonRetriable = true };

                    progress.AddLog("  element is not visible");
                    continue;

                case "error:notinviewport":
                    if (force)
                        throw new PlaywrightError("Element is outside of the viewport") { IsNonRetriable = true };

                    progress.AddLog("  element is outside of the viewport");
                    continue;

                case "error:optionsnotfound":
                    progress.AddLog("  did not find some options");
                    continue;

                case "error:optionnotenabled":
                    progress.AddLog("  option being selected is not enabled");
                    continue;

                case "hitTarget":
                    progress.AddLog($"  {result.Detail} intercepts pointer events");
                    continue;

                case "missingState":
                    progress.AddLog($"  element is not {result.Detail}");
                    continue;

                default:
                    return result;
            }
        }
    }

    /// <summary>A pointer action (<c>_retryPointerAction</c>): the element is scrolled into view, then the input is performed at the point of the action.</summary>
    public static Task<ActionOutcome> RetryPointerActionAsync(ElementReference element, string name, bool waitForEnabled, Func<(double X, double Y), Task> perform, PointerActionOptions options, bool frameVisible, Progress progress, bool skipActionPreChecks = false, bool waitAfter = true)
    {
        _ = skipActionPreChecks;
        return RetryActionAsync(name, retry =>
        {
            // The scroll alignments of the retries, as Playwright
            (string Block, string Inline)? forceScroll = (retry % 4) switch
            {
                1 => ("end", "end"),
                2 => ("center", "center"),
                3 => ("start", "start"),
                _ => null,
            };
            return PerformPointerActionAsync(element, name, waitForEnabled, perform, forceScroll, options, frameVisible, waitAfter, progress);
        }, options.Force, options.Trial, progress);
    }

    private static async Task<ActionOutcome> PerformPointerActionAsync(ElementReference element, string name, bool waitForEnabled, Func<(double X, double Y), Task> perform, (string Block, string Inline)? forceScroll, PointerActionOptions options, bool frameVisible, bool waitAfter, Progress progress)
    {
        async Task<ActionOutcome> ScrollIntoViewAsync()
        {
            if (options.Scroll == "none")
                return ActionOutcome.Done;

            if (forceScroll is { } scroll)
            {
                var result = await EvaluateInUtilityAsync(element, "(injected, node, block, inline) => { if (node.nodeType === 1) node.scrollIntoView({ block, inline, behavior: 'instant' }); return 'done'; }", progress, scroll.Block, scroll.Inline).ConfigureAwait(true);
                return ActionOutcome.FromJsValue(result) ?? ActionOutcome.Done;
            }

            return ScrollRectIntoViewIfNeeded(element);
        }

        if (element.Frame.ParentFrame is not null)
        {
            try
            {
                await ScrollIntoViewAsync().ConfigureAwait(true);
            }
            catch (PlaywrightError)
            {
            }
        }

        if (!options.Force)
        {
            var states = waitForEnabled ? "visible, enabled and stable" : "visible and stable";
            progress.AddLog($"  waiting for element to be {states}");
            var checkResult = await EvaluateInUtilityAsync(element, "(injected, node, waitForEnabled, frameVisible) => injected.checkElementStates(node, waitForEnabled ? ['visible', 'enabled', 'stable'] : ['visible', 'stable'], frameVisible)", progress, waitForEnabled, frameVisible).ConfigureAwait(true);
            if (ActionOutcome.FromJsValue(checkResult) is { } failed)
                return failed;

            progress.AddLog($"  element is {states}");
        }

        progress.AddLog("  scrolling into view if needed");
        var scrolled = await ScrollIntoViewAsync().ConfigureAwait(true);
        if (!scrolled.IsDone)
            return scrolled;

        progress.AddLog("  done scrolling");
        var (pointOutcome, point) = options.Position is { } position ? GetOffsetPoint(element, position) : GetClickablePoint(element);
        if (pointOutcome is not null)
            return pointOutcome;

        point = (Math.Round(point.X * 100, MidpointRounding.AwayFromZero) / 100, Math.Round(point.Y * 100, MidpointRounding.AwayFromZero) / 100);
        JsValue interceptor = JsValue.Undefined;
        if (options.Force)
        {
            progress.AddLog("  forcing action");
        }
        else
        {
            var (frameOutcome, framePoint) = await CheckFrameIsHitTargetAsync(element, point, progress).ConfigureAwait(true);
            if (frameOutcome is not null)
                return frameOutcome;

            var actionType = name switch
            {
                "move and up" => "drag",
                "hover" or "tap" => name,
                _ => "mouse",
            };
            var hitPoint = framePoint is { } p ? element.Context.ParseJson(new JsonObject { ["x"] = p.X, ["y"] = p.Y }.ToJsonString()) : JsValue.Undefined;
            var handle = await EvaluateInUtilityAsync(element, "(injected, node, actionType, hitPoint, trial) => injected.setupHitTargetInterceptor(node, actionType, hitPoint, trial)", progress, actionType, hitPoint, options.Trial).ConfigureAwait(true);
            if (handle.IsString)
                return handle.AsString() == "error:notconnected" ? ActionOutcome.NotConnected : new ActionOutcome("hitTarget", handle.AsString());

            interceptor = handle;
        }

        var page = element.Frame.Page;
        IReadOnlyList<string>? restoreModifiers = null;
        if (options.Modifiers is { } modifiers)
        {
            restoreModifiers = page.Keyboard.EnsureModifiers(modifiers);
        }

        progress.AddLog($"  performing {name} action");
        var navigationWaiter = waitAfter && !options.NoWaitAfter ? page.StartNavigationWatcher() : null;
        try
        {
            if (!options.Trial)
            {
                await perform(point).ConfigureAwait(true);
            }

            if (restoreModifiers is not null)
            {
                page.Keyboard.EnsureModifiers(restoreModifiers);
            }

            if (interceptor.IsObject && !element.Context.IsDestroyed)
            {
                JsValue stopResult;
                try
                {
                    stopResult = element.Context.Call("(handle) => handle.stop()", interceptor);
                }
                catch (JsException)
                {
                    stopResult = "done";
                }

                if (stopResult.IsString && stopResult.AsString() != "done")
                    return new ActionOutcome("hitTarget", stopResult.AsString());
            }

            progress.AddLog($"  {(options.Trial ? "trial " : "")}{name} action done");
            if (navigationWaiter is not null)
            {
                progress.AddLog("  waiting for scheduled navigations to finish");
                await navigationWaiter.WaitAsync(progress).ConfigureAwait(true);
                progress.AddLog("  navigations have finished");
            }

            return ActionOutcome.Done;
        }
        finally
        {
            navigationWaiter?.Dispose();
        }
    }

    /// <summary>Scrolls the element into view when it is not visible in the viewport (<c>DOM.scrollIntoViewIfNeeded</c> of Chromium).</summary>
    public static ActionOutcome ScrollRectIntoViewIfNeeded(ElementReference element)
    {
        if (element.Context.IsDestroyed || element.Context.FromJsValue(element.Value) is not Element domElement || !domElement.IsConnected)
            return ActionOutcome.NotConnected;

        var rect = domElement.GetBoundingClientRect();
        if (rect.Width == 0 && rect.Height == 0 && domElement.GetClientRects().Length == 0)
            return new ActionOutcome("error:notvisible");

        var window = domElement.OwnerDocument?.DefaultView;
        if (window is null)
            return ActionOutcome.NotConnected;

        var inViewport = rect.Left >= 0 && rect.Top >= 0 && rect.Right <= window.InnerWidth && rect.Bottom <= window.InnerHeight;
        if (!inViewport)
        {
            _ = domElement.ScrollIntoView(new ScrollIntoViewOptions { Block = ScrollLogicalPosition.Center, Inline = ScrollLogicalPosition.Center, Behavior = ScrollBehavior.Instant });
        }

        return ActionOutcome.Done;
    }

    /// <summary>The middle of the first box of the element that is in the viewport, in the coordinates of the top-level viewport (<c>_clickablePoint</c>).</summary>
    private static (ActionOutcome? Outcome, (double X, double Y) Point) GetClickablePoint(ElementReference element)
    {
        if (element.Context.IsDestroyed || element.Context.FromJsValue(element.Value) is not Element domElement || !domElement.IsConnected)
            return (ActionOutcome.NotConnected, default);

        var quads = GetContentQuads(domElement);
        if (quads.Count == 0)
            return (new ActionOutcome("error:notvisible"), default);

        var viewport = element.Frame.Page.BrowsingContext.Viewport;
        foreach (var quad in quads)
        {
            var clipped = quad.Select(p => (X: Math.Clamp(p.X, 0, viewport.Width), Y: Math.Clamp(p.Y, 0, viewport.Height))).ToArray();
            if (ComputeArea(clipped) > 0.99)
                return (null, (clipped.Average(p => p.X), clipped.Average(p => p.Y)));
        }

        return (new ActionOutcome("error:notinviewport"), default);

        static double ComputeArea((double X, double Y)[] quad)
        {
            var area = 0d;
            for (var i = 0; i < quad.Length; i++)
            {
                var p1 = quad[i];
                var p2 = quad[(i + 1) % quad.Length];
                area += ((p1.X * p2.Y) - (p2.X * p1.Y)) / 2;
            }

            return Math.Abs(area);
        }
    }

    /// <summary>The border boxes of the element, in the coordinates of the top-level viewport (<c>DOM.getContentQuads</c>).</summary>
    public static List<(double X, double Y)[]> GetContentQuads(Element element)
    {
        var result = new List<(double X, double Y)[]>();
        var document = element.OwnerDocument!;
        foreach (var rect in element.GetClientRects())
        {
            if (rect.Width <= 0 && rect.Height <= 0)
                continue;

            result.Add(
            [
                UserInput.MapToTopLevel(document, rect.Left, rect.Top),
                UserInput.MapToTopLevel(document, rect.Right, rect.Top),
                UserInput.MapToTopLevel(document, rect.Right, rect.Bottom),
                UserInput.MapToTopLevel(document, rect.Left, rect.Bottom),
            ]);
        }

        return result;
    }

    /// <summary>The bounding box of the element in the coordinates of the top-level viewport, or null when it is not rendered.</summary>
    public static JsonObject? GetBoundingBox(ElementReference element)
    {
        if (element.Context.IsDestroyed || element.Context.FromJsValue(element.Value) is not Element domElement || !domElement.IsConnected)
            return null;

        var quads = GetContentQuads(domElement);
        if (quads.Count == 0)
            return null;

        var points = quads.SelectMany(quad => quad).ToArray();
        var minX = points.Min(p => p.X);
        var minY = points.Min(p => p.Y);
        return new JsonObject
        {
            ["x"] = minX,
            ["y"] = minY,
            ["width"] = points.Max(p => p.X) - minX,
            ["height"] = points.Max(p => p.Y) - minY,
        };
    }

    /// <summary>A point relative to the padding box of the element (the <c>position</c> option, <c>_offsetPoint</c>).</summary>
    private static (ActionOutcome? Outcome, (double X, double Y) Point) GetOffsetPoint(ElementReference element, (double X, double Y) offset)
    {
        if (element.Context.IsDestroyed || element.Context.FromJsValue(element.Value) is not Element domElement || !domElement.IsConnected)
            return (ActionOutcome.NotConnected, default);

        if (GetBoundingBox(element) is not { } box)
            return (new ActionOutcome("error:notvisible"), default);

        var border = element.Context.CallInjected("(injected, node) => injected.getElementBorderWidth(node)", element.Value);
        var left = border.IsObject ? border.AsObject().GetProperty("left").AsNumber() : 0;
        var top = border.IsObject ? border.AsObject().GetProperty("top").AsNumber() : 0;
        _ = domElement;
        return (null, (box["x"]!.GetValue<double>() + left + offset.X, box["y"]!.GetValue<double>() + top + offset.Y));
    }

    /// <summary>
    /// Checks that the containers of the frames of the element receive the input at the point (<c>_checkFrameIsHitTarget</c>),
    /// and returns the point in the coordinates of the frame of the element (null for a transformed iframe).
    /// </summary>
    private static async Task<(ActionOutcome? Outcome, (double X, double Y)? FramePoint)> CheckFrameIsHitTargetAsync(ElementReference element, (double X, double Y) point, Progress progress)
    {
        var frame = element.Frame;
        var data = new List<(FrameDispatcher Frame, ElementReference? FrameElement, (double X, double Y) PointInFrame)>();
        while (frame.ParentFrame is { } parent)
        {
            if (frame.BrowsingContext.Container is not { } container)
                return (ActionOutcome.NotConnected, null);

            var parentContext = parent.GetExecutionContext();
            var frameElement = new ElementReference(parent, parentContext, parentContext.ToJsValue(container));
            var box = GetBoundingBox(frameElement);
            var style = await EvaluateInUtilityAsync(frameElement, "(injected, iframe) => injected.describeIFrameStyle(iframe)", progress).ConfigureAwait(true);
            if (box is null || (style.IsString && style.AsString() == "error:notconnected"))
                return (ActionOutcome.NotConnected, null);

            if (style.IsString && style.AsString() == "transformed")
                return (null, null);

            var styleObject = style.AsObject();
            var pointInFrame = (point.X - box["x"]!.GetValue<double>() - styleObject.GetProperty("left").AsNumber(), point.Y - box["y"]!.GetValue<double>() - styleObject.GetProperty("top").AsNumber());
            data.Add((frame, frameElement, pointInFrame));
            point = pointInFrame;
            frame = parent;
        }

        data.Add((frame, null, point));
        for (var i = data.Count - 1; i > 0; i--)
        {
            var frameElement = data[i - 1].FrameElement!;
            var hitPoint = data[i].PointInFrame;
            var result = await EvaluateInUtilityAsync(frameElement, "(injected, element, x, y) => injected.expectHitTarget({ x, y }, element)", progress, hitPoint.X, hitPoint.Y).ConfigureAwait(true);
            if (!(result.IsString && result.AsString() == "done"))
                return (ActionOutcome.FromJsValue(result) ?? ActionOutcome.NotConnected, null);
        }

        return (null, data[0].PointInFrame);
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Actions
    // ----------------------------------------------------------------------------------------------------------------

    public static Task<ActionOutcome> ClickAsync(ElementReference element, PointerActionOptions options, bool frameVisible, Progress progress)
    {
        var page = element.Frame.Page;
        return RetryPointerActionAsync(element, "click", waitForEnabled: true, point => page.Mouse.ClickAsync(point.X, point.Y, options.Button, options.ClickCount, options.Delay, progress), options, frameVisible, progress);
    }

    public static Task<ActionOutcome> DblClickAsync(ElementReference element, PointerActionOptions options, bool frameVisible, Progress progress)
    {
        var page = element.Frame.Page;
        return RetryPointerActionAsync(element, "dblclick", waitForEnabled: true, point => page.Mouse.ClickAsync(point.X, point.Y, options.Button, 2, options.Delay, progress), options, frameVisible, progress);
    }

    public static Task<ActionOutcome> HoverAsync(ElementReference element, PointerActionOptions options, bool frameVisible, Progress progress)
    {
        var page = element.Frame.Page;
        return RetryPointerActionAsync(element, "hover", waitForEnabled: false, point =>
        {
            page.Mouse.Move(point.X, point.Y, steps: 1);
            return Task.CompletedTask;
        }, options, frameVisible, progress);
    }

    public static Task<ActionOutcome> TapAsync(ElementReference element, PointerActionOptions options, bool frameVisible, Progress progress)
    {
        var page = element.Frame.Page;
        if (!page.BrowsingContext.Viewport.HasTouch)
            throw new PlaywrightError("The page does not support tap. Use hasTouch context option to enable touch support.") { IsNonRetriable = true };

        return RetryPointerActionAsync(element, "tap", waitForEnabled: true, point =>
        {
            page.Mouse.Tap(point.X, point.Y);
            return Task.CompletedTask;
        }, options, frameVisible, progress);
    }

    public static async Task<ActionOutcome> FillAsync(ElementReference element, string value, bool force, bool frameVisible, Progress progress)
    {
        progress.AddLog($"  fill(\"{value}\")");
        var page = element.Frame.Page;
        return await RetryActionAsync("fill", async _ =>
        {
            if (!force)
            {
                progress.AddLog("  waiting for element to be visible, enabled and editable");
            }

            var result = await EvaluateInUtilityAsync(element, "async (injected, node, value, force, frameVisible) => { if (!force) { const checkResult = await injected.checkElementStates(node, ['visible', 'enabled', 'editable'], frameVisible); if (checkResult) return checkResult; } return injected.fill(node, value); }", progress, value, force, frameVisible).ConfigureAwait(true);
            if (result.IsString && result.AsString() == "needsinput")
            {
                if (value.Length > 0)
                {
                    page.Keyboard.InsertText(value);
                }
                else
                {
                    await page.Keyboard.PressAsync("Delete", 0, progress).ConfigureAwait(true);
                }

                return ActionOutcome.Done;
            }

            return ActionOutcome.FromJsValue(result) ?? ActionOutcome.Done;
        }, force, trial: false, progress).ConfigureAwait(true);
    }

    public static async Task<(ActionOutcome Outcome, JsonArray Values)> SelectOptionAsync(ElementReference element, JsValue optionsToSelect, bool force, bool frameVisible, Progress progress)
    {
        var values = new JsonArray();
        var outcome = await RetryActionAsync("select option", async _ =>
        {
            if (!force)
            {
                progress.AddLog("  waiting for element to be visible and enabled");
            }

            var result = await EvaluateInUtilityAsync(element, "async (injected, node, optionsToSelect, force, frameVisible) => { if (!force) { const checkResult = await injected.checkElementStates(node, ['visible', 'enabled'], frameVisible); if (checkResult) return checkResult; } return injected.selectOptions(node, optionsToSelect); }", progress, optionsToSelect, force, frameVisible).ConfigureAwait(true);
            if (result.IsObject && element.Context.Engine.Engine.IsArray(result))
            {
                progress.AddLog("  selected specified option(s)");
                var array = result.AsObject();
                var length = (int)array.GetProperty("length").AsNumber();
                values.Clear();
                for (var i = 0; i < length; i++)
                {
                    values.Add((JsonNode)array.GetProperty(i.ToString(CultureInfo.InvariantCulture)).AsString());
                }

                return ActionOutcome.Done;
            }

            return ActionOutcome.FromJsValue(result) ?? ActionOutcome.Done;
        }, force, trial: false, progress).ConfigureAwait(true);
        return (outcome, values);
    }

    public static async Task<ActionOutcome> SetInputFilesAsync(ElementReference element, JsonElement parameters, Progress progress)
    {
        if (parameters.TryGet("localPaths", out _) || parameters.TryGet("localDirectory", out _) || parameters.TryGet("streams", out _))
            throw new PlaywrightError("Uploading files from paths is not supported by a remote Playwright server: pass the content of the files (FilePayload)") { IsNonRetriable = true };

        var payloads = new JsonArray();
        foreach (var payload in parameters.GetArray("payloads"))
        {
            payloads.Add((JsonNode)new JsonObject
            {
                ["name"] = payload.GetString("name") ?? "",
                ["mimeType"] = payload.GetString("mimeType") ?? "application/octet-stream",
                ["buffer"] = payload.GetString("buffer") ?? "",
            });
        }

        var multiple = payloads.Count > 1;
        var input = await EvaluateInUtilityAsync(element, "(injected, node, multiple) => { const element = injected.retarget(node, 'follow-label'); if (!element) return; if (element.tagName !== 'INPUT') throw injected.createStacklessError('Node is not an HTMLInputElement'); if (multiple && !element.multiple && !element.webkitdirectory) throw injected.createStacklessError('Non-multiple file input can only accept single file'); if (element.webkitdirectory) throw injected.createStacklessError('[webkitdirectory] input requires passing a path to a directory'); return element; }", progress, multiple).ConfigureAwait(true);
        if (!input.IsObject)
            return ActionOutcome.NotConnected;

        var target = element with { Value = input };
        await EvaluateInUtilityAsync(target, "(injected, node, files) => injected.setInputFiles(node, files)", progress, element.Context.ParseJson(payloads.ToJsonString())).ConfigureAwait(true);
        return ActionOutcome.Done;
    }

    public static async Task<ActionOutcome> FocusAsync(ElementReference element, bool resetSelectionIfNotFocused, Progress progress)
    {
        var result = await EvaluateInUtilityAsync(element, "(injected, node, reset) => injected.focusNode(node, reset)", progress, resetSelectionIfNotFocused).ConfigureAwait(true);
        return ActionOutcome.FromJsValue(result) ?? ActionOutcome.Done;
    }

    public static async Task<ActionOutcome> BlurAsync(ElementReference element, Progress progress)
    {
        var result = await EvaluateInUtilityAsync(element, "(injected, node) => injected.blurNode(node)", progress).ConfigureAwait(true);
        return ActionOutcome.FromJsValue(result) ?? ActionOutcome.Done;
    }

    public static async Task<ActionOutcome> TypeAsync(ElementReference element, string text, double delay, Progress progress)
    {
        progress.AddLog($"elementHandle.type(\"{text}\")");
        var focus = await FocusAsync(element, resetSelectionIfNotFocused: true, progress).ConfigureAwait(true);
        if (!focus.IsDone)
            return focus;

        await element.Frame.Page.Keyboard.TypeAsync(text, delay, progress).ConfigureAwait(true);
        return ActionOutcome.Done;
    }

    public static async Task<ActionOutcome> PressAsync(ElementReference element, string key, double delay, bool noWaitAfter, Progress progress)
    {
        progress.AddLog($"elementHandle.press(\"{key}\")");
        var page = element.Frame.Page;
        using var navigationWaiter = noWaitAfter ? null : page.StartNavigationWatcher();
        var focus = await FocusAsync(element, resetSelectionIfNotFocused: true, progress).ConfigureAwait(true);
        if (!focus.IsDone)
            return focus;

        await page.Keyboard.PressAsync(key, delay, progress).ConfigureAwait(true);
        if (navigationWaiter is not null)
        {
            await navigationWaiter.WaitAsync(progress).ConfigureAwait(true);
        }

        return ActionOutcome.Done;
    }

    public static async Task<ActionOutcome> SetCheckedAsync(ElementReference element, bool state, PointerActionOptions options, bool frameVisible, Progress progress)
    {
        async Task<(bool Matches, bool IsRadio)> IsCheckedAsync()
        {
            var result = await EvaluateInUtilityAsync(element, "(injected, node) => injected.elementState(node, 'checked')", progress).ConfigureAwait(true);
            if ((result.IsString && result.AsString() == "error:notconnected") || (result.IsObject && result.AsObject().GetProperty("received") is { IsString: true } received && received.AsString() == "error:notconnected"))
                throw new PlaywrightError("Element is not attached to the DOM");

            var obj = result.AsObject();
            return (JsEngine.ToBoolean(obj.GetProperty("matches")), JsEngine.ToBoolean(obj.GetProperty("isRadio")));
        }

        var checkedState = await IsCheckedAsync().ConfigureAwait(true);
        if (checkedState.Matches == state)
            return ActionOutcome.Done;

        if (!state && checkedState.IsRadio)
            throw new PlaywrightError("Cannot uncheck radio button. Radio buttons can only be unchecked by selecting another radio button in the same group.") { IsNonRetriable = true };

        var page = element.Frame.Page;
        var clicked = await RetryPointerActionAsync(element, "click", waitForEnabled: true, point => page.Mouse.ClickAsync(point.X, point.Y, "left", 1, 0, progress), options, frameVisible, progress, waitAfter: false).ConfigureAwait(true);
        if (!clicked.IsDone || options.Trial)
            return clicked;

        var finalState = await IsCheckedAsync().ConfigureAwait(true);
        if (finalState.Matches != state)
            throw new PlaywrightError("Clicking the checkbox did not change its state") { IsNonRetriable = true };

        return ActionOutcome.Done;
    }

    public static async Task<ActionOutcome> ScrollIntoViewIfNeededAsync(ElementReference element, Progress progress)
    {
        return await RetryActionAsync("scroll into view", async _ =>
        {
            progress.AddLog("  waiting for element to be stable");
            var result = await EvaluateInUtilityAsync(element, "(injected, node) => injected.checkElementStates(node, ['stable'], true)", progress).ConfigureAwait(true);
            if (ActionOutcome.FromJsValue(result) is { } failed)
                return failed;

            return ScrollRectIntoViewIfNeeded(element);
        }, force: false, trial: false, progress).ConfigureAwait(true);
    }

    public static async Task<ActionOutcome> WaitForElementStateAsync(ElementReference element, string state, Progress progress)
    {
        return await RetryActionAsync("wait for " + state, async _ =>
        {
            var result = await EvaluateInUtilityAsync(element, "async (injected, node, state) => (await injected.checkElementStates(node, [state], true)) || 'done'", progress, state).ConfigureAwait(true);
            return ActionOutcome.FromJsValue(result) ?? ActionOutcome.Done;
        }, force: false, trial: false, progress).ConfigureAwait(true);
    }

    public static async Task<ActionOutcome> SelectTextAsync(ElementReference element, bool force, Progress progress)
    {
        return await RetryActionAsync("selectText", async _ =>
        {
            if (!force)
            {
                progress.AddLog("  waiting for element to be visible");
            }

            var result = await EvaluateInUtilityAsync(element, "async (injected, node, force) => { if (!force) { const checkResult = await injected.checkElementStates(node, ['visible'], true); if (checkResult) return checkResult; } return injected.selectText(node); }", progress, force).ConfigureAwait(true);
            return ActionOutcome.FromJsValue(result) ?? ActionOutcome.Done;
        }, force, trial: false, progress).ConfigureAwait(true);
    }
}
