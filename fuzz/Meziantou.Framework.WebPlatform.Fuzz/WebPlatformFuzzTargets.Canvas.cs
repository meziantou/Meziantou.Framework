using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Rendering;

#pragma warning disable WEBPLATEXP0017 // The target exercises the canvas layers, a proposal
#pragma warning disable WEBPLATEXP0019 // The target exercises the interpolation methods of the canvas gradients, a proposal

namespace Meziantou.Framework.WebPlatform.Fuzz;

/// <summary>
/// The target of the state of the 2D canvas: a sequence of operations on the stack of the drawing states (saves, layers,
/// resets) mixed with drawings. The context must agree with a model of the stack (which calls throw an
/// <c>InvalidStateError</c>), and every display list the canvas rasterizes must have balanced saves and restores.
/// </summary>
public static partial class WebPlatformFuzzTargets
{
    private const int CanvasSize = 16;

    // The operations: one byte each, followed by the bytes of their arguments
    private const byte CanvasSave = 0;
    private const byte CanvasRestore = 1;
    private const byte CanvasBeginLayer = 2;
    private const byte CanvasBeginLayerWithFilter = 3;
    private const byte CanvasEndLayer = 4;
    private const byte CanvasFillRect = 5;
    private const byte CanvasClearRect = 6;
    private const byte CanvasReset = 7;
    private const byte CanvasGetImageData = 8;
    private const byte CanvasCompositing = 9;
    private const byte CanvasTransform = 10;
    private const byte CanvasClipAndFilter = 11;
    private const byte CanvasOperationCount = 12;

    /// <summary>The filters of the layers: valid and invalid CSS filters.</summary>
    private static readonly string[] CanvasLayerFilters = ["blur(2px)", "drop-shadow(1px 1px 0 red)", "none", "not a filter", "", "url(#missing)", "blur(-1px)", "grayscale(1) blur(1px)", "drop-shadow(-20px 0 2px blue) opacity(0.5)"];

    /// <summary>The colors of the stops of the gradients: legacy sRGB colors, colors of other color spaces, with missing components.</summary>
    private static readonly string[] CanvasGradientColors = ["red", "rgb(0 0 255 / 0.5)", "transparent", "color(srgb 0 1 0)", "oklch(0.7 0.2 none)", "hsl(none 100% 50%)", "lab(50 none 20 / 0.3)", "color-mix(in srgb, #0f0, #00f)"];

    /// <summary>The interpolation methods of the gradients: valid and invalid values.</summary>
    private static readonly string[] CanvasInterpolationMethods = ["srgb", "hsl", "hwb", "srgb-linear", "display-p3-linear", "oklab", "lch", "oklch", "xyz", "longer", "decreasing", "shorter", "increasing", "", "OKLAB", "not a method"];

    private static readonly string[] CanvasCompositeOperations = ["source-over", "copy", "xor", "destination-out", "multiply", "screen", "source-in", "lighter"];

    /// <summary>The tokens of the target: the pairs of operations that open and close a scope.</summary>
    private static readonly string[] CanvasTokens = ["\u0002\u0004", "\u0000\u0001", "\u0003\u0000", "\u0002\u0000\u0004", "\u0000\u0002\u0001", "\u0002\u0007\u0004", "\u0002\u0008"];

    private static readonly Lazy<CanvasRenderingContext2D> FuzzCanvasContext = new(CreateFuzzCanvasContext);

    private static CanvasRenderingContext2D CreateFuzzCanvasContext()
    {
#pragma warning disable CA2000 // The page lives as long as the process: the target reuses its canvas
        var context = new BrowsingContext(new BrowsingContextOptions { RenderingBackend = new CheckingRenderingBackend() });
#pragma warning restore CA2000
        var document = context.Navigate("https://example.com/", _ => $"<canvas id=c width={CanvasSize} height={CanvasSize}></canvas>");
        context.EventLoop.RunUntilIdle();
        var canvas = (HtmlCanvasElement)document.GetElementById("c")!;
        return (CanvasRenderingContext2D)canvas.GetContext("2d")!;
    }

