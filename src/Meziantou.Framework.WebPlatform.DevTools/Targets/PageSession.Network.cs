using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.Framework.WebPlatform.DevTools.Network;
using Meziantou.Framework.WebPlatform.DevTools.Protocol;

namespace Meziantou.Framework.WebPlatform.DevTools.Targets;

// The Network and Fetch domains (the requests are reported and intercepted by the ContextLoader of the browser context)
internal sealed partial class PageSession
{
    private JsonObject? InvokeNetwork(string method, JsonElement parameters)
    {
        var context = Target.Context;
        switch (method)
        {
            case "Network.enable":
                NetworkEnabled = true;
                return null;

            case "Network.disable":
                NetworkEnabled = false;
                return null;

            case "Network.setExtraHTTPHeaders":
                ExtraHeaders = parameters.GetHeaderObject("headers");
                return null;

            case "Network.emulateNetworkConditions":
            case "Network.emulateNetworkConditionsByRule":
                IsOffline = parameters.GetBoolean("offline", defaultValue: false);
                Target.ApplyNetworkSettings();
                return null;

            case "Network.setUserAgentOverride":
                SetUserAgentOverride(parameters);
                return null;

            case "Network.getResponseBody":
                var body = context.Loader.GetResponseBody(Target, parameters.GetRequiredString("requestId")) ?? throw new ProtocolException("No resource with given identifier found");
                return IsText(body)
                    ? new JsonObject { ["body"] = Encoding.UTF8.GetString(body), ["base64Encoded"] = false }
                    : new JsonObject { ["body"] = Convert.ToBase64String(body), ["base64Encoded"] = true };

            case "Network.getRequestPostData":
                var postData = context.Loader.GetRequestPostData(Target, parameters.GetRequiredString("requestId")) ?? throw new ProtocolException("No post data available for the request");
                return new JsonObject { ["postData"] = Encoding.UTF8.GetString(postData) };

            case "Network.getCookies":
                var urls = parameters.GetArray("urls").Select(url => url.GetString() ?? "").ToList();
                if (urls.Count == 0)
                {
                    urls = [.. Target.GetFrames().Select(frame => frame.Url)];
                }

                return new JsonObject { ["cookies"] = context.GetCookies(urls) };

            case "Network.getAllCookies":
                return new JsonObject { ["cookies"] = context.GetCookies(urls: null) };

            case "Network.setCookie":
                return new JsonObject { ["success"] = context.SetCookie(parameters) };

            case "Network.setCookies":
                context.SetCookies(parameters.TryGet("cookies", out var cookies) ? cookies : default);
                return null;

            case "Network.deleteCookies":
                context.DeleteCookies(parameters);
                return null;

            case "Network.clearBrowserCookies":
                context.Cookies.Clear();
                return null;

            case "Network.loadNetworkResource":
                throw new ProtocolException("Loading network resources is not supported");

            case "Network.setCacheDisabled":
            case "Network.setBypassServiceWorker":
            case "Network.clearBrowserCache":
            case "Network.setBlockedURLs":
            case "Network.setAcceptedEncodings":
            case "Network.clearAcceptedEncodingsOverride":
            case "Network.setAttachDebugStack":
            case "Network.enableReportingApi":
            case "Network.setCookieControls":
                return null;

            default:
                throw ProtocolException.NotFound(method);
        }
    }

    private JsonObject? InvokeFetch(string method, JsonElement parameters)
    {
        switch (method)
        {
            case "Fetch.enable":
                // The requests paused by the previous patterns continue
                Fetch?.Dispose();
                Fetch = new FetchInterception(parameters);
                return null;

            case "Fetch.disable":
                Fetch?.Dispose();
                Fetch = null;
                return null;

            default:
                throw ProtocolException.NotFound(method);
        }
    }

    private static bool IsText(byte[] body)
    {
        try
        {
            _ = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetCharCount(body);
            return Array.IndexOf(body, (byte)0) < 0;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}
