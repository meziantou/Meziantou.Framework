using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.WebPlatform.Dom;

#pragma warning disable WEBPLATEXP0023 // The target exercises HTML-in-canvas, a proposal

namespace Meziantou.Framework.WebPlatform.Fuzz;

/// <summary>
/// The target of HTML-in-canvas (https://github.com/WICG/html-in-canvas): the input is the contents of a canvas whose content is
/// drawable. A rendering update records the snapshots of its drawable elements, then each one is drawn into the canvas: the
/// display lists must be balanced, the default sizes finite and positive, and the canvas transform set by
/// <c>drawElementImage()</c> must map the element to where it was drawn (also in its geometry).
/// </summary>
public static partial class WebPlatformFuzzTargets
{
    /// <summary>The maximum length of the contents of the canvas (the cost of the layout and of the snapshots grows with them).</summary>
    private const int CanvasDrawablesMaxLength = 4096;

    /// <summary>The maximum number of drawable elements that the target draws.</summary>
    private const int CanvasDrawablesMaxElements = 16;

    private static string[] CanvasDrawablesTokens =>
    [
        " drawable", " content=drawable", "<canvas", "</canvas>", "<div", "<span", "<img", "<svg", "<dialog open>", " style=\"", "\">",
        "display: contents", "display: none", "position: fixed", "position: absolute", "transform: rotate(45deg)", "zoom: 2",
        "filter: blur(2px)", "opacity: 0.5", "overflow: scroll", "width: 10px", "height: 10px", "float: left", "column-count: 2",
    ];

    /// <summary>Draws the drawable elements of a canvas whose contents are the input, and checks their sizes and their geometries.</summary>
    public static void CanvasDrawElements(string input)
    {
        using var context = new BrowsingContext(new BrowsingContextOptions
        {
            RenderingBackend = new CheckingRenderingBackend(),
            ScriptingEnabled = true,
            TimeProvider = FrozenTimeProvider.Instance,
        });

        // The canvas is 128x96 CSS pixels, its bitmap 64x48 pixels: an element image is drawn at half its CSS size
        var document = context.Navigate("https://example.com/", _ => "<body style='margin: 0'><canvas content=drawable width=64 height=48 style='display: block; width: 128px; height: 96px'>" + input + "</canvas>");
        if (document.QuerySelector("canvas") is not HtmlCanvasElement canvas || canvas.GetContext("2d") is not CanvasRenderingContext2D context2D)
            return;

        document.RunCanvasPaintSteps();

        // The geometry of the elements is compared with the drawing when nothing transforms or zooms the canvas: the input can style it
        var hasTransformedAncestor = false;
        for (Element? ancestor = canvas; ancestor is not null; ancestor = ancestor.ParentElement)
        {
            var ancestorStyle = document.DefaultView!.GetComputedStyle(ancestor);
            hasTransformedAncestor |= IsTransformed(ancestorStyle) || ancestorStyle.GetPropertyValue("zoom") != "1";
        }

        var offset = 0;
        foreach (var element in canvas.QuerySelectorAll("[drawable]").OfType<Element>().Take(CanvasDrawablesMaxElements))
        {
            ElementImageDefaultSize size;
            try
            {
                size = canvas.GetElementImageDefaultSize(element);
            }
            catch (DomException exception) when (exception.Name == "InvalidStateError")
            {
                // Not a drawable element of the canvas, or not rendered: it cannot be drawn either
                var drawn = true;
                try
                {
                    context2D.DrawElementImage(element, 0, 0);
                }
                catch (DomException drawException) when (drawException.Name == "InvalidStateError")
                {
                    drawn = false;
                }

                FuzzAssert.False(drawn, "an element without default size cannot be drawn");
                continue;
            }

            FuzzAssert.True(double.IsFinite(size.Width) && double.IsFinite(size.Height) && size.Width >= 0 && size.Height >= 0, $"the default size {size.Width}x{size.Height} is finite and positive");

            // (as drawImage(), an image of zero width or height draws nothing: the geometry is not updated either)
            if (size.Width == 0 || size.Height == 0)
                continue;

            // drawElementImage() sets the canvas transform to where the element is drawn (in the CSS pixels of the element: a zoomed
            // element has a scaled transform), so its geometry is where it is drawn
            offset = (offset + 7) % 32;
            context2D.ResetTransform();
            context2D.DrawElementImage(element, offset, offset + 1);
            var transform = canvas.GetElementTransform(element);
            FuzzAssert.True(transform.Is2D && transform.B == 0 && transform.C == 0, "the canvas transform of an element drawn without rotation is a 2D scale and translation");

            var style = document.DefaultView!.GetComputedStyle(element);
            if (!IsTransformed(style) && !hasTransformedAncestor)
            {
                var rect = element.GetBoundingClientRect();
                FuzzAssert.True(Math.Abs(rect.Left - (2 * offset)) < 0.01 && Math.Abs(rect.Top - (2 * (offset + 1))) < 0.01, $"the element is at ({rect.Left}, {rect.Top}) instead of where it is drawn ({2 * offset}, {2 * (offset + 1)})");
                FuzzAssert.True(Math.Abs(rect.Width - (2 * size.Width)) < 0.01 && Math.Abs(rect.Height - (2 * size.Height)) < 0.01, $"the element is {rect.Width}x{rect.Height} instead of the size it is drawn at ({2 * size.Width}x{2 * size.Height})");
            }

            canvas.ClearElementGeometry(element);
            FuzzAssert.True(canvas.GetElementTransform(element).IsIdentity, "clearElementGeometry() resets the canvas transform");
        }

        // The pixels are read: the display lists of the canvas are rasterized (and checked by the backend)
        _ = context2D.GetImageData(0, 0, 1, 1);
    }

    private static bool IsTransformed(Css.Om.CssStyleDeclaration style)
        => style.GetPropertyValue("transform") != "none" || style.GetPropertyValue("translate") != "none" || style.GetPropertyValue("rotate") != "none"
            || style.GetPropertyValue("scale") != "none" || style.GetPropertyValue("offset-path") != "none";
}
