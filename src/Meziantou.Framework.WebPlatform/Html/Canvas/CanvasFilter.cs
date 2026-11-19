using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Rendering;
using Meziantou.Framework.WebPlatform.Scripting;

#pragma warning disable WEBPLATEXP0021 // The members of the proposal use each other

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// A filter of the 2D contexts described by a list of filter primitives (https://github.com/whatwg/html/issues/5621, the
/// <see href="https://github.com/fserb/canvas2D/blob/master/spec/filters.md">explainer</see>): the
/// <see cref="CanvasRenderingContext2DBase.Filter"/> of a context, or the filter of a layer
/// (<see cref="BeginLayerOptions.Filter"/>). The primitives apply in sRGB, in the order of the list, except the noise of
/// <c>turbulence</c>, which is generated in linearRGB as the one of an SVG <c>feTurbulence</c> element (as in Chromium).
/// </summary>
/// <remarks>
/// A primitive is described by a dictionary of strings (<c>record&lt;DOMString, any&gt;</c>) whose <c>name</c> is
/// <c>gaussianBlur</c> (<c>stdDeviation</c>), <c>dropShadow</c> (<c>dx</c>, <c>dy</c>, <c>stdDeviation</c>, <c>floodColor</c>,
/// <c>floodOpacity</c>), <c>colorMatrix</c> (<c>type</c>, <c>values</c>), <c>componentTransfer</c> (<c>funcR</c>,
/// <c>funcG</c>, <c>funcB</c>, <c>funcA</c>: dictionaries of <c>type</c>, <c>tableValues</c>, <c>slope</c>, <c>intercept</c>,
/// <c>amplitude</c>, <c>exponent</c>, <c>offset</c>), <c>convolveMatrix</c> (<c>kernelMatrix</c>, <c>divisor</c>,
/// <c>bias</c>, <c>targetX</c>, <c>targetY</c>, <c>edgeMode</c>, <c>preserveAlpha</c>) or <c>turbulence</c>
/// (<c>baseFrequency</c>, <c>numOctaves</c>, <c>seed</c>, <c>stitchTiles</c>, <c>type</c>), as the attributes of the SVG
/// filter primitives. The values are converted as Chromium converts them: a number is converted as a JavaScript number
/// (<c>null</c> is 0, <c>"10"</c> is 10), and a number of a pair (<c>stdDeviation</c>, <c>baseFrequency</c>) can also be a
/// list of two numbers.
/// </remarks>
/// <seealso href="https://github.com/fserb/canvas2D/blob/master/spec/filters.md#proposal"/>
[IdlInterface("CanvasFilter", Href)]
[Experimental(ExperimentalApis.CanvasFilterObjects, UrlFormat = ExperimentalApis.UrlFormat)]
public sealed class CanvasFilter
{
    private const string Href = "https://github.com/fserb/canvas2D/blob/master/spec/filters.md#proposal";

    // The number of octaves of a turbulence is limited: the amplitude of an octave is half the one of the previous octave,
    // so the octaves after 32 add nothing to the colors
    private const int MaxOctaves = 32;

    private readonly Operation[] _operations;

    /// <summary>Creates a filter from the description of a filter primitive, or of a list of filter primitives.</summary>
    /// <param name="filters">
    /// The description of a filter primitive (an <see cref="IReadOnlyDictionary{TKey, TValue}"/> of strings), or a list of
    /// descriptions applied in this order. The values are null, strings, booleans, numbers, lists or dictionaries of strings.
    /// </param>
    /// <remarks>As in Chromium, a primitive without a <c>name</c>, or with an unknown name, is ignored.</remarks>
    /// <exception cref="TypeErrorException">The description is not valid (e.g. it is not a dictionary or a list, or a number is not finite).</exception>
    /// <seealso href="https://github.com/fserb/canvas2D/blob/master/spec/filters.md#proposal"/>
    [IdlMember("constructor", Href)]
    public CanvasFilter(object? filters)
        : this(filters, GetOperations())
    {
    }

