using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.Framework.JavaScriptEngine;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Playwright.Protocol;
using Meziantou.Framework.WebPlatform.Playwright.Server;

namespace Meziantou.Framework.WebPlatform.Playwright.Dispatchers;

/// <summary>The target of a selector: the selector, its strictness, and the element it is relative to (or null for the document).</summary>
internal sealed record SelectorTarget(string Selector, bool Strict, ElementReference? Scope = null);

/// <summary>An element of a page: its frame, the execution context of its document, and its value in the realm of the document.</summary>
internal sealed record ElementReference(FrameDispatcher Frame, FrameExecutionContext Context, JsValue Value);

/// <summary>The result of a function called on the elements that match a selector (<c>callOnSelector</c>).</summary>
internal sealed record SelectorCallResult(FrameDispatcher Frame, FrameExecutionContext Context, JsValue Result, bool FrameVisible);

// The selectors (frameSelectors.ts of the Playwright server), the queries and the assertions of the frames
internal sealed partial class FrameDispatcher
{
    private const string NoResultValue = "--playwright--no--result--value--";

    private const string QueryFramesFunction = """
        (injected, parsed, scope, strict, selectorString, frameVisible) => {
          const elements = injected.querySelectorAll(parsed, scope || document);
          if (strict && elements.length > 1)
            throw injected.strictModeViolationError(parsed, elements);
          for (const element of elements) {
            if (element.nodeName !== 'IFRAME' && element.nodeName !== 'FRAME')
              throw injected.createStacklessError(`Selector "${selectorString}" resolved to ${injected.previewNode(element)}, <iframe> was expected`);
          }
          return elements.map(element => [element, frameVisible ? injected.utils.isElementVisible(element) : false]);
        }
        """;

    private const string CallOnSelectorFunction = """
        (injected, parsed, scope, strict, callWithoutMatches, frameVisible, func, arg) => {
          const elements = injected.querySelectorAll(parsed, scope || document);
          if (strict && elements.length > 1)
            throw injected.strictModeViolationError(parsed, elements);
          if (!elements.length && !callWithoutMatches)
            return '--playwright--no--result--value--';
          return func({ injected, elements, info: { parsed, strict }, frameVisible }, arg);
        }
        """;

    private static readonly int[] BackoffTimeouts = [20, 50, 100, 100, 500];

    /// <summary>Gets whether the selectors are strict by default (the <c>strictSelectors</c> option of the context).</summary>
    private bool IsStrictByDefault => Context.Options.StrictSelectors;

    /// <summary>Returns the locator of a selector in the language of the client, for the logs (e.g. <c>GetByRole(AriaRole.Button)</c>).</summary>
    public string AsLocator(string selector)
    {
        try
        {
            var context = GetExecutionContext();
            return context.CallInjected("(injected, language, selector) => injected.utils.asLocator(language, selector)", Context.Browser.SdkLanguage, selector).AsString();
        }
        catch (Exception ex) when (ex is JsException or PlaywrightError or InvalidCastException)
        {
            return selector;
        }
    }

    /// <summary>Parses a selector into the parsed selectors of its frames (split at <c>internal:control=enter-frame</c>), as JSON.</summary>
    private List<string> ParseSelectorChunks(string selector)
    {
        var context = GetExecutionContext();
        string json;
        try
        {
            json = context.Stringify(context.CallInjected("(injected, selector) => injected.parseSelector(selector)", selector)) ?? throw new PlaywrightError("Invalid selector: " + selector) { IsNonRetriable = true };
        }
        catch (JsException ex)
        {
            throw ToNonRetriableError(context, ex);
        }

        var parsed = JsonNode.Parse(json)!.AsObject();
        var parts = parsed["parts"]!.AsArray();
        var capture = parsed["capture"]?.GetValue<double>();
        var chunks = new List<string>();
        var chunk = new JsonArray();
        int? chunkCapture = null;
        var chunkStartIndex = 0;
        for (var i = 0; i < parts.Count; i++)
        {
            var part = parts[i]!;
            var name = part["name"]?.GetValue<string>();
            var body = part["body"] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
            if (name == "internal:control" && body == "any-frame")
                throw new PlaywrightError("\"any-frame\" selectors are not supported by the Playwright server of Meziantou.Framework.WebPlatform") { IsNonRetriable = true };

            if (name == "internal:control" && body == "enter-frame")
            {
                if (chunk.Count == 0)
                    throw new PlaywrightError("Selector cannot start with entering frame, select the iframe first") { IsNonRetriable = true };

                chunks.Add(CreateChunk(chunk, chunkCapture));
                chunk = [];
                chunkCapture = null;
                chunkStartIndex = i + 1;
                continue;
            }

            if (capture is { } captureIndex && (int)captureIndex == i)
            {
                chunkCapture = i - chunkStartIndex;
            }

            chunk.Add(part.DeepClone());
        }

        if (chunk.Count == 0)
            throw new PlaywrightError("Selector cannot end with entering frame, while parsing selector " + selector) { IsNonRetriable = true };

        chunks.Add(CreateChunk(chunk, chunkCapture));
        return chunks;

        static string CreateChunk(JsonArray parts, int? capture)
        {
            var result = new JsonObject { ["parts"] = parts };
            if (capture is not null)
            {
                result["capture"] = capture.Value;
            }

            return result.ToJsonString();
        }
    }

