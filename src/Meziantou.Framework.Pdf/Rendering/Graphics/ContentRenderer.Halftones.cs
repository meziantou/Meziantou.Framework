using System.Buffers.Binary;
using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Rendering.Graphics;

/// <summary>
/// The halftones (ISO 32000-2, 10.6), when <see cref="PdfRenderOptions.UseHalftones"/> asks for them: each color
/// component of the colors an opaque object paints is turned fully on or off by the screen of the halftone of the
/// graphics state. The default halftone (<c>/Default</c>, and the initial graphics state) leaves the colors as they are.
/// </summary>
internal sealed partial class ContentRenderer
{
    private static readonly PdfName HalftoneType = PdfName.Create("HalftoneType");

    /// <summary>Gets or initializes whether the halftones are not applied: in the cells of the patterns, the soft masks, and the groups that are not opaque.</summary>
    public bool SuppressHalftones { get; init; }

    /// <summary>
    /// Gets the halftone that applies to an object painted with an alpha: only to an object that is fully opaque, painted
    /// with an alpha of 1, the Normal blend mode and no soft mask (11.7.5.2); the backdrop it does not hide keeps the
    /// default halftone.
    /// </summary>
    /// <param name="alpha">The constant alpha of the painting operation.</param>
    /// <param name="hasSoftMask">Whether the object has a soft mask of its own: an image with <c>/SMask</c>.</param>
    private RenderHalftone? GetHalftone(double alpha, bool hasSoftMask = false)
    {
        if (_state.Halftone is not { } halftone || SuppressHalftones || hasSoftMask || alpha < 1 || _state.BlendMode != BlendMode.Normal || _state.SoftMask is not null)
            return null;

        return halftone;
    }

    /// <summary>
    /// Creates the filter that halftones a drawing: the transfer functions, then each component compared with the
    /// threshold of its pixel, the drawing keeping its coverage (its alpha).
    /// </summary>
    private FilterGraph CreateHalftoneFilter(RenderHalftone halftone)
    {
        var image = halftone.GetThresholdImage(_state.HalftoneOrigin, DeviceSize, _cache).Frames[0];
        var transfer = _state.Transfer;

        // The step of the comparison: 0 below one half, 1 from one half
        var step = TransferFunction.CreateDiscrete(0, 1);
        return new FilterGraph(

            // 0: the colors through the transfer functions, opaque, so that the comparison sees the colors alone
            new ComponentTransferPrimitive
            {
                Red = halftone.Red.Transfer ?? transfer?.Red ?? TransferFunction.Identity,
                Green = halftone.Green.Transfer ?? transfer?.Green ?? TransferFunction.Identity,
                Blue = halftone.Blue.Transfer ?? transfer?.Blue ?? TransferFunction.Identity,
                Alpha = TransferFunction.CreateLinear(0, 1),
                ColorSpace = FilterColorSpace.Srgb,
            },

            // 1: the thresholds of device space, T / 65535
            new ImagePrimitive(canvas => canvas.Fill(new ImageBrush(image) { WrapMode = ImageWrapMode.Repeat, Sampling = ImageSampling.Nearest }))
            {
                ColorSpace = FilterColorSpace.Srgb,
            },

            // 2: 65535 × c − T + 1, which is at least one half where 65535 × c ≥ T − 0.5
            new CompositePrimitive(FilterCompositeOperator.Arithmetic, FilterInput.FromResult(1))
            {
                Input = FilterInput.FromResult(0),
                K2 = 65535,
                K3 = -65535,
                K4 = 1,
                ColorSpace = FilterColorSpace.Srgb,
            },

            // 3: white or black
            new ComponentTransferPrimitive { Red = step, Green = step, Blue = step, ColorSpace = FilterColorSpace.Srgb },

            // 4: the coverage of the drawing
            new CompositePrimitive(FilterCompositeOperator.In, FilterInput.SourceAlpha) { ColorSpace = FilterColorSpace.Srgb });
    }

    /// <summary>Reads the halftone of a graphics state (<c>/HT</c>); <see langword="null"/> for the default halftone, and for a halftone that cannot be used.</summary>
    private RenderHalftone? ReadHalftone(PdfObject value) => _cache.GetHalftone(value, () => CreateHalftone(value));

    private RenderHalftone? CreateHalftone(PdfObject value)
    {
        // The name Default, and any other name, is the default halftone of the device
        if (GetHalftoneDictionary(value) is not { } dictionary)
            return null;

        try
        {
            if (dictionary.GetInteger(HalftoneType, -1) != 5)
            {
                // One screen for every component
                return ReadHalftoneComponent(value) is { } component ? new RenderHalftone(component, component, component) : null;
            }

            // A screen per colorant, and the default one for the colorants without their own (10.6.5.6)
            var fallback = dictionary.GetResolved(PdfName.Create("Default"));
            var red = ReadHalftoneComponent(dictionary.GetResolved(PdfName.Create("Red")) ?? fallback);
            var green = ReadHalftoneComponent(dictionary.GetResolved(PdfName.Create("Green")) ?? fallback);
            var blue = ReadHalftoneComponent(dictionary.GetResolved(PdfName.Create("Blue")) ?? fallback);
            return red is not null && green is not null && blue is not null ? new RenderHalftone(red, green, blue) : null;
        }
        catch (Exception ex) when (RenderCache.IsContentError(ex))
        {
            return null;
        }
    }