    private CanvasFilter(object? filters, IScriptValueOperations operations)
    {
        // (CanvasFilterInput or sequence<CanvasFilterInput>): an iterable object is a sequence, another object a record
        if (!operations.IsObject(filters))
            throw new TypeErrorException("The filter is not a CanvasFilterInput or a sequence of CanvasFilterInput");

        var inputs = operations.TryToSequence(filters) ?? [filters];

        var parser = new Parser(operations);
        _operations = [.. inputs.Select(parser.ParseOperation)];
    }

    /// <summary>Returns the operations of the realm of the current global object, or the .NET operations.</summary>
    private static IScriptValueOperations GetOperations()
        => GlobalScope.Current is { } global ? global.ScriptEngine?.GetScriptValueOperations(global) ?? DotNetScriptValueOperations.Instance : DotNetScriptValueOperations.Instance;

    /// <summary>
    /// Returns the filter of a layer (<see cref="BeginLayerOptions.Filter"/>) that is not a .NET string: a
    /// <see cref="CanvasFilter"/>, the description of one, or nothing for <c>null</c> and <c>undefined</c>. The other values
    /// (the primitive values of a script) are converted to a string: a CSS filter.
    /// </summary>
    /// <exception cref="TypeErrorException">The description is not valid.</exception>
    internal static (CanvasFilter? Filter, string? CssFilter) FromLayerFilter(object value)
    {
        if (value is CanvasFilter filter)
            return (filter, null);

        var operations = GetOperations();
        if (operations.IsNullOrUndefined(value))
            return default;

        if (!operations.IsObject(value))
            return (null, operations.ToDomString(value));

        return (new CanvasFilter(value, operations), null);
    }

    /// <summary>
    /// Returns the filters of the primitives, or null when there is none. The filters that have a region (the primitives of
    /// SVG) cover <paramref name="area"/> and the drawings that the primitives can move into it.
    /// </summary>
    /// <param name="area">The area of the result that is drawn, in the coordinates of the filter.</param>
    /// <param name="colorSpace">The color space of the canvas.</param>
    internal DisplayFilter[]? GetDisplayFilters(PhysicalRect area, RenderingColorSpace colorSpace)
    {
        if (_operations.Length == 0)
            return null;

        var reach = 1d;
        foreach (var operation in _operations)
        {
            reach += operation.Reach;
        }

        var region = area.Expand(new Sides<double>(reach, reach, reach, reach));
        var filters = new List<DisplayFilter>(_operations.Length);
        foreach (var operation in _operations)
        {
            // On an sRGB canvas, a color matrix is a filter without a region (a graph converts the colors of the other color
            // spaces to sRGB, but its region limits the drawings that a shadow can move onto the canvas)
            var filter = operation is ColorMatrixOperation colorMatrix && colorSpace == RenderingColorSpace.Srgb ? new ColorMatrixFilter(colorMatrix.Matrix) : operation.CreateFilter(region);
            if (filter is not null)
            {
                filters.Add(filter);
            }
        }

        return filters.Count == 0 ? null : [.. filters];
    }

    /// <summary>A filter primitive.</summary>
    private abstract record Operation
    {
        /// <summary>The distance that the primitive can move a drawing.</summary>
        public virtual double Reach => 0;

        /// <summary>Returns the filter of the primitive, or null when it is the identity.</summary>
        public abstract DisplayFilter? CreateFilter(PhysicalRect region);

        /// <summary>Returns a graph of one primitive that reads the source graphic, in sRGB, in a region.</summary>
        protected static FilterGraph CreateGraph(FilterPrimitive primitive, PhysicalRect region)
            => new([primitive with { Inputs = [FilterInput.SourceGraphic], Subregion = region, LinearRgb = false }], region);
    }

    private sealed record GaussianBlurOperation(double DeviationX, double DeviationY) : Operation
    {
        public override double Reach => 3 * Math.Max(DeviationX, DeviationY);

        public override DisplayFilter? CreateFilter(PhysicalRect region) => DeviationX == 0 && DeviationY == 0 ? null : new BlurFilter(DeviationX, DeviationY);
    }

    private sealed record DropShadowOperation(double Dx, double Dy, double DeviationX, double DeviationY, DisplayColor Color) : Operation
    {
        public override double Reach => (3 * Math.Max(DeviationX, DeviationY)) + Math.Max(Math.Abs(Dx), Math.Abs(Dy));