    private static PlaywrightError ToNonRetriableError(FrameExecutionContext context, JsException exception)
    {
        var error = context.ToPlaywrightError(exception);
        return new PlaywrightError(error.ErrorName, error.Message, exception) { ErrorStack = error.ErrorStack, IsNonRetriable = true };
    }

    /// <summary>Resolves the frames of a selector: the frame of its last part, the parsed last part and the scope in that frame.</summary>
    private (FrameDispatcher Frame, string Parsed, JsValue Scope, bool FrameVisible)? ResolveFrames(SelectorTarget target)
    {
        var chunks = ParseSelectorChunks(target.Selector);
        var frame = this;
        var scope = target.Scope?.Value ?? JsValue.Undefined;
        var frameVisible = true;
        for (var i = 0; i < chunks.Count - 1; i++)
        {
            var context = frame.GetExecutionContext();
            JsValue result;
            try
            {
                result = context.CallInjected(QueryFramesFunction, context.ParseJson(chunks[i]), scope, target.Strict, target.Selector, frameVisible);
            }
            catch (JsException ex)
            {
                throw ToNonRetriableError(context, ex);
            }

            var entries = result.AsObject();
            if (entries.GetProperty("length").AsNumber() == 0)
                return null;

            var first = entries.GetProperty("0").AsObject();
            var element = context.FromJsValue(first.GetProperty("0"));
            var browsingContext = element switch
            {
                HtmlIFrameElement iframe => iframe.ContentWindow?.BrowsingContext,
                HtmlFrameElement frameElement => frameElement.ContentWindow?.BrowsingContext,
                _ => null,
            };
            if (Context.FindFrame(browsingContext) is not { } childFrame)
                return null;

            frameVisible = frameVisible && JsEngine.ToBoolean(first.GetProperty("1"));
            frame = childFrame;
            scope = JsValue.Undefined;
        }

        return (frame, chunks[^1], scope, frameVisible);
    }

    /// <summary>
    /// Calls a function on the elements that match a selector: <paramref name="functionSource"/> receives
    /// <c>{ injected, elements, info, frameVisible }</c> and <paramref name="argument"/>. Returns null when no element matches.
    /// </summary>
    public async Task<SelectorCallResult?> CallOnSelectorAsync(SelectorTarget target, string functionSource, JsValue argument, Progress progress, bool callWithoutMatches = false)
    {
        if (ResolveFrames(target) is not { } resolved)
            return null;

        var context = resolved.Frame.GetExecutionContext();
        try
        {
            var parsed = context.ParseJson(resolved.Parsed);
            var function = context.GetFunction(functionSource);
            var result = await context.CallInjectedAsync(CallOnSelectorFunction, progress, parsed, resolved.Scope, target.Strict, callWithoutMatches, resolved.FrameVisible, function, argument).ConfigureAwait(true);
            if (result.IsString && result.AsString() == NoResultValue)
                return null;

            return new SelectorCallResult(resolved.Frame, context, result, resolved.FrameVisible);
        }
        catch (JsException ex)
        {
            throw ToNonRetriableError(context, ex);
        }
    }

    /// <summary>Whether an error ends the operations that retry (<c>isNonRetriableError</c>).</summary>
    private bool IsNonRetriableError(Exception exception, Progress progress)
    {
        if (progress.IsCanceled || IsDetached)
            return true;

        return exception switch
        {
            PlaywrightError error => error.IsNonRetriable || error.ErrorName is "TimeoutError" or "TargetClosedError" or "AbortError",
            JsException => true,
            _ => false,
        };
    }

    /// <summary>
    /// Runs an action until it is done, with the backoff of Playwright between the attempts (<c>retryWithProgressAndBackoff</c>):
    /// the action returns (true, value) when it is done, or (false, _) to poll again. The errors that are not retriable end the polling.
    /// </summary>
    public async Task<T?> RetryWithBackoffAsync<T>(Progress progress, Func<Task<(bool Done, T? Value)>> action)
    {
        var timeouts = new List<int> { 0 };
        timeouts.AddRange(BackoffTimeouts.Where(timeout => progress.Timeout <= 0 || timeout <= progress.Timeout / 5));
        var index = 0;
        while (true)
        {
            var delay = timeouts[Math.Min(index++, timeouts.Count - 1)];
            if (delay > 0)
            {
                await progress.WaitAsync(TimeSpan.FromMilliseconds(delay)).ConfigureAwait(true);
            }

            if (IsDetached)
                throw new PlaywrightError("Frame was detached");

            try
            {
                var (done, value) = await action().ConfigureAwait(true);
                if (done)
                    return value;
            }
            catch (Exception ex) when (!IsNonRetriableError(ex, progress))
            {
            }
        }
    }

