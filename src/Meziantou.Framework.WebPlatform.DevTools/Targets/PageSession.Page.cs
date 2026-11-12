using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.Framework.WebPlatform.DevTools.Protocol;

namespace Meziantou.Framework.WebPlatform.DevTools.Targets;

// The Page domain
internal sealed partial class PageSession
{
    private async Task<JsonObject?> InvokePageAsync(string method, JsonElement parameters)
    {
        switch (method)
        {
            case "Page.enable":
                PageEnabled = true;
                return null;

            case "Page.disable":
                PageEnabled = false;
                return null;

            case "Page.getFrameTree":
                return new JsonObject { ["frameTree"] = GetFrameTree(Target.MainFrame) };

            case "Page.getResourceTree":
                return new JsonObject { ["frameTree"] = GetResourceTree(Target.MainFrame) };

            case "Page.setLifecycleEventsEnabled":
                LifecycleEventsEnabled = parameters.GetBoolean("enabled", defaultValue: false);
                if (LifecycleEventsEnabled)
                {
                    Target.SendLifecycleEvents(this);
                }

                return null;

            case "Page.addScriptToEvaluateOnNewDocument":
                return AddScriptToEvaluateOnNewDocument(parameters);

            case "Page.removeScriptToEvaluateOnNewDocument":
                InitScripts.Remove(parameters.GetRequiredString("identifier"));
                return null;

            case "Page.createIsolatedWorld":
                if (!Target.IsLive)
                    throw new ProtocolException("Cannot create an isolated world before the page runs");

                var worldFrame = Target.GetFrame(parameters.GetRequiredString("frameId"));
                if (worldFrame.Document is null)
                    throw new ProtocolException("Frame has no document");

                var world = Target.CreateIsolatedWorld(worldFrame, parameters.GetString("worldName") ?? "");
                return new JsonObject { ["executionContextId"] = world.Id };

            case "Page.navigate":
                return await NavigateAsync(parameters).ConfigureAwait(true);

            case "Page.reload":
                Target.EnsureLive();
                Target.MainFrame.IsClientNavigation = true;
                Target.BrowsingContext!.Reload();
                return null;

            case "Page.stopLoading":
                return null;

            case "Page.getNavigationHistory":
                Target.EnsureLive();
                return NavigationHistory.Get(Target.BrowsingContext!);

            case "Page.navigateToHistoryEntry":
                Target.EnsureLive();
                NavigationHistory.NavigateTo(Target.BrowsingContext!, parameters.GetRequiredInt32("entryId"));
                return null;

            case "Page.resetNavigationHistory":
                return null;

            case "Page.close":
                _ = Target.CloseAsync(runBeforeUnload: true);
                return null;

            case "Page.getLayoutMetrics":
                Target.EnsureLive();
                return Screenshots.GetLayoutMetrics(Target);

            case "Page.captureScreenshot":
                Target.EnsureLive();
                return new JsonObject { ["data"] = Convert.ToBase64String(Screenshots.Capture(Target, parameters)) };

            case "Page.printToPDF":
                Target.EnsureLive();
                var pdf = Screenshots.PrintToPdf(Target, parameters);
                if (parameters.GetString("transferMode") == "ReturnAsStream")
                    return new JsonObject { ["data"] = "", ["stream"] = Target.Browser.Streams.Add(pdf) };

                return new JsonObject { ["data"] = Convert.ToBase64String(pdf) };

            case "Page.setInterceptFileChooserDialog":
                InterceptFileChooser = parameters.GetBoolean("enabled", defaultValue: false);
                return null;

            case "Page.setBypassCSP":
            case "Page.setFontFamilies":
            case "Page.setFontSizes":
            case "Page.bringToFront":
            case "Page.setDownloadBehavior":
            case "Page.setAdBlockingEnabled":
            case "Page.setWebLifecycleState":
            case "Page.setPrerenderingAllowed":
            case "Page.startScreencast":
            case "Page.stopScreencast":
            case "Page.screencastFrameAck":
            case "Page.setRPHRegistrationMode":
            case "Page.setSPCTransactionMode":
            case "Page.clearCompilationCache":
            case "Page.crash":
                return null;

            case "Page.setDocumentContent":
                Target.EnsureLive();
                var frame = Target.GetFrame(parameters.GetRequiredString("frameId"));
                var context = frame.MainWorld ?? throw new ProtocolException("Frame has no document");
                context.Call("(html) => { document.open(); document.write(html); document.close(); }", parameters.GetString("html") ?? "");
                return null;

            case "Page.getFrameOwner":
                throw ProtocolException.NotFound(method);

            default:
                throw ProtocolException.NotFound(method);
        }
    }