        public override DisplayFilter CreateFilter(PhysicalRect region)
        {
            if (DeviationX == DeviationY)
                return new DropShadowFilter(new PhysicalOffset(Dx, Dy), DeviationX, Color);

            return CreateGraph(new DropShadowPrimitive(Dx, Dy, DeviationX, DeviationY, Color), region);
        }
    }

    private sealed record ColorMatrixOperation(float[] Matrix) : Operation
    {
        // A primitive of a graph, which applies in sRGB on the canvases of the other color spaces (Display P3)
        public override DisplayFilter CreateFilter(PhysicalRect region) => CreateGraph(new ColorMatrixPrimitive(Matrix), region);
    }

    private sealed record ComponentTransferOperation(byte[]? Red, byte[]? Green, byte[]? Blue, byte[]? Alpha) : Operation
    {
        public override DisplayFilter? CreateFilter(PhysicalRect region)
            => Red is null && Green is null && Blue is null && Alpha is null ? null : CreateGraph(new ComponentTransferPrimitive(Red, Green, Blue, Alpha), region);
    }

    private sealed record ConvolveMatrixOperation(ConvolveMatrixPrimitive Primitive) : Operation
    {
        public override double Reach => Math.Max(Primitive.OrderX, Primitive.OrderY);

        public override DisplayFilter CreateFilter(PhysicalRect region) => CreateGraph(Primitive, region);
    }

    private sealed record TurbulenceOperation(TurbulencePrimitive Primitive) : Operation
    {
        // The noise is generated in linearRGB, the default color-interpolation-filters of feTurbulence (WPT
        // html/canvas/element/manual/filters/tentative/canvas-filter-object-turbulence.html)
        public override DisplayFilter CreateFilter(PhysicalRect region) => new FilterGraph([Primitive with { Inputs = [FilterInput.SourceGraphic], Subregion = region, LinearRgb = true }], region);
    }

    /// <summary>A primitive that does nothing (e.g. an unknown primitive, or a convolution with an empty kernel).</summary>
    private sealed record IdentityOperation : Operation
    {
        public static IdentityOperation Instance { get; } = new();

        public override DisplayFilter? CreateFilter(PhysicalRect region) => null;
    }

    /// <summary>Converts the descriptions of the primitives.</summary>
    private sealed class Parser(IScriptValueOperations operations)
    {
        public Operation ParseOperation(object? input)
        {
            // As in Chromium, a primitive without a name, or with an unknown name, is ignored
            var record = ToDictionary(input);
            if (!record.TryGetValue("name", out var nameValue))
                return IdentityOperation.Instance;

            return operations.ToDomString(nameValue) switch
            {
                "gaussianBlur" => ParseGaussianBlur(record),
                "dropShadow" => ParseDropShadow(record),
                "colorMatrix" => ParseColorMatrix(record),
                "componentTransfer" => ParseComponentTransfer(record),
                "convolveMatrix" => ParseConvolveMatrix(record),
                "turbulence" => ParseTurbulence(record),
                _ => IdentityOperation.Instance,
            };
        }

        private Dictionary<string, object?> ToDictionary(object? value)
        {
            var result = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var (key, item) in operations.ToRecord(value))
            {
                result[key] = item;
            }

            return result;
        }

        private GaussianBlurOperation ParseGaussianBlur(Dictionary<string, object?> record)
        {
            if (!record.TryGetValue("stdDeviation", out var value))
                throw new TypeErrorException("The gaussianBlur filter has no stdDeviation");

            var (x, y) = ToNumberPair(value, "stdDeviation");
            return new GaussianBlurOperation(Math.Max(x, 0), Math.Max(y, 0));
        }

