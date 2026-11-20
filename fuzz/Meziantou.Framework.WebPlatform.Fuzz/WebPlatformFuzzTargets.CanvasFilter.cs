using System.Text;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.WebPlatform.Dom;

#pragma warning disable WEBPLATEXP0017 // The target exercises the canvas layers, a proposal
#pragma warning disable WEBPLATEXP0021 // The target exercises the CanvasFilter objects, a proposal

namespace Meziantou.Framework.WebPlatform.Fuzz;

/// <summary>
/// The target of the descriptions of the <see cref="CanvasFilter"/> objects: the input is decoded to a list of filter
/// primitives (dictionaries of values: numbers, strings, booleans, null, lists and dictionaries). An invalid description
/// throws a <see cref="TypeErrorException"/> (no other exception), the constructor and <c>beginLayer()</c> agree on which
/// descriptions are invalid, and a valid filter is the filter of the context and filters its drawings.
/// </summary>
public static partial class WebPlatformFuzzTargets
{
    // The kinds of the values: one byte each, followed by the bytes of the value
    private const byte FilterValueNull = 0;
    private const byte FilterValueNumber = 1;
    private const byte FilterValueBoolean = 2;
    private const byte FilterValueString = 3;
    private const byte FilterValueList = 4;
    private const byte FilterValueDictionary = 5;
    private const byte FilterValueText = 6;
    private const byte FilterValueKindCount = 7;

    private const int FilterMaxDepth = 4;
    private const int FilterMaxLength = 6;

    private static readonly string[] CanvasFilterNames = ["gaussianBlur", "dropShadow", "colorMatrix", "componentTransfer", "convolveMatrix", "turbulence", "unknown"];

    private static readonly string[] CanvasFilterKeys =
    [
        "stdDeviation", "dx", "dy", "floodColor", "floodOpacity", "type", "values", "funcR", "funcG", "funcB", "funcA", "tableValues",
        "slope", "intercept", "amplitude", "exponent", "offset", "kernelMatrix", "divisor", "bias", "targetX", "targetY", "edgeMode",
        "preserveAlpha", "baseFrequency", "numOctaves", "seed", "stitchTiles", "name",
    ];

    private static readonly double[] CanvasFilterNumbers = [0, 1, -1, 0.5, 2, 3, 10, 20, 255, 1e300, -0.0, double.NaN, double.PositiveInfinity, double.NegativeInfinity];

    private static readonly string[] CanvasFilterStrings =
    [
        "", "10", " 0x1F ", "-Infinity", "1e3", "foo", "red", "canvas", "rgba(4, -3, 0.5, 1)", "#abcd", "currentcolor", "matrix", "saturate",
        "hueRotate", "luminanceToAlpha", "identity", "table", "discrete", "linear", "gamma", "duplicate", "wrap", "none", "stitch",
        "noStitch", "turbulence", "fractalNoise",
    ];

    /// <summary>The tokens of the target: a primitive with each name, a pair of numbers, and nested values.</summary>
    private static readonly string[] CanvasFilterTokens = ["\u0001\u0000", "\u0001\u0001", "\u0001\u0004", "\u0004\u0002\u0001\u0003\u0001\u0004", "\u0005\u0001", "\u0006\u0003"];

    /// <summary>Decodes the input to the description of a filter, then checks the CanvasFilter and the layer of this description.</summary>
    public static void CanvasFilterDescription(ReadOnlySpan<byte> data)
    {
        var position = 0;
        var description = ReadCanvasFilterDescription(data, ref position);

        CanvasFilter? filter;
        try
        {
            filter = new CanvasFilter(description);
        }
        catch (TypeErrorException)
        {
            filter = null;
        }

        var context = FuzzCanvasContext;
        context.Reset();

        // beginLayer() accepts the description of a CanvasFilter: it throws when the constructor throws, before opening a layer
        var layerThrows = false;
        try
        {
            context.BeginLayer(new BeginLayerOptions { Filter = description });
        }
        catch (TypeErrorException)
        {
            layerThrows = true;
        }

        FuzzAssert.Equal(filter is null, layerThrows, "the constructor and beginLayer() agree on the validity of the description");
        if (!layerThrows)
        {
            context.FillRect(2, 2, 8, 8);
            context.EndLayer();
        }

        if (filter is null)
            return;

        context.Filter = filter;
        FuzzAssert.True(ReferenceEquals(filter, context.Filter), "the filter of the context is the CanvasFilter");
        context.Filter = "not a filter";
        FuzzAssert.True(ReferenceEquals(filter, context.Filter), "an invalid CSS filter does not replace the CanvasFilter");
        context.FillRect(4, 4, 8, 8);
        _ = context.GetImageData(0, 0, CanvasSize, CanvasSize);
    }

