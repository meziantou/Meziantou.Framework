namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The curve set element: one curve per channel (ICC.2:2023 section 11.2.2, ICC.1:2022 section 10.16.2.2). Its curves are
/// segmented curves ('curf'), single sampled curves ('sngf') or sampled calculator curves ('clcf'), copied from the profile.
/// </summary>
internal sealed class IccMpeCurveSet : IccMpeElement
{
    private const uint CurveSegmented = 0x63757266; // 'curf'
    private const uint CurveSingleSampled = 0x736E6766; // 'sngf'
    private const uint CurveSampledCalculator = 0x636C6366; // 'clcf'

    /// <summary>The largest number of samples of a sampled calculator curve: its recommended count is a hint.</summary>
    private const int MaxCalculatorSamples = 65_536;

    private readonly IccMpeCurve[] _curves;

    private IccMpeCurveSet(IccMpeCurve[] curves)
        : base(curves.Length, curves.Length)
    {
        _curves = curves;
        foreach (var curve in curves)
        {
            Cost += curve.Cost;
        }
    }

    public override long Cost { get; }

    public static IccMpeCurveSet Parse(ReadOnlyMemory<byte> element, int inputs, int outputs, IccMpeParseContext context)
    {
        if (inputs != outputs)
            throw context.Invalid("a curve set element must have as many outputs as inputs.");

        if (12 + (8L * inputs) > element.Length)
            throw context.Invalid("the curve positions of a curve set element do not fit in it.");

        // The curves of several channels may share their data: they are read once
        var curves = new IccMpeCurve[inputs];
        var cache = new Dictionary<(int Offset, int Length), IccMpeCurve>();
        for (var i = 0; i < curves.Length; i++)
        {
            var span = element.Span;
            var key = ((int)IccReader.ReadUInt32(span[(12 + (8 * i))..]), (int)IccReader.ReadUInt32(span[(16 + (8 * i))..]));
            if (!cache.TryGetValue(key, out var curve))
            {
                curve = ParseCurve(IccMpeParser.GetPosition(element, 12 + (8 * i), context), context);
                cache[key] = curve;
            }

            curves[i] = curve;
        }

        return new IccMpeCurveSet(curves);
    }

    public override void Apply(ReadOnlySpan<double> input, Span<double> output, Span<double> scratch)
    {
        for (var i = 0; i < output.Length; i++)
        {
            output[i] = _curves[i].Evaluate(input[i]);
        }
    }

    private static IccMpeCurve ParseCurve(ReadOnlyMemory<byte> curve, IccMpeParseContext context)
    {
        var span = curve.Span;
        if (span.Length < 12)
            throw context.Invalid("a curve of a curve set element is shorter than its header.");

        return IccReader.ReadUInt32(span) switch
        {
            CurveSegmented => ParseSegmented(span, context),
            CurveSingleSampled => ParseSingleSampled(span, context),
            CurveSampledCalculator => ParseSampledCalculator(curve, context),
            var type => throw context.Invalid($"a curve of a curve set element has the unknown type {IccReader.FormatSignature(type)}."),
        };
    }

    // ICC.2:2023 section 11.2.2.4: N segments separated by N - 1 break-points, formulas at both ends
    private static IccMpeSegmentedCurve ParseSegmented(ReadOnlySpan<byte> curve, IccMpeParseContext context)
    {
        int count = IccReader.ReadUInt16(curve[8..]);
        if (count == 0 || 12 + (4L * (count - 1)) > curve.Length)
            throw context.Invalid(string.Create(CultureInfo.InvariantCulture, $"a segmented curve declares {count} segments, which do not fit in it."));

        context.Allocate(count - 1);
        var breakPoints = new double[count - 1];
        for (var i = 0; i < breakPoints.Length; i++)
        {
            breakPoints[i] = IccMpeParser.ReadFiniteSingle(curve[(12 + (4 * i))..], context, "break-point of a segmented curve");
            if (i > 0 && breakPoints[i] < breakPoints[i - 1])
                throw context.Invalid("the break-points of a segmented curve are decreasing.");
        }

        var segments = new IccMpeSegment[count];
        var position = 12 + (4 * (count - 1));
        for (var i = 0; i < segments.Length; i++)
        {
            if (position + 12 > curve.Length)
                throw context.Invalid("the segments of a segmented curve do not fit in it.");

            var segment = curve[position..];
            var type = IccReader.ReadUInt32(segment);
            if (type == 0x70617266) // 'parf'
            {
                int function = IccReader.ReadUInt16(segment[8..]);
                var parameterCount = function switch
                {
                    0 => 4,
                    1 or 2 or 3 or 4 => 5,
                    5 or 7 => 6,
                    6 => 7,
                    _ => throw context.Invalid(string.Create(CultureInfo.InvariantCulture, $"a curve segment has the unknown function type {function}.")),
                };

                if (position + 12 + (4 * parameterCount) > curve.Length)
                    throw context.Invalid("the parameters of a curve segment do not fit in its curve.");

                context.Allocate(parameterCount);
                var parameters = new double[parameterCount];
                for (var j = 0; j < parameters.Length; j++)
                {
                    parameters[j] = IccMpeParser.ReadFiniteSingle(segment[(12 + (4 * j))..], context, "parameter of a curve segment");
                }

                segments[i] = new IccMpeFormulaSegment(function, parameters);
                position += 12 + (4 * parameterCount);
            }
            else if (type == 0x73616D66) // 'samf'
            {
                // The first and the last segments extend to infinity: only a formula can (section 11.2.2.4)
                if (i == 0 || i == segments.Length - 1)
                    throw context.Invalid("the first or the last segment of a segmented curve is a sampled segment instead of a formula.");

                var samples = IccReader.ReadUInt32(segment[8..]);
                if (samples == 0 || position + 12 + (4L * samples) > curve.Length)
                    throw context.Invalid(string.Create(CultureInfo.InvariantCulture, $"a sampled curve segment declares {samples} entries, which do not fit in its curve."));

                context.Allocate(samples);
                var values = new double[samples];
                for (var j = 0; j < values.Length; j++)
                {
                    values[j] = IccMpeParser.ReadFiniteSingle(segment[(12 + (4 * j))..], context, "entry of a sampled curve segment");
                }

                // The value at the first break-point of the segment is the one of the previous segment there
                var start = breakPoints[i - 1];
                segments[i] = new IccMpeSampledSegment(start, breakPoints[i], segments[i - 1].Evaluate(start), values);
                position += 12 + (4 * values.Length);
            }
            else
            {
                throw context.Invalid($"a curve segment has the unknown type {IccReader.FormatSignature(type)}.");
            }
        }

        return new IccMpeSegmentedCurve(breakPoints, segments);
    }