    /// <summary>Waits for an element that matches a selector, and calls a function on it (<c>_waitForFunctionOnSelector</c>).</summary>
    private async Task<JsValue> WaitForFunctionOnSelectorAsync(SelectorTarget target, string callback, JsValue data, Progress progress)
    {
        progress.AddLog("waiting for " + AsLocator(target.Selector));
        var result = await RetryWithBackoffAsync(progress, async () =>
        {
            var context = GetExecutionContext();
            var callbackFunction = context.GetFunction(callback);
            var argument = context.Call("(callback, data) => ({ callback, data })", callbackFunction, data);
            var resolved = await CallOnSelectorAsync(target, "({ injected, elements }, { callback, data }) => { const element = elements[0]; const value = callback(injected, element, data); if (!value) return { success: false }; return { log: `  locator resolved to ${injected.previewNode(element)}`, success: true, value }; }", argument, progress).ConfigureAwait(true);
            if (resolved is null)
                return (false, JsValue.Undefined);

            var resultObject = resolved.Result.AsObject();
            if (resultObject.GetProperty("log") is { IsString: true } log)
            {
                progress.AddLog(log.AsString());
            }

            if (!JsEngine.ToBoolean(resultObject.GetProperty("success")))
                return (false, JsValue.Undefined);

            return (true, resultObject.GetProperty("value"));
        }).ConfigureAwait(true);
        return result;
    }

    private SelectorTarget CreateTarget(JsonElement parameters, string name = "selector") => new(parameters.GetRequiredString(name), parameters.GetBoolean("strict") ?? IsStrictByDefault);

