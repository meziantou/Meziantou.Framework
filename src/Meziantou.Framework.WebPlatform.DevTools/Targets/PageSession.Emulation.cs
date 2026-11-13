using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.Framework.WebPlatform.DevTools.Protocol;

namespace Meziantou.Framework.WebPlatform.DevTools.Targets;

// The Emulation domain. The user agent, the locale and the time zone are the ones of the browser context: they apply when the
// first page of the context runs, and they cannot change afterwards.
internal sealed partial class PageSession
{
    private JsonObject? InvokeEmulation(string method, JsonElement parameters)
    {
        var emulation = Target.Emulation;
        switch (method)
        {
            case "Emulation.setDeviceMetricsOverride":
                emulation.Width = parameters.GetDouble("width");
                emulation.Height = parameters.GetDouble("height");
                emulation.DeviceScaleFactor = parameters.GetDouble("deviceScaleFactor");
                emulation.IsMobile = parameters.GetBoolean("mobile", defaultValue: false);
                emulation.ScreenWidth = parameters.GetDouble("screenWidth");
                emulation.ScreenHeight = parameters.GetDouble("screenHeight");
                Target.ApplyViewport();
                return null;

            case "Emulation.clearDeviceMetricsOverride":
                emulation.Width = null;
                emulation.Height = null;
                emulation.DeviceScaleFactor = null;
                emulation.IsMobile = false;
                emulation.ScreenWidth = null;
                emulation.ScreenHeight = null;
                Target.ApplyViewport();
                return null;

            case "Emulation.setEmulatedMedia":
                emulation.Media = parameters.GetString("media");
                emulation.MediaFeatures.Clear();
                foreach (var feature in parameters.GetArray("features"))
                {
                    if (feature.GetString("name") is { } name && feature.GetString("value") is { Length: > 0 } value)
                    {
                        emulation.MediaFeatures[name] = value;
                    }
                }

                Target.ApplyViewport();
                return null;

            case "Emulation.setUserAgentOverride":
                SetUserAgentOverride(parameters);
                return null;

            case "Emulation.setLocaleOverride":
                emulation.Locale = parameters.GetString("locale") is { Length: > 0 } locale ? locale : null;
                return null;

            case "Emulation.setTimezoneOverride":
                var timezoneId = parameters.GetString("timezoneId");
                emulation.TimezoneId = timezoneId is { Length: > 0 } ? timezoneId : null;
                return null;

            case "Emulation.setTouchEmulationEnabled":
                emulation.HasTouch = parameters.GetBoolean("enabled", defaultValue: false);
                Target.ApplyViewport();
                return null;

            case "Emulation.setScriptExecutionDisabled":
                emulation.ScriptExecutionDisabled = parameters.GetBoolean("value", defaultValue: false);
                return null;

            case "Emulation.setDefaultBackgroundColorOverride":
                // A transparent color makes the screenshots transparent (omitBackground); no color restores the default
                emulation.TransparentBackground = parameters.TryGet("color", out var color) && (color.GetDouble("a") ?? 1) == 0;
                return null;

            case "Emulation.canEmulate":
                return new JsonObject { ["result"] = true };

            case "Emulation.setFocusEmulationEnabled":
            case "Emulation.setGeolocationOverride":
            case "Emulation.clearGeolocationOverride":
            case "Emulation.setCPUThrottlingRate":
            case "Emulation.setIdleOverride":
            case "Emulation.clearIdleOverride":
            case "Emulation.setEmulatedVisionDeficiency":
            case "Emulation.setAutoDarkModeOverride":
            case "Emulation.setDocumentCookieDisabled":
            case "Emulation.setHardwareConcurrencyOverride":
            case "Emulation.setScrollbarsHidden":
            case "Emulation.setDefaultBackgroundColorOverrideForTesting":
            case "Emulation.setPageScaleFactor":
            case "Emulation.setVisibleSize":
            case "Emulation.setSensorOverrideEnabled":
            case "Emulation.setPressureSourceOverrideEnabled":
            case "Emulation.setAutomationOverride":
            case "Emulation.setSmallViewportHeightDifferenceOverride":
            case "Emulation.setDataSaverOverride":
            case "Emulation.setTouchEmulationEnabledForTesting":
                return null;

            default:
                throw ProtocolException.NotFound(method);
        }
    }

    private void SetUserAgentOverride(JsonElement parameters)
    {
        var emulation = Target.Emulation;
        emulation.UserAgent = parameters.GetString("userAgent") is { Length: > 0 } userAgent ? userAgent : null;
        emulation.AcceptLanguage = parameters.GetString("acceptLanguage") is { Length: > 0 } acceptLanguage ? acceptLanguage : null;
    }
}
