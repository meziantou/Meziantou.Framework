using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The tint array element: the output for a tint in [0, 1], interpolated linearly between equally spaced vectors
/// (ICC.2:2023 section 11.2.14).
/// </summary>
internal sealed class IccMpeTintArray : IccMpeElement
{
    private readonly double[] _values;
    private readonly int _vectors;

    private IccMpeTintArray(int outputs, double[] values)
        : base(1, outputs)
    {
        _values = values;
        _vectors = values.Length / outputs;
    }

    public static IccMpeTintArray Parse(ReadOnlySpan<byte> element, int inputs, int outputs, IccMpeParseContext context)
    {
        if (inputs != 1 || element.Length < 20)
            throw context.Invalid("a tint array element must have one input and a tint encoding.");

        var encoding = IccReader.ReadUInt32(element[12..]);
        var size = IccMpeParser.GetValueSize(encoding);
        if (size == 0)
            throw context.Invalid($"the tint array element has the unknown encoding {IccReader.FormatSignature(encoding)}.");

        var count = (element.Length - 20) / size;
        if (count / outputs < 2)
            throw context.Invalid(string.Create(CultureInfo.InvariantCulture, $"the tint array element has {count} values, fewer than two vectors of {outputs} values."));

        context.Allocate(count);
        var values = new double[count / outputs * outputs];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = IccMpeParser.ReadValue(element[(20 + (i * size))..], encoding);
            if (!double.IsFinite(values[i]))
                throw context.Invalid("a value of the tint array element is not a finite number.");
        }

        return new IccMpeTintArray(outputs, values);
    }

    public override void Apply(ReadOnlySpan<double> input, Span<double> output, Span<double> scratch)
    {
        var position = Math.Clamp(double.IsNaN(input[0]) ? 0 : input[0], 0, 1) * (_vectors - 1);
        var index = Math.Min((int)position, _vectors - 2);
        var fraction = position - index;
        var outputs = OutputCount;
        for (var i = 0; i < outputs; i++)
        {
            var low = _values[(index * outputs) + i];
            var high = _values[((index + 1) * outputs) + i];
            output[i] = low + ((high - low) * fraction);
        }
    }
}