        private DropShadowOperation ParseDropShadow(Dictionary<string, object?> record)
        {
            var dx = GetNumber(record, "dx", 2);
            var dy = GetNumber(record, "dy", 2);
            var (deviationX, deviationY) = record.TryGetValue("stdDeviation", out var deviation) ? ToNumberPair(deviation, "stdDeviation") : (2, 2);
            var color = DisplayColor.Black;
            if (record.TryGetValue("floodColor", out var colorValue))
            {
                var text = operations.ToDomString(colorValue);
                if (CanvasColors.Parse(text, element: null) is not { } parsed)
                    throw new TypeErrorException($"'{text}' is not a color");

                color = parsed.Color;
            }

            var opacity = GetNumber(record, "floodOpacity", 1);
            return new DropShadowOperation(dx, dy, Math.Max(deviationX, 0), Math.Max(deviationY, 0), color.MultiplyAlpha(opacity));
        }

        private ColorMatrixOperation ParseColorMatrix(Dictionary<string, object?> record)
        {
            var type = record.TryGetValue("type", out var typeValue) ? operations.ToDomString(typeValue) : "matrix";
            switch (type)
            {
                case "matrix":
                {
                    if (!record.TryGetValue("values", out var values) || ToNumbers(values, "values") is not { Count: 20 } matrix)
                        throw new TypeErrorException("The values of a colorMatrix filter of type 'matrix' must be a list of 20 numbers");

                    return new ColorMatrixOperation([.. matrix.Select(value => (float)value)]);
                }

                case "saturate":
                    return new ColorMatrixOperation(SvgFilterBuilder.CreateSaturateMatrix(GetNumber(record, "values", 1)));

                case "hueRotate":
                    return new ColorMatrixOperation(SvgFilterBuilder.CreateHueRotateMatrix(GetNumber(record, "values", 0)));

                case "luminanceToAlpha":
                    return new ColorMatrixOperation(SvgFilterBuilder.CreateLuminanceToAlphaMatrix());

                default:
                    throw new TypeErrorException($"'{type}' is not a type of colorMatrix filter");
            }
        }

        private ComponentTransferOperation ParseComponentTransfer(Dictionary<string, object?> record)
        {
            return new ComponentTransferOperation(ParseFunction("funcR"), ParseFunction("funcG"), ParseFunction("funcB"), ParseFunction("funcA"));

            byte[]? ParseFunction(string key)
            {
                if (!record.TryGetValue(key, out var value))
                    return null;

                var function = ToDictionary(value);
                var type = function.TryGetValue("type", out var typeValue) ? operations.ToDomString(typeValue) : "identity";
                if (type is not ("identity" or "table" or "discrete" or "linear" or "gamma"))
                    throw new TypeErrorException($"'{type}' is not a type of transfer function");

                List<double> tableValues = function.TryGetValue("tableValues", out var tableValue) ? ToNumbers(tableValue, "tableValues") : [];
                if (type == "identity" || (type is "table" or "discrete" && tableValues.Count == 0))
                    return null;

                return SvgFilterBuilder.CreateTransferTable(type, tableValues.ConvertAll(value => (float)value), GetNumber(function, "slope", 1), GetNumber(function, "intercept", 0), GetNumber(function, "amplitude", 1), GetNumber(function, "exponent", 1), GetNumber(function, "offset", 0));
            }
        }

        private Operation ParseConvolveMatrix(Dictionary<string, object?> record)
        {
            // A non-empty list of rows of the same length (a single empty row is an empty kernel)
            if (!record.TryGetValue("kernelMatrix", out var value) || operations.TryToSequence(value) is not { Count: > 0 } rows)
                throw new TypeErrorException("The kernelMatrix of a convolveMatrix filter must be a list of rows");

            var kernel = new List<double>();
            var orderX = -1;
            foreach (var row in rows)
            {
                var numbers = ToNumbers(row, "kernelMatrix");
                if (orderX >= 0 && numbers.Count != orderX)
                    throw new TypeErrorException("The rows of the kernelMatrix of a convolveMatrix filter must have the same length");

                orderX = numbers.Count;
                kernel.AddRange(numbers);
            }

            var orderY = rows.Count;
            if (orderX == 0 && orderY > 1)
                throw new TypeErrorException("The rows of the kernelMatrix of a convolveMatrix filter must not be empty");

            var sum = kernel.Sum();
            var divisor = GetNumber(record, "divisor", sum == 0 ? 1 : sum);
            var bias = GetNumber(record, "bias", 0);
            var targetX = GetNumber(record, "targetX", Math.Floor(orderX / 2d));
            var targetY = GetNumber(record, "targetY", Math.Floor(orderY / 2d));
            var edgeMode = record.TryGetValue("edgeMode", out var edgeModeValue) ? operations.ToDomString(edgeModeValue) switch
            {
                "duplicate" => FilterEdgeMode.Duplicate,
                "wrap" => FilterEdgeMode.Wrap,
                "none" => FilterEdgeMode.None,
                var edge => throw new TypeErrorException($"'{edge}' is not an edge mode"),
            } : FilterEdgeMode.Duplicate;
            var preserveAlpha = record.TryGetValue("preserveAlpha", out var preserveAlphaValue) && operations.ToBoolean(preserveAlphaValue);

            // An empty kernel, or a target outside of the kernel, does not change the drawings
            if (orderX == 0 || targetX < 0 || targetX >= orderX || targetY < 0 || targetY >= orderY)
                return IdentityOperation.Instance;

            return new ConvolveMatrixOperation(new ConvolveMatrixPrimitive(orderX, orderY, [.. kernel.Select(value => (float)value)], divisor == 0 ? 1 : divisor, bias, (int)targetX, (int)targetY, edgeMode, preserveAlpha));
        }

