using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.Framework.WebPlatform.DevTools.Protocol;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Imaging;
using Meziantou.Framework.WebPlatform.PdfRendering;

namespace Meziantou.Framework.WebPlatform.DevTools.Targets;

/// <summary>The geometry of the viewport, the screenshots and the PDF of a page (on the thread of the context).</summary>
internal static class Screenshots
{
    private const double CssPixelsPerInch = 96;

    /// <summary>The metrics of the viewport and of the content (<c>Page.getLayoutMetrics</c>): the <c>css*</c> properties are in CSS pixels.</summary>
    public static JsonObject GetLayoutMetrics(PageTarget target)
    {
        var browsingContext = target.BrowsingContext!;
        var window = browsingContext.Window;
        var viewport = browsingContext.Viewport;
        var dpr = viewport.DevicePixelRatio;
        var scrollX = window.ScrollX;
        var scrollY = window.ScrollY;
        var root = browsingContext.Document.DocumentElement;
        var clientWidth = root is not null && root.ClientWidth > 0 ? root.ClientWidth : viewport.Width;
        var clientHeight = root is not null && root.ClientHeight > 0 ? root.ClientHeight : viewport.Height;
        var contentWidth = Math.Max(root?.ScrollWidth ?? 0, clientWidth);
        var contentHeight = Math.Max(root?.ScrollHeight ?? 0, clientHeight);

        var layoutViewport = new JsonObject { ["pageX"] = scrollX, ["pageY"] = scrollY, ["clientWidth"] = clientWidth, ["clientHeight"] = clientHeight };
        var visualViewport = new JsonObject
        {
            ["offsetX"] = 0,
            ["offsetY"] = 0,
            ["pageX"] = scrollX,
            ["pageY"] = scrollY,
            ["clientWidth"] = clientWidth,
            ["clientHeight"] = clientHeight,
            ["scale"] = 1,
            ["zoom"] = 1,
        };
        return new JsonObject
        {
            ["layoutViewport"] = layoutViewport.DeepClone(),
            ["visualViewport"] = visualViewport.DeepClone(),
            ["contentSize"] = new JsonObject { ["x"] = 0, ["y"] = 0, ["width"] = contentWidth * dpr, ["height"] = contentHeight * dpr },
            ["cssLayoutViewport"] = layoutViewport,
            ["cssVisualViewport"] = visualViewport,
            ["cssContentSize"] = new JsonObject { ["x"] = 0, ["y"] = 0, ["width"] = contentWidth, ["height"] = contentHeight },
        };
    }

    /// <summary>
    /// Takes a screenshot (<c>Page.captureScreenshot</c>): the clip is in the coordinates of the document, and its scale multiplies
    /// the device pixel ratio of the viewport.
    /// </summary>
    public static byte[] Capture(PageTarget target, JsonElement parameters)
    {
        var browsingContext = target.BrowsingContext!;
        var viewport = browsingContext.Viewport;
        var captureBeyondViewport = parameters.GetBoolean("captureBeyondViewport", defaultValue: false);
        ScreenshotClip? clip = null;
        var scale = 1d;
        if (parameters.TryGet("clip", out var clipElement))
        {
            var width = clipElement.GetDouble("width") ?? 0;
            var height = clipElement.GetDouble("height") ?? 0;
            if (width <= 0 || height <= 0)
                throw new ProtocolException("Cannot take screenshot with 0 width.");

            var x = clipElement.GetDouble("x") ?? 0;
            var y = clipElement.GetDouble("y") ?? 0;
            if (!captureBeyondViewport)
            {
                var window = browsingContext.Window;
                x -= window.ScrollX;
                y -= window.ScrollY;
            }

            clip = new ScreenshotClip(x, y, width, height);
            scale = clipElement.GetDouble("scale") is > 0 and var clipScale ? clipScale : 1;
        }

        var options = new ScreenshotOptions
        {
            FullPage = captureBeyondViewport && clip is not null,
            Clip = clip,
            OmitBackground = target.Emulation.TransparentBackground,
            DevicePixelRatio = viewport.DevicePixelRatio * scale,
            Quality = parameters.GetInt32("quality"),
        };

        try
        {
            return parameters.GetString("format") switch
            {
                "jpeg" => browsingContext.TakeScreenshotAsJpeg(options),
                "webp" => throw new ProtocolException("The webp screenshots are not supported"),
                _ => browsingContext.TakeScreenshotAsPng(options),
            };
        }
        catch (ArgumentException ex)
        {
            throw new ProtocolException("Unable to capture screenshot: " + ex.Message, ex);
        }
    }

    /// <summary>Prints the page (<c>Page.printToPDF</c>): the sizes of the protocol are in inches.</summary>
    public static byte[] PrintToPdf(PageTarget target, JsonElement parameters)
    {
        var printOptions = new PrintOptions
        {
            PageWidth = (parameters.GetDouble("paperWidth") ?? 8.5) * CssPixelsPerInch,
            PageHeight = (parameters.GetDouble("paperHeight") ?? 11) * CssPixelsPerInch,
            MarginTop = (parameters.GetDouble("marginTop") ?? 0.4) * CssPixelsPerInch,
            MarginRight = (parameters.GetDouble("marginRight") ?? 0.4) * CssPixelsPerInch,
            MarginBottom = (parameters.GetDouble("marginBottom") ?? 0.4) * CssPixelsPerInch,
            MarginLeft = (parameters.GetDouble("marginLeft") ?? 0.4) * CssPixelsPerInch,
            Landscape = parameters.GetBoolean("landscape", defaultValue: false),
            Scale = parameters.GetDouble("scale") ?? 1,
            PrintBackground = parameters.GetBoolean("printBackground", defaultValue: false),
            PreferCssPageSize = parameters.GetBoolean("preferCSSPageSize", defaultValue: false),
            PageRanges = parameters.GetString("pageRanges") is { Length: > 0 } ranges ? ranges : null,
            DisplayHeaderFooter = parameters.GetBoolean("displayHeaderFooter", defaultValue: false),
            HeaderTemplate = parameters.GetString("headerTemplate"),
            FooterTemplate = parameters.GetString("footerTemplate"),
        };
        var pdfOptions = new PdfPrintOptions
        {
            Tagged = parameters.GetBoolean("generateTaggedPDF", defaultValue: false),
            IncludeOutlines = parameters.GetBoolean("generateDocumentOutline", defaultValue: false),
        };

        using var stream = new MemoryStream();
        try
        {
            target.BrowsingContext!.PrintToPdf(stream, printOptions, pdfOptions);
        }
        catch (ArgumentException ex)
        {
            throw new ProtocolException(ex.Message, ex);
        }

        return stream.ToArray();
    }
}