    /// <summary>Reads the halftone of one component: a halftone of type 1, 6, 10 or 16; <see langword="null"/> when it is not valid.</summary>
    private HalftoneComponent? ReadHalftoneComponent(PdfObject? value)
    {
        if (GetHalftoneDictionary(value) is not { } dictionary)
            return null;

        var stream = value is null ? null : PdfReference.ResolveIfReference(value) as PdfStream;
        var screen = dictionary.GetInteger(HalftoneType, -1) switch
        {
            1 => ReadSpotFunctionScreen(dictionary),
            6 when stream is not null => ReadThresholdScreen(stream, ReadDimension(dictionary, "Width"), ReadDimension(dictionary, "Height"), 0, 0, bytesPerThreshold: 1),
            10 when stream is not null => ReadThresholdScreen(stream, ReadDimension(dictionary, "Xsquare"), ReadDimension(dictionary, "Xsquare"), ReadDimension(dictionary, "Ysquare"), ReadDimension(dictionary, "Ysquare"), bytesPerThreshold: 1),
            16 when stream is not null => ReadThresholdScreen(stream, ReadDimension(dictionary, "Width"), ReadDimension(dictionary, "Height"), ReadDimension(dictionary, "Width2"), ReadDimension(dictionary, "Height2"), bytesPerThreshold: 2),
            _ => null,
        };
        if (screen is null)
            return null;

        // The transfer function of the component replaces the one of the graphics state; Identity is the identity
        var transfer = dictionary.GetResolved(PdfName.Create("TransferFunction")) switch
        {
            null => null,
            PdfName name => name.Value == "Identity" ? TransferFunction.Identity : null,
            var function => TryCreateTransfer(function),
        };
        return new HalftoneComponent(screen, transfer);
    }

    private static PdfDictionary? GetHalftoneDictionary(PdfObject? value) => (value is null ? null : PdfReference.ResolveIfReference(value)) switch
    {
        PdfStream stream => stream.Dictionary,
        PdfDictionary dictionary => dictionary,
        _ => null,
    };

    private static int ReadDimension(PdfDictionary dictionary, string key)
    {
        var value = dictionary.GetNumber(PdfName.Create(key), 0);
        return value is >= 0 and <= int.MaxValue ? (int)value : -1;
    }

    /// <summary>Reads the threshold array of a type 6, 10 or 16 halftone: one or two rectangles of 8-bit or 16-bit thresholds.</summary>
    private HalftoneScreen? ReadThresholdScreen(PdfStream stream, int width, int height, int width2, int height2, int bytesPerThreshold)
    {
        if (width <= 0 || height <= 0 || width2 < 0 || height2 < 0)
            return null;

        var count = ((long)width * height) + ((long)width2 * height2);
        if (count > int.MaxValue / bytesPerThreshold)
            return null;

        var data = stream.GetDecodedData();
        if (data.Length < count * bytesPerThreshold)
            return null;

        _cache.Charge((count / 1024) + 1);
        return bytesPerThreshold == 1
            ? HalftoneScreen.FromRectangles(width, height, width2, height2, index => HalftoneScreen.FromThreshold8(data[index]))
            : HalftoneScreen.FromRectangles(width, height, width2, height2, index => HalftoneScreen.FromThreshold16(BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(2 * index))));
    }

    /// <summary>Reads a type 1 halftone: the screen of its frequency, angle and spot function at the resolution of the device.</summary>
    private HalftoneScreen? ReadSpotFunctionScreen(PdfDictionary dictionary)
    {
        var frequency = dictionary.GetNumber(PdfName.Create("Frequency"), 0);
        var angle = dictionary.GetNumber(PdfName.Create("Angle"), 0);
        if (!double.IsFinite(frequency) || frequency <= 0 || GetSpotFunction(dictionary.GetResolved(PdfName.Create("SpotFunction"))) is not { } spotFunction)
            return null;

        // AccurateScreens is ignored: the cell is rounded to whole pixels, as ordinary halftoning does (10.6.5.2)
        var cellSide = _cache.DeviceResolution / frequency;
        _cache.Charge((long)Math.Min(cellSide * cellSide, int.MaxValue) / 64 + 1);
        return HalftoneScreen.FromSpotFunction(cellSide, angle, spotFunction);
    }

    /// <summary>Gets a spot function: a predefined one by its name, a function of two inputs, or the first of an array that is one of them.</summary>
    private Func<double, double, double>? GetSpotFunction(PdfObject? value)
    {
        switch (value)
        {
            case PdfName name:
                return SpotFunctions.Get(name.Value);

            case PdfArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    if (array.GetResolved(i) is not PdfArray and { } item && GetSpotFunction(item) is { } function)
                        return function;
                }

                return null;

            case PdfDictionary or PdfStream:
                {
                    var function = _cache.GetFunction(value);
                    if (function.InputCount != 2 || function.OutputCount < 1)
                        return null;

                    var output = new double[function.OutputCount];
                    return (x, y) =>
                    {
                        function.Evaluate([x, y], output);
                        return output[0];
                    };
                }

            default:
                return null;
        }
    }
}