    private static JsonObject GetFrameTree(FrameState frame)
    {
        var result = new JsonObject { ["frame"] = frame.ToPayload() };
        var children = frame.Target.GetFrames().Where(f => ReferenceEquals(f.ParentFrame, frame)).ToArray();
        if (children.Length > 0)
        {
            result["childFrames"] = new JsonArray([.. children.Select(child => (JsonNode)GetFrameTree(child))]);
        }

        return result;
    }

    private static JsonObject GetResourceTree(FrameState frame)
    {
        var result = new JsonObject { ["frame"] = frame.ToPayload(), ["resources"] = new JsonArray() };
        var children = frame.Target.GetFrames().Where(f => ReferenceEquals(f.ParentFrame, frame)).ToArray();
        if (children.Length > 0)
        {
            result["childFrames"] = new JsonArray([.. children.Select(child => (JsonNode)GetResourceTree(child))]);
        }

        return result;
    }

    private JsonObject AddScriptToEvaluateOnNewDocument(JsonElement parameters)
    {
        var source = parameters.GetString("source") ?? "";
        var worldName = parameters.GetString("worldName");
        var identifier = AddInitScript(source, worldName);

        // The isolated world of the script is created in the current documents, and the script runs in them when asked
        var runImmediately = parameters.GetBoolean("runImmediately", defaultValue: false);
        foreach (var frame in Target.GetFrames())
        {
            if (frame.Document is null)
                continue;

            var context = worldName is { Length: > 0 } ? Target.CreateIsolatedWorld(frame, worldName) : frame.MainWorld;
            if (runImmediately && context is not null)
            {
                PageTarget.RunScript(context, source);
            }
        }

        return new JsonObject { ["identifier"] = identifier };
    }

    private async Task<JsonObject> NavigateAsync(JsonElement parameters)
    {
        Target.EnsureLive();
        var frame = Target.GetFrame(parameters.GetString("frameId"));
        var url = parameters.GetRequiredString("url");
        var browsingContext = frame.BrowsingContext ?? throw new ProtocolException("No frame for given id found");
        var waiter = frame.AddNavigationWaiter();
        try
        {
            frame.PendingReferrer = parameters.GetString("referrer") is { Length: > 0 } referrer ? referrer : null;
            frame.IsClientNavigation = true;
            browsingContext.Navigate(url);
        }
        catch (ArgumentException ex)
        {
            frame.RemoveNavigationWaiter(waiter);
            frame.IsClientNavigation = false;
            throw new ProtocolException("Cannot navigate to invalid URL", ex);
        }

        var result = new JsonObject { ["frameId"] = frame.FrameId };
        if (url.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
        {
            frame.RemoveNavigationWaiter(waiter);
            return result;
        }

        // The navigation ends with a new document, a same-document navigation, or an error. A navigation that does not
        // happen (e.g. canceled by a beforeunload handler) ends when the browsing context stops navigating.
        await Task.Yield();
        while (!waiter.Task.IsCompleted)
        {
            var winner = await Task.WhenAny(waiter.Task, Task.Delay(TimeSpan.FromMilliseconds(50), Connection.Closed)).ConfigureAwait(true);
            if (winner == waiter.Task)
                break;

            if (Connection.IsClosed || Target.IsClosed || frame.IsDetached)
            {
                frame.RemoveNavigationWaiter(waiter);
                throw new ProtocolException("Target closed");
            }

            if (!browsingContext.IsNavigating && frame.PendingLoaderId is null && !waiter.Task.IsCompleted)
            {
                frame.RemoveNavigationWaiter(waiter);
                frame.IsClientNavigation = false;
                result["errorText"] = "net::ERR_ABORTED";
                return result;
            }
        }

        var navigation = await waiter.Task.ConfigureAwait(true);
        if (navigation.LoaderId is not null)
        {
            result["loaderId"] = navigation.LoaderId;
        }

        if (navigation.ErrorText is not null)
        {
            result["errorText"] = navigation.ErrorText;
        }

        return result;
    }
}