        private TurbulenceOperation ParseTurbulence(Dictionary<string, object?> record)
        {
            var (frequencyX, frequencyY) = record.TryGetValue("baseFrequency", out var frequency) ? ToNumberPair(frequency, "baseFrequency") : (0, 0);
            if (frequencyX < 0 || frequencyY < 0)
                throw new TypeErrorException("The baseFrequency of a turbulence filter must not be negative");

            var octaves = GetNumber(record, "numOctaves", 1);
            if (octaves < 0)
                throw new TypeErrorException("The numOctaves of a turbulence filter must not be negative");

            var seed = GetNumber(record, "seed", 0);
            var stitchTiles = GetEnumeration(record, "stitchTiles", "noStitch", ["stitch", "noStitch"]) == "stitch";
            var fractalNoise = GetEnumeration(record, "type", "turbulence", ["turbulence", "fractalNoise"]) == "fractalNoise";
            return new TurbulenceOperation(new TurbulencePrimitive(frequencyX, frequencyY, (int)Math.Min(Math.Truncate(octaves), MaxOctaves), seed, stitchTiles, fractalNoise, DisplayMatrix.Identity));
        }

        /// <summary>Returns a finite number (the default value when the entry is missing).</summary>
        private double GetNumber(Dictionary<string, object?> record, string key, double defaultValue)
            => record.TryGetValue(key, out var value) ? ToFiniteNumber(value, key) : defaultValue;

        private string GetEnumeration(Dictionary<string, object?> record, string key, string defaultValue, string[] values)
        {
            if (!record.TryGetValue(key, out var value))
                return defaultValue;

            var text = operations.ToDomString(value);
            if (!values.Contains(text, StringComparer.Ordinal))
                throw new TypeErrorException($"'{text}' is not a valid value of {key}");

            return text;
        }

        private double ToFiniteNumber(object? value, string key)
        {
            var number = operations.ToNumber(value);
            if (!double.IsFinite(number))
                throw new TypeErrorException($"The value of {key} is not a finite number");

            return number;
        }

        /// <summary>Returns a number, used for both values of the pair, or a list of two numbers.</summary>
        private (double X, double Y) ToNumberPair(object? value, string key)
        {
            var number = operations.ToNumber(value);
            if (double.IsFinite(number))
                return (number, number);

            if (operations.TryToSequence(value) is [var x, var y])
                return (ToFiniteNumber(x, key), ToFiniteNumber(y, key));

            throw new TypeErrorException($"The value of {key} is not a number or a list of two numbers");
        }

        /// <summary>Returns a list of finite numbers (https://webidl.spec.whatwg.org/#js-sequence of <c>double</c>).</summary>
        private List<double> ToNumbers(object? value, string key)
        {
            if (operations.TryToSequence(value) is not { } items)
                throw new TypeErrorException($"The value of {key} is not a list of numbers");

            return items.ConvertAll(item => ToFiniteNumber(item, key));
        }
    }
}