    /// <summary>Returns the elements that match a selector (<c>querySelectorAll</c>), without waiting.</summary>
    public async Task<List<JsValue>> QueryAllElementsAsync(string selector, Progress progress, ElementReference? scope = null)
    {
        var resolved = await CallOnSelectorAsync(new SelectorTarget(selector, Strict: false, scope), "({ elements }) => elements", JsValue.Undefined, progress).ConfigureAwait(true);
        if (resolved is null)
            return [];

        var array = resolved.Result.AsObject();
        var length = (int)array.GetProperty("length").AsNumber();
        var result = new List<JsValue>(length);
        for (var i = 0; i < length; i++)
        {
            result.Add(array.GetProperty(i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        return result;
    }

    /// <summary>Returns the frame and the execution context of the elements that match a selector (the last frame of a frame locator).</summary>
    private async Task<(FrameDispatcher Frame, FrameExecutionContext Context, List<JsValue> Elements)> QueryAllInFrameAsync(string selector, Progress progress, ElementReference? scope)
    {
        var resolved = await CallOnSelectorAsync(new SelectorTarget(selector, Strict: false, scope), "({ elements }) => elements", JsValue.Undefined, progress).ConfigureAwait(true);
        if (resolved is null)
            return (this, GetExecutionContext(), []);

        var array = resolved.Result.AsObject();
        var length = (int)array.GetProperty("length").AsNumber();
        var elements = new List<JsValue>(length);
        for (var i = 0; i < length; i++)
        {
            elements.Add(array.GetProperty(i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        return (resolved.Frame, resolved.Context, elements);
    }

    /// <summary>Waits for the state of an element that matches a selector (<c>waitForSelector</c>), and returns the element, or null.</summary>
    public async Task<ElementReference?> WaitForSelectorAsync(SelectorTarget target, string state, bool omitReturnValue, Progress progress, bool log = true)
    {
        if (state is not ("attached" or "detached" or "visible" or "hidden"))
            throw new PlaywrightError("state: expected one of (attached|detached|visible|hidden)");

        if (log)
        {
            progress.AddLog("waiting for " + AsLocator(target.Selector) + (state == "attached" ? "" : " to be " + state));
        }

        return await RetryWithBackoffAsync<ElementReference?>(progress, async () =>
        {
            if (target.Scope is { } scope && !JsEngine.ToBoolean(scope.Context.Call("(node) => node.isConnected", scope.Value)))
                throw new PlaywrightError("Element is not attached to the DOM") { IsNonRetriable = true };

            var resolved = await CallOnSelectorAsync(target, """
                ({ injected, elements, frameVisible }) => {
                  const element = elements[0];
                  const visible = element ? injected.elementState(element, 'visible', frameVisible).matches : false;
                  let log = '';
                  if (elements.length > 1)
                    log = `  locator resolved to ${elements.length} elements. Proceeding with the first one: ${injected.previewNode(elements[0])}`;
                  else if (element)
                    log = `  locator resolved to ${visible ? 'visible' : 'hidden'} ${injected.previewNode(element)}`;
                  return { log, element, visible, attached: !!element };
                }
                """, JsValue.Undefined, progress).ConfigureAwait(true);
            if (resolved is null)
                return state is "hidden" or "detached" ? (true, null) : (false, null);

            var result = resolved.Result.AsObject();
            if (result.GetProperty("log") is { IsString: true } logLine && logLine.AsString().Length > 0)
            {
                progress.AddLog(logLine.AsString());
            }

            var visible = JsEngine.ToBoolean(result.GetProperty("visible"));
            var attached = JsEngine.ToBoolean(result.GetProperty("attached"));
            var success = state switch
            {
                "attached" => attached,
                "detached" => !attached,
                "visible" => visible,
                _ => !visible,
            };
            if (!success)
                return (false, null);

            if (omitReturnValue || state is "hidden" or "detached")
                return (true, null);

            return (true, new ElementReference(resolved.Frame, resolved.Context, result.GetProperty("element")));
        }).ConfigureAwait(true);
    }

    /// <summary>Returns the state of the element that matches a selector (<c>isEnabled</c>, <c>isChecked</c>...), once it exists.</summary>
    private async Task<bool> GetElementStateAsync(SelectorTarget target, string state, Progress progress)
    {
        var result = await WaitForFunctionOnSelectorAsync(target, "(injected, element, data) => ({ result: injected.elementState(element, data.state) })", GetExecutionContext().ParseJson(new JsonObject { ["state"] = state }.ToJsonString()), progress).ConfigureAwait(true);
        var stateResult = result.AsObject().GetProperty("result").AsObject();
        if (stateResult.GetProperty("received") is { IsString: true } received && received.AsString() == "error:notconnected")
            throw new PlaywrightError("Element is not attached to the DOM");

        return JsEngine.ToBoolean(stateResult.GetProperty("matches"));
    }

    /// <summary>Returns whether the element that matches a selector is visible (false when there is no element), without waiting.</summary>
    private async Task<bool> IsVisibleAsync(SelectorTarget target, Progress progress)
    {
        progress.AddLog("  checking visibility of " + AsLocator(target.Selector));
        try
        {
            var resolved = await CallOnSelectorAsync(target, "({ injected, elements, frameVisible }) => injected.elementState(elements[0], 'visible', frameVisible).matches", JsValue.Undefined, progress).ConfigureAwait(true);
            return resolved is not null && JsEngine.ToBoolean(resolved.Result);
        }
        catch (Exception ex) when (!IsNonRetriableError(ex, progress))
        {
            return false;
        }
    }

    /// <summary>Asserts a condition on the elements that match a selector (<c>expect(locator)</c>), with retries until the timeout.</summary>
    private async Task ExpectAsync(JsonElement parameters, Progress progress)
    {
        var selector = parameters.GetString("selector");
        var expression = parameters.GetRequiredString("expression");
        var isNot = parameters.GetBoolean("isNot", defaultValue: false);
        if (expression == "to.match.aria")
            throw new ExpectError(new JsonObject { ["customErrorMessage"] = "toMatchAriaSnapshot is not supported by the Playwright server of Meziantou.Framework.WebPlatform" });

        progress.AddLog("Expect \"" + expression + "\"" + (progress.Timeout > 0 ? $" with timeout {progress.Timeout.ToInvariantString()}ms" : ""));
        var lastReceived = (JsonObject?)null;
        string? lastErrorMessage = null;
        var hasIntermediateResult = false;
        try
        {
            if (selector is not null)
            {
                progress.AddLog("waiting for " + AsLocator(selector));
            }

            var matched = await RetryWithBackoffAsync(progress, async () =>
            {
                var (matches, received, missingReceived) = await ExpectOnceAsync(selector, expression, parameters, progress).ConfigureAwait(true);
                if (matches == isNot)
                {
                    hasIntermediateResult = true;
                    lastReceived = received;
                    lastErrorMessage = missingReceived ? "element(s) not found" : null;
                    return (false, false);
                }

                return (true, true);
            }).ConfigureAwait(true);
            _ = matched;
        }
        catch (Exception ex) when (ex is not ExpectError)
        {
            var details = new JsonObject();
            if (ex is PlaywrightError { IsNonRetriable: true } nonRetriable && !nonRetriable.IsTimeout)
            {
                details["customErrorMessage"] = nonRetriable.Message.StartsWith("Error: ", StringComparison.Ordinal) ? nonRetriable.Message["Error: ".Length..] : nonRetriable.Message;
            }
            else if (hasIntermediateResult)
            {
                if (lastReceived is not null)
                {
                    details["received"] = lastReceived;
                }

                if (lastErrorMessage is not null)
                {
                    details["customErrorMessage"] = lastErrorMessage;
                }
            }
            else if (ex is PlaywrightError { ErrorName: "TargetClosedError" } closed)
            {
                details["customErrorMessage"] = closed.Message;
            }

            if (ex is PlaywrightError { IsTimeout: true } || (progress.IsCanceled && progress.GetCancellationError().IsTimeout))
            {
                details["timedOut"] = true;
            }

            throw new ExpectError(details);
        }
    }

    private async Task<(bool Matches, JsonObject? Received, bool MissingReceived)> ExpectOnceAsync(string? selector, string expression, JsonElement parameters, Progress progress)
    {
        var isNot = parameters.GetBoolean("isNot", defaultValue: false);
        var isArray = expression == "to.have.count" || expression.EndsWith(".array", StringComparison.Ordinal);
        var effectiveSelector = selector ?? ":root";
        var context = GetExecutionContext();

        // The options of the injected script: the parameters of the client, with the expected value parsed
        var options = (JsonObject)JsonNode.Parse(parameters.GetRawText())!;
        options.Remove("expectedValue");
        options.Remove("selector");
        var optionsValue = context.ParseJson(options.ToJsonString());
        if (parameters.TryGet("expectedValue", out var expectedValue))
        {
            var parsedExpectedValue = await Evaluation.EvaluateHandleAsync(context, "(value) => value", isFunction: true, expectedValue, Connection, progress).ConfigureAwait(true);
            optionsValue.AsObject().SetProperty("expectedValue", parsedExpectedValue);
        }

        var resolved = await CallOnSelectorAsync(new SelectorTarget(effectiveSelector, !isArray), """
            async ({ injected, elements, frameVisible }, options) => {
              const isArray = options.expression === 'to.have.count' || options.expression.endsWith('.array');
              const log = isArray ? `  locator resolved to ${elements.length} element${elements.length === 1 ? '' : 's'}` : `  locator resolved to ${injected.previewNode(elements[0])}`;
              return { log, ...await injected.expect(elements[0], options, elements, frameVisible) };
            }
            """, optionsValue, progress).ConfigureAwait(true);

        if (resolved is not null)
        {
            var result = resolved.Result.AsObject();
            if (result.GetProperty("log") is { IsString: true } log)
            {
                progress.AddLog(log.AsString());
            }

            var matches = JsEngine.ToBoolean(result.GetProperty("matches"));
            JsonObject? received = null;
            if (result.GetProperty("received") is { IsObject: true } receivedValue)
            {
                received = new JsonObject();
                var value = receivedValue.AsObject().GetProperty("value");
                if (!value.IsUndefined)
                {
                    received["value"] = Evaluation.SerializeValue(resolved.Context, value);
                }

                if (receivedValue.AsObject().GetProperty("ariaSnapshot") is { IsString: true } ariaSnapshot)
                {
                    received["ariaSnapshot"] = ariaSnapshot.AsString();
                }
            }

            if (matches == isNot && received?["value"] is JsonObject { } receivedSerialized && receivedSerialized["s"] is JsonValue text)
            {
                progress.AddLog($"  unexpected value \"{text}\"");
            }

            return (matches, received, false);
        }

        // No element matches
        if (expression == "to.have.count")
        {
            progress.AddLog("  locator resolved to 0 elements");
            return (parameters.GetDouble("expectedNumber") == 0, new JsonObject { ["value"] = new JsonObject { ["n"] = 0 } }, false);
        }

        if (expression.EndsWith(".array", StringComparison.Ordinal))
        {
            progress.AddLog("  locator resolved to 0 elements");
            return (!parameters.GetArray("expectedText").Any(), new JsonObject { ["value"] = new JsonObject { ["a"] = new JsonArray(), ["id"] = 1 } }, false);
        }

        var noElementMatches = (expression, isNot) switch
        {
            ("to.be.hidden", false) => true,
            ("to.be.visible", true) => false,
            ("to.be.detached", false) => true,
            ("to.be.attached", true) => false,
            ("to.be.in.viewport", true) => false,
            _ => (bool?)null,
        };
        if (noElementMatches is { } value2)
            return (value2, null, false);

        return (isNot, null, true);
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Protocol
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Runs a query of an element handle (its target is the element itself, <c>:scope</c>).</summary>
    internal ValueTask<JsonNode?> InvokeOnScopeAsync(string method, JsonElement parameters, SelectorTarget target, Progress progress) => InvokeSelectorMethodAsync(method, parameters, progress, target);

    private async ValueTask<JsonNode?> InvokeSelectorMethodAsync(string method, JsonElement parameters, Progress progress, SelectorTarget? scopeTarget = null)
    {
        var engine = Context.Engine;
        return method switch
        {
            "querySelector" => await engine.InvokeAsync(async () =>
            {
                var target = CreateTarget(parameters);
                progress.AddLog($"    finding element using the selector \"{target.Selector}\"");
                var resolved = await CallOnSelectorAsync(target, "({ elements }) => elements[0]", JsValue.Undefined, progress).ConfigureAwait(true);
                var result = new JsonObject();
                if (resolved is not null && resolved.Result.IsObject)
                {
                    result["element"] = resolved.Frame.CreateHandle(resolved.Context, resolved.Result).ToReference();
                }

                return result;
            }).ConfigureAwait(false),

            "querySelectorAll" => await engine.InvokeAsync(async () =>
            {
                var (frame, context, elements) = await QueryAllInFrameAsync(parameters.GetRequiredString("selector"), progress, scope: null).ConfigureAwait(true);
                return new JsonObject { ["elements"] = new JsonArray([.. elements.Select(element => (JsonNode)frame.CreateHandle(context, element).ToReference())]) };
            }).ConfigureAwait(false),

            "queryCount" => await engine.InvokeAsync(async () =>
            {
                try
                {
                    var resolved = await CallOnSelectorAsync(new SelectorTarget(parameters.GetRequiredString("selector"), Strict: false), "({ elements }) => elements.length", JsValue.Undefined, progress).ConfigureAwait(true);
                    return new JsonObject { ["value"] = resolved is null ? 0 : (int)resolved.Result.AsNumber() };
                }
                catch (Exception ex) when (!IsNonRetriableError(ex, progress))
                {
                    return new JsonObject { ["value"] = 0 };
                }
            }).ConfigureAwait(false),

            "waitForSelector" => await engine.InvokeAsync(async () =>
            {
                var element = await WaitForSelectorAsync(CreateTarget(parameters), parameters.GetString("state") ?? "visible", parameters.GetBoolean("omitReturnValue", defaultValue: false), progress).ConfigureAwait(true);
                var result = new JsonObject();
                if (element is not null)
                {
                    result["element"] = element.Frame.CreateHandle(element.Context, element.Value).ToReference();
                }

                return result;
            }).ConfigureAwait(false),

            "evalOnSelector" => await engine.InvokeAsync(async () =>
            {
                var target = CreateTarget(parameters);
                var resolved = await CallOnSelectorAsync(target, "({ elements }) => elements[0]", JsValue.Undefined, progress).ConfigureAwait(true);
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
            }).ConfigureAwait(false),

            "evalOnSelectorAll" => await engine.InvokeAsync(async () =>
            {
                var (frame, context, elements) = await QueryAllInFrameAsync(parameters.GetRequiredString("selector"), progress, scope: null).ConfigureAwait(true);
                var array = context.Call("(...elements) => elements", [.. elements]);
                var handle = new JSHandleDispatcher(frame, context, array, "JSHandle@array");
                try
                {
                    return new JsonObject { ["value"] = await Evaluation.EvaluateAsync(context, parameters.GetRequiredString("expression"), parameters.GetBoolean("isFunction"), GetArgument(parameters), Connection, progress, handle).ConfigureAwait(true) };
                }
                finally
                {
                    handle.Dispose();
                }
            }).ConfigureAwait(false),

            "textContent" => await engine.InvokeAsync(async () => ToNullableStringResult(await WaitForFunctionOnSelectorAsync(scopeTarget ?? CreateTarget(parameters), "(injected, element) => ({ result: element.textContent })", JsValue.Undefined, progress).ConfigureAwait(true))).ConfigureAwait(false),

            "innerText" => await engine.InvokeAsync(async () => ToNullableStringResult(await WaitForFunctionOnSelectorAsync(scopeTarget ?? CreateTarget(parameters), "(injected, element) => { if (element.namespaceURI !== 'http://www.w3.org/1999/xhtml') throw injected.createStacklessError('Node is not an HTMLElement'); return { result: element.innerText }; }", JsValue.Undefined, progress).ConfigureAwait(true))).ConfigureAwait(false),

            "innerHTML" => await engine.InvokeAsync(async () => ToNullableStringResult(await WaitForFunctionOnSelectorAsync(scopeTarget ?? CreateTarget(parameters), "(injected, element) => ({ result: element.innerHTML })", JsValue.Undefined, progress).ConfigureAwait(true))).ConfigureAwait(false),

            "getAttribute" => await engine.InvokeAsync(async () => ToNullableStringResult(await WaitForFunctionOnSelectorAsync(scopeTarget ?? CreateTarget(parameters), "(injected, element, name) => ({ result: element.getAttribute(name) })", parameters.GetRequiredString("name"), progress).ConfigureAwait(true))).ConfigureAwait(false),

            "inputValue" => await engine.InvokeAsync(async () => ToNullableStringResult(await WaitForFunctionOnSelectorAsync(scopeTarget ?? CreateTarget(parameters), "(injected, node) => { const element = injected.retarget(node, 'follow-label'); if (!element || (element.nodeName !== 'INPUT' && element.nodeName !== 'TEXTAREA' && element.nodeName !== 'SELECT')) throw injected.createStacklessError('Node is not an <input>, <textarea> or <select> element'); return { result: element.value }; }", JsValue.Undefined, progress).ConfigureAwait(true))).ConfigureAwait(false),

            "isVisible" => await engine.InvokeAsync(async () => new JsonObject { ["value"] = await IsVisibleAsync(scopeTarget ?? CreateTarget(parameters), progress).ConfigureAwait(true) }).ConfigureAwait(false),
            "isHidden" => await engine.InvokeAsync(async () => new JsonObject { ["value"] = !await IsVisibleAsync(scopeTarget ?? CreateTarget(parameters), progress).ConfigureAwait(true) }).ConfigureAwait(false),
            "isEnabled" => await engine.InvokeAsync(async () => new JsonObject { ["value"] = await GetElementStateAsync(scopeTarget ?? CreateTarget(parameters), "enabled", progress).ConfigureAwait(true) }).ConfigureAwait(false),
            "isDisabled" => await engine.InvokeAsync(async () => new JsonObject { ["value"] = await GetElementStateAsync(scopeTarget ?? CreateTarget(parameters), "disabled", progress).ConfigureAwait(true) }).ConfigureAwait(false),
            "isEditable" => await engine.InvokeAsync(async () => new JsonObject { ["value"] = await GetElementStateAsync(scopeTarget ?? CreateTarget(parameters), "editable", progress).ConfigureAwait(true) }).ConfigureAwait(false),
            "isChecked" => await engine.InvokeAsync(async () => new JsonObject { ["value"] = await GetElementStateAsync(scopeTarget ?? CreateTarget(parameters), "checked", progress).ConfigureAwait(true) }).ConfigureAwait(false),

            "expect" => await engine.InvokeAsync(async () =>
            {
                await ExpectAsync(parameters, progress).ConfigureAwait(true);
                return (JsonNode?)null;
            }).ConfigureAwait(false),

            "dispatchEvent" => await engine.InvokeAsync(async () =>
            {
                var context = GetExecutionContext();
                var eventInit = parameters.TryGet("eventInit", out var init) ? await Evaluation.EvaluateHandleAsync(context, "(value) => value", isFunction: true, init, Connection, progress).ConfigureAwait(true) : JsValue.Undefined;
                var data = context.Call("(type, eventInit) => ({ type, eventInit })", parameters.GetRequiredString("type"), eventInit);
                await WaitForFunctionOnSelectorAsync(scopeTarget ?? CreateTarget(parameters), "(injected, element, data) => { injected.dispatchEvent(element, data.type, data.eventInit); return { result: undefined }; }", data, progress).ConfigureAwait(true);
                return (JsonNode?)null;
            }).ConfigureAwait(false),

            "ariaSnapshot" => await engine.InvokeAsync(async () =>
            {
                var selector = parameters.GetString("selector") ?? "body";
                var options = new JsonObject { ["mode"] = parameters.GetString("mode") ?? "default" };
                if (parameters.GetInt32("depth") is { } depth)
                {
                    options["depth"] = depth;
                }

                var result = await WaitForFunctionOnSelectorAsync(new SelectorTarget(selector, Strict: true), "(injected, element, options) => ({ result: injected.ariaSnapshot(element, options) })", GetExecutionContext().ParseJson(options.ToJsonString()), progress).ConfigureAwait(true);
                return new JsonObject { ["snapshot"] = result.AsObject().GetProperty("result").AsString() };
            }).ConfigureAwait(false),

            "resolveSelector" => await engine.InvokeAsync(async () =>
            {
                var selector = parameters.GetRequiredString("selector");
                var resolved = await CallOnSelectorAsync(new SelectorTarget(selector, Strict: false), "({ injected, elements }) => injected.generateSelectorSimple(elements[0])", JsValue.Undefined, progress).ConfigureAwait(true)
                    ?? throw new PlaywrightError("No element matching " + selector);
                var result = new JsonObject { ["resolvedSelector"] = resolved.Result.AsString() };
                if (parameters.GetString("sdkLanguage") is { } language)
                {
                    result["locatorCode"] = resolved.Context.CallInjected("(injected, language, selector) => injected.utils.asLocator(language, selector)", language, resolved.Result.AsString()).AsString();
                }

                return result;
            }).ConfigureAwait(false),

            "waitForFunction" => await engine.InvokeAsync(() => WaitForFunctionAsync(parameters, progress)).ConfigureAwait(false),

            "addScriptTag" or "addStyleTag" => await engine.InvokeAsync(() => AddTagAsync(method == "addScriptTag", parameters, progress)).ConfigureAwait(false),

            "highlight" or "hideHighlight" => null,

            _ => await InvokeActionAsync(method, parameters, progress).ConfigureAwait(false),
        };
    }

    private static JsonObject ToNullableStringResult(JsValue value)
    {
        var result = new JsonObject();
        if (value.IsObject && value.AsObject().GetProperty("result") is { IsString: true } text)
        {
            result["value"] = text.AsString();
        }

        return result;
    }

    /// <summary>Waits until a function returns a truthy value (<c>waitForFunction</c>), polling at each animation frame or at an interval.</summary>
    private async Task<JsonNode?> WaitForFunctionAsync(JsonElement parameters, Progress progress)
    {
        var expression = Evaluation.NormalizeExpression(parameters.GetRequiredString("expression"), parameters.GetBoolean("isFunction"));
        var isFunction = parameters.GetBoolean("isFunction");
        var polling = parameters.GetDouble("pollingInterval");
        if (polling is <= 0)
            throw new PlaywrightError("Cannot poll with non-positive interval: " + polling.Value.ToInvariantString());

        if (parameters.GetString("selector") is { } selector)
        {
            var context = GetExecutionContext();
            var argument = await Evaluation.EvaluateHandleAsync(context, "(value) => value", isFunction: true, GetArgument(parameters), Connection, progress).ConfigureAwait(true);
            var data = context.Call("(expression, isFunction, arg) => ({ expression, isFunction, arg })", expression, isFunction is { } f ? f : JsValue.Undefined, argument);
            await WaitForFunctionOnSelectorAsync(new SelectorTarget(selector, parameters.GetBoolean("strict") ?? IsStrictByDefault), "(injected, element, data) => { let result = globalThis.eval(data.expression); if (data.isFunction === true) result = result(element, data.arg); else if (data.isFunction !== false && typeof result === 'function') result = result(element, data.arg); return result; }", data, progress).ConfigureAwait(true);
            return new JsonObject();
        }

        var handle = await RetryWithBackoffAsync(progress, async () =>
        {
            var context = GetExecutionContext();
            var argument = await Evaluation.EvaluateHandleAsync(context, "(value) => value", isFunction: true, GetArgument(parameters), Connection, progress).ConfigureAwait(true);
            var polled = context.CallInjected("""
                (injected, expression, isFunction, polling, arg) => {
                  let evaledExpression;
                  const predicate = () => {
                    let result = evaledExpression ?? globalThis.eval(expression);
                    if (isFunction === true) {
                      evaledExpression = result;
                      result = result(arg);
                    } else if (isFunction !== false && typeof result === 'function') {
                      evaledExpression = result;
                      result = result(arg);
                    }
                    return result;
                  };
                  return new Promise((fulfill, reject) => {
                    const next = () => {
                      try {
                        const success = predicate();
                        if (success) {
                          fulfill(success);
                          return;
                        }
                        if (typeof polling !== 'number')
                          injected.utils.builtins.requestAnimationFrame(next);
                        else
                          injected.utils.builtins.setTimeout(next, polling);
                      } catch (e) {
                        reject(e);
                      }
                    };
                    next();
                  });
                }
                """, expression, isFunction is { } fn ? fn : JsValue.Undefined, polling is { } interval ? interval : JsValue.Undefined, argument);
            try
            {
                var value = await context.AwaitAsync(polled, progress).ConfigureAwait(true);
                return (true, CreateHandle(context, value));
            }
            catch (JsException ex)
            {
                throw new PlaywrightError(context.ToPlaywrightError(ex).ErrorName, context.ToPlaywrightError(ex).Message, ex) { IsNonRetriable = true };
            }
        }).ConfigureAwait(true);
        return new JsonObject { ["handle"] = handle!.ToReference() };
    }

    /// <summary>Adds a script or a style element (<c>addScriptTag</c>, <c>addStyleTag</c>), and waits for it to load.</summary>
    private async Task<JsonNode?> AddTagAsync(bool isScript, JsonElement parameters, Progress progress)
    {
        var context = GetExecutionContext();
        var options = new JsonObject
        {
            ["url"] = parameters.GetString("url"),
            ["content"] = parameters.GetString("content"),
            ["type"] = parameters.GetString("type") ?? "",
        };
        var function = isScript
            ? """
              async ({ url, content, type }) => {
                const script = document.createElement('script');
                script.type = type || 'text/javascript';
                if (url) {
                  script.src = url;
                  const promise = new Promise((resolve, reject) => { script.onload = resolve; script.onerror = () => reject(new Error('Failed to load script at ' + url)); });
                  document.head.appendChild(script);
                  await promise;
                } else {
                  script.text = content;
                  document.head.appendChild(script);
                }
                return script;
              }
              """
            : """
              async ({ url, content }) => {
                let element;
                if (url) {
                  element = document.createElement('link');
                  element.rel = 'stylesheet';
                  element.href = url;
                  const promise = new Promise((resolve, reject) => { element.onload = resolve; element.onerror = () => reject(new Error('Failed to load stylesheet at ' + url)); });
                  document.head.appendChild(element);
                  await promise;
                } else {
                  element = document.createElement('style');
                  element.appendChild(document.createTextNode(content));
                  document.head.appendChild(element);
                }
                return element;
              }
              """;
        try
        {
            var element = await context.CallAsync(function, progress, context.ParseJson(options.ToJsonString())).ConfigureAwait(true);
            return new JsonObject { ["element"] = CreateHandle(context, element).ToReference() };
        }
        catch (JsException ex)
        {
            throw context.ToPlaywrightError(ex);
        }
    }
}

/// <summary>A failed assertion (<c>ExpectError</c>): its details are the <c>errorDetails</c> of the response.</summary>
#pragma warning disable CA1064 // The errors of the protocol are not exposed to the users of the library
internal sealed class ExpectError : Exception
#pragma warning restore CA1064
{
    public ExpectError(JsonObject details)
        : base("Expect failed")
    {
        Details = details;
    }

    public ExpectError()
        : this([])
    {
    }

    public ExpectError(string message)
        : base(message)
    {
        Details = [];
    }

    public ExpectError(string message, Exception innerException)
        : base(message, innerException)
    {
        Details = [];
    }

    public JsonObject Details { get; }
}