    /// <summary>Runs the operations of the input on a 2D context, and checks the stack of its drawing states against a model.</summary>
    public static void CanvasState(ReadOnlySpan<byte> data)
    {
        var context = FuzzCanvasContext.Value;
        context.Reset();

        // The model of the stack: true for a state saved by BeginLayer(), false for one saved by Save()
        var stack = new List<bool>();
        var position = 0;
        byte Next(ReadOnlySpan<byte> input) => position < input.Length ? input[position++] : (byte)0;

        while (position < data.Length)
        {
            switch (Next(data) % CanvasOperationCount)
            {
                case CanvasSave:
                    context.Save();
                    stack.Add(false);
                    break;

                case CanvasRestore:
                    if (ExpectInvalidState(() => context.Restore(), stack is [.., true], "restore()") is false && stack.Count > 0)
                    {
                        stack.RemoveAt(stack.Count - 1);
                    }

                    break;

                case CanvasBeginLayer:
                    context.BeginLayer();
                    stack.Add(true);
                    FuzzAssert.Equal(1.0, context.GlobalAlpha, "beginLayer() resets the global alpha");
                    FuzzAssert.Equal("none", context.Filter, "beginLayer() resets the filter");
                    break;

                case CanvasBeginLayerWithFilter:
                    context.BeginLayer(new BeginLayerOptions { Filter = CanvasLayerFilters[Next(data) % CanvasLayerFilters.Length] });
                    stack.Add(true);
                    break;

                case CanvasEndLayer:
                {
                    var alpha = stack is [.., true] ? (double?)null : context.GlobalAlpha;
                    if (ExpectInvalidState(context.EndLayer, stack is not [.., true], "endLayer()") is false)
                    {
                        stack.RemoveAt(stack.Count - 1);
                    }
                    else
                    {
                        FuzzAssert.Equal(alpha, context.GlobalAlpha, "a failed endLayer() leaves the state unchanged");
                    }

                    break;
                }

                case CanvasFillRect:
                    var style = Next(data) % 4;
                    if (style == 3)
                    {
                        // A gradient interpolated in a color space: the invalid interpolation methods are ignored
                        var gradient = context.CreateLinearGradient(0, 0, Next(data) % CanvasSize, Next(data) % CanvasSize);
                        gradient.AddColorStop(Next(data) / 255.0, CanvasGradientColors[Next(data) % CanvasGradientColors.Length]);
                        gradient.AddColorStop(Next(data) / 255.0, CanvasGradientColors[Next(data) % CanvasGradientColors.Length]);
                        var colorMethod = gradient.ColorInterpolationMethod;
                        var hueMethod = gradient.HueInterpolationMethod;
                        var method = CanvasInterpolationMethods[Next(data) % CanvasInterpolationMethods.Length];
                        gradient.ColorInterpolationMethod = method;
                        gradient.HueInterpolationMethod = method;
                        FuzzAssert.True(gradient.ColorInterpolationMethod == method || gradient.ColorInterpolationMethod == colorMethod, "colorInterpolationMethod is the value set, or unchanged by an invalid value");
                        FuzzAssert.True(gradient.HueInterpolationMethod == method || gradient.HueInterpolationMethod == hueMethod, "hueInterpolationMethod is the value set, or unchanged by an invalid value");
                        context.FillStyle = gradient;
                    }
                    else
                    {
                        context.FillStyle = style switch { 0 => "red", 1 => "rgb(0 0 255 / 0.5)", _ => "transparent" };
                    }

                    context.FillRect(Next(data) - 128, Next(data) - 128, Next(data) - 64, Next(data) - 64);
                    break;

                case CanvasClearRect:
                    context.ClearRect(Next(data) % CanvasSize, Next(data) % CanvasSize, Next(data) % CanvasSize, Next(data) % CanvasSize);
                    break;

                case CanvasReset:
                    context.Reset();
                    stack.Clear();
                    break;

                case CanvasGetImageData:
                    ExpectInvalidState(() => context.GetImageData(0, 0, 2, 2), stack.Contains(true), "getImageData()");
                    break;

                case CanvasCompositing:
                    context.GlobalAlpha = Next(data) / 255.0;
                    context.GlobalCompositeOperation = CanvasCompositeOperations[Next(data) % CanvasCompositeOperations.Length];
                    context.ShadowColor = (Next(data) % 2) == 0 ? "transparent" : "black";
                    context.ShadowOffsetX = Next(data) % 8;
                    context.ShadowBlur = Next(data) % 4;
                    break;

                case CanvasTransform:
                    switch (Next(data) % 4)
                    {
                        case 0:
                            context.Scale(Next(data) / 64.0, Next(data) / 64.0);
                            break;
                        case 1:
                            context.Rotate(Next(data) / 40.0);
                            break;
                        case 2:
                            context.Translate(Next(data) - 128, Next(data) - 128);
                            break;
                        default:
                            context.ResetTransform();
                            break;
                    }

                    break;

                default:
                    context.BeginPath();
                    context.Rect(Next(data) % CanvasSize, Next(data) % CanvasSize, Next(data) % CanvasSize, Next(data) % CanvasSize);
                    context.Clip();
                    context.Filter = CanvasLayerFilters[Next(data) % CanvasLayerFilters.Length];
                    break;
            }

            FuzzAssert.Equal(stack.Contains(true), context.HasOpenLayers, "the context has an open layer when its stack has one");
        }

        // The layers are ended, then the pixels are read: the display lists are rasterized (and checked by the backend)
        while (stack.Count > 0)
        {
            if (stack[^1])
            {
                context.EndLayer();
            }
            else
            {
                context.Restore();
            }

            stack.RemoveAt(stack.Count - 1);
        }

        FuzzAssert.False(context.HasOpenLayers, "the layers are ended");
        context.GetImageData(0, 0, CanvasSize, CanvasSize);
    }