    // ICC.2:2023 section 11.2.2.2
    private static IccMpeTableCurve ParseSingleSampled(ReadOnlySpan<byte> curve, IccMpeParseContext context)
    {
        if (curve.Length < 24)
            throw context.Invalid("a single sampled curve is shorter than its header.");

        var count = IccReader.ReadUInt32(curve[8..]);
        var first = IccMpeParser.ReadFiniteSingle(curve[12..], context, "input range of a single sampled curve");
        var last = IccMpeParser.ReadFiniteSingle(curve[16..], context, "input range of a single sampled curve");
        int extension = IccReader.ReadUInt16(curve[20..]);
        uint encoding = IccReader.ReadUInt16(curve[22..]);
        var size = IccMpeParser.GetValueSize(encoding);
        if (count < 2 || !(first < last) || extension > 1 || size == 0)
            throw context.Invalid("a single sampled curve has fewer than two entries, an empty input range, or an unknown extension or encoding.");

        if (24 + ((long)count * size) > curve.Length)
            throw context.Invalid(string.Create(CultureInfo.InvariantCulture, $"a single sampled curve declares {count} entries, which do not fit in it."));

        context.Allocate(count);
        var values = new double[count];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = IccMpeParser.ReadValue(curve[(24 + (i * size))..], encoding);
            if (!double.IsFinite(values[i]))
                throw context.Invalid("an entry of a single sampled curve is not a finite number.");
        }

        return new IccMpeTableCurve(first, last, extension == 1, values);
    }

    // ICC.2:2023 section 11.2.2.3: the calculator is sampled once into a table
    private static IccMpeTableCurve ParseSampledCalculator(ReadOnlyMemory<byte> curve, IccMpeParseContext context)
    {
        var span = curve.Span;
        if (span.Length < 24)
            throw context.Invalid("a sampled calculator curve is shorter than its header.");

        var count = IccReader.ReadUInt32(span[8..]);
        var first = IccMpeParser.ReadFiniteSingle(span[12..], context, "input range of a sampled calculator curve");
        var last = IccMpeParser.ReadFiniteSingle(span[16..], context, "input range of a sampled calculator curve");
        int extension = IccReader.ReadUInt16(span[20..]);
        if (count < 2 || !(first < last) || extension > 1)
            throw context.Invalid("a sampled calculator curve has fewer than two samples, an empty input range, or an unknown extension.");

        if (span.Length < 36 || IccReader.ReadUInt32(span[24..]) != IccMpeParser.ElementCalculator)
            throw context.Invalid("the function of a sampled calculator curve is not a calculator element.");

        if (IccMpeParser.ParseElement(curve[24..], context) is not IccMpeCalculator calculator || calculator.InputCount != 1 || calculator.OutputCount != 1)
            throw context.Invalid("the calculator element of a sampled calculator curve must have one input and one output.");

        // The calculator curves of the tag are sampled within a bounded number of operations
        var samples = (int)Math.Min(count, MaxCalculatorSamples);
        context.SamplingCost += samples * calculator.Cost;
        if (context.SamplingCost > IccMpeParseContext.MaxCost * 64)
            throw context.Unsupported(string.Create(CultureInfo.InvariantCulture, $"sampling the calculator curves at {samples} points needs too many operations."), "ICC processing elements too costly to evaluate");

        context.Allocate(samples + calculator.ScratchSize);
        var values = new double[samples];

        Span<double> input = stackalloc double[1];
        Span<double> output = stackalloc double[1];
        var scratch = new double[calculator.ScratchSize];
        for (var i = 0; i < values.Length; i++)
        {
            input[0] = first + ((last - first) * i / (values.Length - 1));
            calculator.Apply(input, output, scratch);
            values[i] = output[0];
        }

        return new IccMpeTableCurve(first, last, extension == 1, values);
    }
}
