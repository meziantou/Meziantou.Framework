using System.Globalization;
using System.Text.Json;
using Meziantou.Framework.JavaScriptEngine;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Imaging;
using Meziantou.Framework.WebPlatform.PdfRendering;
using Meziantou.Framework.WebPlatform.Playwright.Dispatchers;
using Meziantou.Framework.WebPlatform.Playwright.Protocol;

namespace Meziantou.Framework.WebPlatform.Playwright.Server;

/// <summary>The screenshots and the PDF of the pages (<c>screenshotter.ts</c> and <c>page.pdf()</c> of the Playwright server).</summary>
internal static class Screenshotter
{
    /// <summary>Takes a screenshot of a page (<c>page.screenshot()</c>), on the thread of the context.</summary>
    public static async Task<byte[]> TakePageScreenshotAsync(PageDispatcher page, JsonElement parameters, Progress progress)
    {
        var browsingContext = page.BrowsingContext;
        ScreenshotClip? clip = null;
        if (parameters.TryGet("clip", out var clipElement))
        {
            var width = clipElement.GetDouble("width") ?? 0;
            var height = clipElement.GetDouble("height") ?? 0;
            if (width <= 0 || height <= 0)
                throw new PlaywrightError("Clipped area is either empty or outside the resulting image");

            clip = new ScreenshotClip(clipElement.GetDouble("x") ?? 0, clipElement.GetDouble("y") ?? 0, width, height);
        }

        var options = new ScreenshotOptions
        {
            FullPage = parameters.GetBoolean("fullPage", defaultValue: false),
            Clip = clip,
            OmitBackground = parameters.GetBoolean("omitBackground", defaultValue: false),
            DevicePixelRatio = parameters.GetString("scale") == "css" ? 1 : null,
            Quality = parameters.GetInt32("quality"),
        };

        using var scope = await PrepareAsync(page, parameters, progress).ConfigureAwait(true);
        return parameters.GetString("type") switch
        {
            "jpeg" => browsingContext.TakeScreenshotAsJpeg(options),
            "webp" => throw new PlaywrightError("The webp screenshots are not supported by the Playwright server of Meziantou.Framework.WebPlatform"),
            _ => browsingContext.TakeScreenshotAsPng(options),
        };
    }

    /// <summary>Takes a screenshot of an element (<c>elementHandle.screenshot()</c>), on the thread of the context.</summary>
    public static async Task<byte[]> TakeElementScreenshotAsync(PageDispatcher page, Element element, JsonElement parameters, Progress progress)
    {
        var options = new ScreenshotOptions
        {
            OmitBackground = parameters.GetBoolean("omitBackground", defaultValue: false),
            DevicePixelRatio = parameters.GetString("scale") == "css" ? 1 : null,
            Quality = parameters.GetInt32("quality"),
        };

        using var scope = await PrepareAsync(page, parameters, progress).ConfigureAwait(true);
        return parameters.GetString("type") switch
        {
            "jpeg" => element.TakeScreenshotAsJpeg(options),
            "webp" => throw new PlaywrightError("The webp screenshots are not supported by the Playwright server of Meziantou.Framework.WebPlatform"),
            _ => element.TakeScreenshotAsPng(options),
        };
    }

    /// <summary>
    /// Applies the options that change the page during a screenshot: the caret is hidden (<c>caret: "hide"</c>, the default),
    /// the style sheet of the <c>style</c> option is added to the documents, and the elements of the <c>mask</c> option are covered.
    /// </summary>
    private static async Task<IDisposable> PrepareAsync(PageDispatcher page, JsonElement parameters, Progress progress)
    {
        var restore = new List<Action>();
        var browsingContext = page.BrowsingContext;
        if (parameters.GetString("caret") != "initial" && browsingContext.Viewport.CaretVisible)
        {
            browsingContext.Viewport = browsingContext.Viewport with { CaretVisible = false };
            restore.Add(() => browsingContext.Viewport = browsingContext.Viewport with { CaretVisible = true });
        }

        if (parameters.GetString("style") is { Length: > 0 } style)
        {
            foreach (var frame in page.GetFrames())
            {
                var document = frame.BrowsingContext.Document;
                if (document.DocumentElement is not { } root)
                    continue;

                var styleElement = document.CreateElement("style");
                styleElement.TextContent = style;
                (document.Head ?? root).AppendChild(styleElement);
                restore.Add(() => styleElement.Remove());
            }
        }

        var maskColor = parameters.GetString("maskColor") ?? "#FF00FF";
        foreach (var mask in parameters.GetArray("mask"))
        {
            var frameGuid = mask.GetGuid("frame");
            var selector = mask.GetString("selector");
            if (frameGuid is null || selector is null || page.Connection.Find(frameGuid) is not FrameDispatcher frame)
                continue;

            var context = frame.GetExecutionContext();
            try
            {
                var elements = await frame.QueryAllElementsAsync(selector, progress).ConfigureAwait(true);
                var array = context.Call("(...elements) => elements", [.. elements]);
                context.CallInjected("(injected, elements, color) => injected.addMaskedElements(elements, color)", array, maskColor);
                restore.Add(() =>
                {
                    if (!context.IsDestroyed)
                    {
                        context.CallInjected("(injected) => injected.addMaskedElements([], '')");
                    }
                });
            }
            catch (JsException)
            {
            }
        }

        return new RestoreScope(restore);
    }