    /// <summary>Calls an operation that throws an <c>InvalidStateError</c> when <paramref name="expected"/> is true, and returns whether it threw.</summary>
    private static bool ExpectInvalidState(Action action, bool expected, string operation)
    {
        try
        {
            action();
        }
        catch (DomException ex)
        {
            FuzzAssert.True(expected, operation + " threw " + ex.Name + " although the stack does not require it");
            FuzzAssert.Equal("InvalidStateError", ex.Name, operation + " throws an InvalidStateError");
            return true;
        }

        FuzzAssert.False(expected, operation + " did not throw an InvalidStateError");
        return false;
    }

    /// <summary>The seeds of the canvas target: the scenarios of the unit tests and of the web-platform-tests of the layers.</summary>
    private static IEnumerable<FuzzSeed> GetCanvasSeeds()
    {
        byte[][] scenarios =
        [
            // A layer drawn with the global alpha of the state, and two overlapping rectangles
            [CanvasFillRect, 1, 128, 128, 94, 74, CanvasCompositing, 128, 0, 0, 0, 0, CanvasBeginLayer, CanvasFillRect, 0, 128, 128, 84, 74, CanvasFillRect, 0, 138, 128, 84, 74, CanvasEndLayer],

            // clearRect() and the copy operator in a layer
            [CanvasFillRect, 1, 128, 128, 94, 74, CanvasBeginLayer, CanvasCompositing, 255, 1, 0, 0, 0, CanvasFillRect, 0, 128, 128, 84, 74, CanvasClearRect, 0, 0, 4, 4, CanvasEndLayer],

            // A clip and a filter on the layer, and an invalid filter
            [CanvasClipAndFilter, 8, 0, 8, 16, 2, CanvasBeginLayerWithFilter, 0, CanvasFillRect, 0, 128, 128, 72, 74, CanvasEndLayer, CanvasReset, CanvasBeginLayerWithFilter, 3, CanvasFillRect, 0, 128, 128, 74, 74, CanvasEndLayer],

            // The unmatched calls and the pixel access with open layers
            [CanvasEndLayer, CanvasSave, CanvasEndLayer, CanvasRestore, CanvasBeginLayer, CanvasRestore, CanvasGetImageData, CanvasSave, CanvasEndLayer, CanvasRestore, CanvasEndLayer, CanvasBeginLayer, CanvasFillRect, 0, 128, 128, 74, 74, CanvasReset, CanvasEndLayer],

            // Nested layers, a layer opened with a non-invertible matrix, and unclosed layers
            [CanvasBeginLayerWithFilter, 1, CanvasTransform, 1, 80, CanvasBeginLayer, CanvasFillRect, 1, 120, 120, 74, 74, CanvasEndLayer, CanvasTransform, 0, 0, 64, CanvasBeginLayer, CanvasTransform, 3, CanvasFillRect, 0, 128, 128, 74, 74, CanvasEndLayer, CanvasEndLayer, CanvasBeginLayer, CanvasSave, CanvasBeginLayer],

            // Gradients interpolated in Oklab (a stop that is not a legacy sRGB color) and in other color spaces, with an
            // invalid interpolation method, in a layer with a filter
            [CanvasFillRect, 3, 16, 0, 0, 0, 255, 4, 7, 128, 128, 74, 74, CanvasBeginLayerWithFilter, 0, CanvasFillRect, 3, 16, 16, 0, 3, 255, 6, 1, 128, 128, 74, 74, CanvasFillRect, 3, 0, 16, 64, 5, 192, 1, 15, 128, 128, 74, 74, CanvasEndLayer],
        ];

        for (var i = 0; i < scenarios.Length; i++)
        {
            yield return new FuzzSeed("canvas-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), scenarios[i]);
        }
    }

    /// <summary>A rendering backend whose surfaces check the display lists they draw (balanced saves and restores) instead of rasterizing them.</summary>
    private sealed class CheckingRenderingBackend : RenderingBackend
    {
        internal override RenderingSurface? CreateSurface(int width, int height, RenderingSurfaceOptions options) => new CheckingSurface(width, height);

        internal override RenderingImageDecoder? CreateImageDecoder(ReadOnlyMemory<byte> data, string? mimeType) => null;

        private sealed class CheckingSurface(int width, int height) : RenderingSurface
        {
            public override int Width => width;

            public override int Height => height;

            public override void Draw(DisplayList displayList)
            {
                var depth = 0;
                foreach (var item in displayList.Items)
                {
                    switch (item.Kind)
                    {
                        case DisplayItemKind.Save or DisplayItemKind.SaveLayer:
                            depth++;
                            break;

                        case DisplayItemKind.Restore:
                            depth--;
                            FuzzAssert.True(depth >= 0, "the display list restores a state that was not saved");
                            break;
                    }
                }

                FuzzAssert.Equal(0, depth, "the display list restores every saved state");
            }

            public override void Clear(DisplayColor color)
            {
            }

            public override void ReadPixels(int x, int y, int width, int height, Span<byte> destination, ImageDataPixelFormat pixelFormat = ImageDataPixelFormat.RgbaUnorm8) => destination.Clear();

            public override void WritePixels(int x, int y, int width, int height, ReadOnlySpan<byte> source, ImageDataPixelFormat pixelFormat = ImageDataPixelFormat.RgbaUnorm8)
            {
            }

            public override RenderingImage CreateSnapshot() => new TransparentImage(width, height);

            protected override void Dispose(bool disposing)
            {
            }
        }

        private sealed class TransparentImage(int width, int height) : RenderingImage
        {
            public override int Width => width;

            public override int Height => height;

            public override void ReadPixels(Span<byte> destination) => destination.Clear();

            protected override void Dispose(bool disposing)
            {
            }
        }
    }
}