    /// <summary>Reads a list of primitives: each one is a dictionary with a name (an index of <see cref="CanvasFilterNames"/>) and entries.</summary>
    private static object ReadCanvasFilterDescription(ReadOnlySpan<byte> data, ref int position)
    {
        var count = NextFilterByte(data, ref position) % FilterMaxLength;
        var primitives = new List<object?>(count);
        for (var i = 0; i < count && position < data.Length; i++)
        {
            var primitive = new Dictionary<string, object?>(StringComparer.Ordinal) { ["name"] = CanvasFilterNames[NextFilterByte(data, ref position) % CanvasFilterNames.Length] };
            var entries = NextFilterByte(data, ref position) % FilterMaxLength;
            for (var j = 0; j < entries; j++)
            {
                primitive[CanvasFilterKeys[NextFilterByte(data, ref position) % CanvasFilterKeys.Length]] = ReadCanvasFilterValue(data, ref position, depth: 0);
            }

            primitives.Add(primitive);
        }

        // A single primitive is also a description
        return primitives is [var single] ? single! : primitives;
    }

    private static object? ReadCanvasFilterValue(ReadOnlySpan<byte> data, ref int position, int depth)
    {
        var kind = NextFilterByte(data, ref position) % FilterValueKindCount;
        if (depth >= FilterMaxDepth && kind is FilterValueList or FilterValueDictionary)
        {
            kind = FilterValueNumber;
        }

        switch (kind)
        {
            case FilterValueNull:
                return null;

            case FilterValueNumber:
                return CanvasFilterNumbers[NextFilterByte(data, ref position) % CanvasFilterNumbers.Length];

            case FilterValueBoolean:
                return NextFilterByte(data, ref position) % 2 == 1;

            case FilterValueString:
                return CanvasFilterStrings[NextFilterByte(data, ref position) % CanvasFilterStrings.Length];

            case FilterValueList:
            {
                var count = NextFilterByte(data, ref position) % FilterMaxLength;
                var list = new List<object?>(count);
                for (var i = 0; i < count; i++)
                {
                    list.Add(ReadCanvasFilterValue(data, ref position, depth + 1));
                }

                return list;
            }

            case FilterValueDictionary:
            {
                var count = NextFilterByte(data, ref position) % FilterMaxLength;
                var dictionary = new Dictionary<string, object?>(StringComparer.Ordinal);
                for (var i = 0; i < count; i++)
                {
                    dictionary[CanvasFilterKeys[NextFilterByte(data, ref position) % CanvasFilterKeys.Length]] = ReadCanvasFilterValue(data, ref position, depth + 1);
                }

                return dictionary;
            }

            default:
            {
                // A string of the bytes of the input (the numbers and the colors that the pools do not have)
                var length = Math.Min(NextFilterByte(data, ref position) % 16, data.Length - position);
                var text = Encoding.Latin1.GetString(data.Slice(position, length));
                position += length;
                return text;
            }
        }
    }

    private static byte NextFilterByte(ReadOnlySpan<byte> data, ref int position) => position < data.Length ? data[position++] : (byte)0;

    /// <summary>The seeds: the descriptions of the unit tests and of the web-platform-tests.</summary>
    private static IEnumerable<FuzzSeed> GetCanvasFilterSeeds()
    {
        byte[][] scenarios =
        [
            // {name: 'colorMatrix', type: 'hueRotate', values: 90} (type and values are keys 5 and 6)
            [1, 2, 2, 5, FilterValueString, 13, 6, FilterValueNumber, 6],

            // A drop shadow, then a component transfer of the alpha with a table [0, 1]
            [2, 1, 3, 1, FilterValueNumber, 6, 2, FilterValueNumber, 1, 3, FilterValueString, 6, 3, 1, 10, FilterValueDictionary, 2, 5, FilterValueString, 16, 11, FilterValueList, 2, FilterValueNumber, 0, FilterValueNumber, 1],

            // A blur with a pair of deviations, and a deviation that is not a number
            [1, 0, 1, 0, FilterValueList, 2, FilterValueNumber, 4, FilterValueText, 1, (byte)'2'],
            [1, 0, 1, 0, FilterValueString, 5],

            // A convolution with a 3x3 kernel, and with rows of different lengths
            [1, 4, 1, 17, FilterValueList, 3, FilterValueList, 3, FilterValueNumber, 0, FilterValueNumber, 2, FilterValueNumber, 0, FilterValueList, 3, FilterValueNumber, 2, FilterValueNumber, 1, FilterValueNumber, 2, FilterValueList, 3, FilterValueNumber, 0, FilterValueNumber, 2, FilterValueNumber, 0],
            [1, 4, 1, 17, FilterValueList, 2, FilterValueList, 2, FilterValueNumber, 1, FilterValueNumber, 0, FilterValueList, 1, FilterValueNumber, 0],

            // A turbulence, and a negative frequency
            [1, 5, 4, 24, FilterValueNumber, 3, 25, FilterValueNumber, 4, 26, FilterValueNumber, 5, 27, FilterValueString, 23],
            [1, 5, 1, 24, FilterValueNumber, 2],

            // A primitive with an unknown name, and a description without primitives
            [1, 6, 0],
            [0],
        ];

        for (var i = 0; i < scenarios.Length; i++)
        {
            yield return new FuzzSeed("canvas-filter-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), scenarios[i]);
        }
    }
}