    private sealed class RestoreScope(List<Action> actions) : IDisposable
    {
        public void Dispose()
        {
            for (var i = actions.Count - 1; i >= 0; i--)
            {
                try
                {
                    actions[i]();
                }
                catch (JsException)
                {
                }
            }
        }
    }

    /// <summary>Prints a page to PDF (<c>page.pdf()</c>), on the thread of the context.</summary>
    public static byte[] PrintToPdf(PageDispatcher page, JsonElement parameters)
    {
        var (width, height) = GetPaperSize(parameters.GetString("format"));
        width = ParseLength(parameters.GetString("width")) ?? width;
        height = ParseLength(parameters.GetString("height")) ?? height;
        var margin = parameters.TryGet("margin", out var marginElement) ? marginElement : default;
        var printOptions = new PrintOptions
        {
            PageWidth = width,
            PageHeight = height,
            MarginTop = ParseLength(margin.GetString("top")) ?? 0,
            MarginRight = ParseLength(margin.GetString("right")) ?? 0,
            MarginBottom = ParseLength(margin.GetString("bottom")) ?? 0,
            MarginLeft = ParseLength(margin.GetString("left")) ?? 0,
            Landscape = parameters.GetBoolean("landscape", defaultValue: false),
            Scale = parameters.GetDouble("scale") ?? 1,
            PrintBackground = parameters.GetBoolean("printBackground", defaultValue: false),
            PreferCssPageSize = parameters.GetBoolean("preferCSSPageSize", defaultValue: false),
            PageRanges = parameters.GetString("pageRanges"),
            DisplayHeaderFooter = parameters.GetBoolean("displayHeaderFooter", defaultValue: false),
            HeaderTemplate = parameters.GetString("headerTemplate"),
            FooterTemplate = parameters.GetString("footerTemplate"),
        };
        var pdfOptions = new PdfPrintOptions
        {
            Tagged = parameters.GetBoolean("tagged", defaultValue: false),
            IncludeOutlines = parameters.GetBoolean("outline", defaultValue: false),
        };

        using var stream = new MemoryStream();
        page.BrowsingContext.PrintToPdf(stream, printOptions, pdfOptions);
        return stream.ToArray();
    }

    /// <summary>The size of a paper format of Playwright, in CSS pixels (96 per inch); Letter by default.</summary>
    private static (double Width, double Height) GetPaperSize(string? format)
    {
        var (widthInches, heightInches) = format?.ToUpperInvariant() switch
        {
            "LEGAL" => (8.5, 14),
            "TABLOID" => (11, 17),
            "LEDGER" => (17, 11),
            "A0" => (33.1, 46.8),
            "A1" => (23.4, 33.1),
            "A2" => (16.54, 23.4),
            "A3" => (11.7, 16.54),
            "A4" => (8.27, 11.7),
            "A5" => (5.83, 8.27),
            "A6" => (4.13, 5.83),
            null or "LETTER" => (8.5, 11),
            _ => throw new PlaywrightError("Unknown paper format: " + format),
        };
        return (widthInches * 96, heightInches * 96);
    }

    /// <summary>Parses a length of <c>page.pdf()</c> (a number of pixels, or a number followed by px, in, cm or mm).</summary>
    private static double? ParseLength(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        value = value.Trim().ToLowerInvariant();
        var (number, factor) = value switch
        {
            _ when value.EndsWith("px", StringComparison.Ordinal) => (value[..^2], 1d),
            _ when value.EndsWith("in", StringComparison.Ordinal) => (value[..^2], 96d),
            _ when value.EndsWith("cm", StringComparison.Ordinal) => (value[..^2], 37.8),
            _ when value.EndsWith("mm", StringComparison.Ordinal) => (value[..^2], 3.78),
            _ => (value, 1d),
        };
        if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
            throw new PlaywrightError("Failed to parse parameter value: " + value);

        return result * factor;
    }
}
