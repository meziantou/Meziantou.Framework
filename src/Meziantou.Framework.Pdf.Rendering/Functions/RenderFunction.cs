using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Rendering.Functions;

/// <summary>A function of a document (ISO 32000-2, 7.10), evaluated: sampled, exponential, stitching or PostScript calculator.</summary>
internal abstract class RenderFunction
{
    // The nesting of stitching functions
    private const int MaxDepth = 16;

    // The samples of a sampled function
    private const long MaxSamples = 1 << 24;

    /// <summary>The largest number of inputs or outputs of a function, and of colorants of a color space: their values are on the stack.</summary>
    public const int MaxComponents = 32;

    private readonly double[] _domain;
    private readonly double[]? _range;

    private protected RenderFunction(double[] domain, double[]? range)
    {
        _domain = domain;
        _range = range;
    }

    public int InputCount => _domain.Length / 2;

    public abstract int OutputCount { get; }

    /// <summary>Reads a function, or an array of functions of one output each that act as one function.</summary>
    public static RenderFunction Read(PdfObject? value, int depth = 0)
    {
        if (depth > MaxDepth)
            throw new InvalidPdfContentException("The functions are nested too deeply");

        value = value is null ? null : PdfReference.ResolveIfReference(value);
        if (value is PdfArray array)
        {
            if (array.Count is 0 or > MaxComponents)
                throw new InvalidPdfContentException("An array of functions has no function or too many functions");

            var functions = new RenderFunction[array.Count];
            for (var i = 0; i < array.Count; i++)
            {
                functions[i] = Read(array.GetResolved(i), depth + 1);
            }

            return new ArrayFunction(functions);
        }

        var dictionary = value switch
        {
            PdfStream stream => stream.Dictionary,
            PdfDictionary d => d,
            _ => throw new InvalidPdfContentException("A function is not a dictionary or a stream"),
        };
        var domain = ReadNumbers(dictionary.Get<PdfArray>(PdfName.Create("Domain"))) ?? throw new InvalidPdfContentException("A function has no /Domain");
        if (domain.Length < 2 || domain.Length % 2 != 0 || domain.Length / 2 > MaxComponents)
            throw new InvalidPdfContentException("The /Domain of a function is invalid");

        var range = ReadNumbers(dictionary.Get<PdfArray>(PdfName.Create("Range")));
        if (range is { Length: 0 } || range?.Length % 2 != 0)
        {
            range = null;
        }

        if (range?.Length / 2 > MaxComponents)
            throw new InvalidPdfContentException("A function has too many outputs");

        return dictionary.GetInteger(PdfName.Create("FunctionType"), -1) switch
        {
            0 when value is PdfStream stream => ReadSampled(stream, domain, range),
            2 => ReadExponential(dictionary, domain, range),
            3 => ReadStitching(dictionary, domain, range, depth),
            4 when value is PdfStream stream && range is not null => new PostScriptFunction(PostScriptProgram.Parse(stream.GetDecodedData()), domain, range, range.Length / 2),
            _ => throw new InvalidPdfContentException("Unknown function type"),
        };
    }

    /// <summary>Evaluates the function: the inputs are clipped to the domain, the outputs to the range.</summary>
    public void Evaluate(ReadOnlySpan<double> inputs, Span<double> outputs)
    {
        Span<double> clipped = stackalloc double[Math.Max(InputCount, 1)];
        for (var i = 0; i < InputCount; i++)
        {
            var value = i < inputs.Length ? inputs[i] : 0;
            clipped[i] = Math.Clamp(double.IsNaN(value) ? _domain[2 * i] : value, Math.Min(_domain[2 * i], _domain[(2 * i) + 1]), Math.Max(_domain[2 * i], _domain[(2 * i) + 1]));
        }

        outputs.Clear();
        EvaluateCore(clipped[..InputCount], outputs);
        if (_range is not null)
        {
            for (var i = 0; i < Math.Min(outputs.Length, _range.Length / 2); i++)
            {
                outputs[i] = Math.Clamp(double.IsNaN(outputs[i]) ? _range[2 * i] : outputs[i], Math.Min(_range[2 * i], _range[(2 * i) + 1]), Math.Max(_range[2 * i], _range[(2 * i) + 1]));
            }
        }
    }

    private protected abstract void EvaluateCore(ReadOnlySpan<double> inputs, Span<double> outputs);

    private protected double DomainStart(int input) => _domain[2 * input];

    private protected double DomainEnd(int input) => _domain[(2 * input) + 1];

    internal static double[]? ReadNumbers(PdfArray? array)
    {
        if (array is null)
            return null;

        var result = new double[array.Count];
        for (var i = 0; i < array.Count; i++)
        {
            result[i] = array.GetNumber(i);
        }

        return result;
    }

    private static double Interpolate(double x, double xMin, double xMax, double yMin, double yMax) => xMax == xMin ? yMin : yMin + ((x - xMin) * (yMax - yMin) / (xMax - xMin));

    private static SampledFunction ReadSampled(PdfStream stream, double[] domain, double[]? range)
    {
        var dictionary = stream.Dictionary;
        if (range is null)
            throw new InvalidPdfContentException("A sampled function has no /Range");

        var size = ReadNumbers(dictionary.Get<PdfArray>(PdfName.Create("Size"))) ?? throw new InvalidPdfContentException("A sampled function has no /Size");
        var inputs = domain.Length / 2;
        var outputs = range.Length / 2;
        if (size.Length != inputs)
            throw new InvalidPdfContentException("The /Size of a sampled function does not match its /Domain");

        var sizes = new int[inputs];
        long count = outputs;
        for (var i = 0; i < inputs; i++)
        {
            if (size[i] < 1 || size[i] > MaxSamples)
                throw new InvalidPdfContentException("The /Size of a sampled function is invalid");

            sizes[i] = (int)size[i];
            count *= sizes[i];
            if (count > MaxSamples)
                throw new PdfResourceLimitException("A sampled function has too many samples");
        }

        var bitsPerSample = (int)dictionary.GetInteger(PdfName.Create("BitsPerSample"), 8);
        if (bitsPerSample is not (1 or 2 or 4 or 8 or 12 or 16 or 24 or 32))
            throw new InvalidPdfContentException("The /BitsPerSample of a sampled function is invalid");

        var encode = ReadNumbers(dictionary.Get<PdfArray>(PdfName.Create("Encode")));
        if (encode is null || encode.Length != inputs * 2)
        {
            encode = new double[inputs * 2];
            for (var i = 0; i < inputs; i++)
            {
                encode[(2 * i) + 1] = sizes[i] - 1;
            }
        }

        var decode = ReadNumbers(dictionary.Get<PdfArray>(PdfNames.Decode));
        if (decode is null || decode.Length != outputs * 2)
        {
            decode = range;
        }

        // The samples, as doubles from 0 to 1
        var data = stream.GetDecodedData();
        var samples = new double[count];
        var maxValue = bitsPerSample == 32 ? uint.MaxValue : (1UL << bitsPerSample) - 1;
        long bitPosition = 0;
        for (var i = 0; i < count; i++)
        {
            ulong value = 0;
            for (var b = 0; b < bitsPerSample; b++, bitPosition++)
            {
                var index = bitPosition >> 3;
                var bit = index < data.Length ? (data[index] >> (7 - (int)(bitPosition & 7))) & 1 : 0;
                value = (value << 1) | (uint)bit;
            }

            samples[i] = value / (double)maxValue;
        }

        return new SampledFunction(domain, range, sizes, encode, decode, samples);
    }

    private static ExponentialFunction ReadExponential(PdfDictionary dictionary, double[] domain, double[]? range)
    {
        var c0 = ReadNumbers(dictionary.Get<PdfArray>(PdfName.Create("C0"))) ?? [0];
        var c1 = ReadNumbers(dictionary.Get<PdfArray>(PdfName.Create("C1"))) ?? [1];
        if (c0.Length != c1.Length || c0.Length > MaxComponents)
            throw new InvalidPdfContentException("The /C0 and /C1 of an exponential function do not have the same length, or are too long");

        return new ExponentialFunction(domain, range, c0, c1, dictionary.GetNumber(PdfNames.N, 1));
    }

    private static StitchingFunction ReadStitching(PdfDictionary dictionary, double[] domain, double[]? range, int depth)
    {
        var array = dictionary.Get<PdfArray>(PdfName.Create("Functions")) ?? throw new InvalidPdfContentException("A stitching function has no /Functions");
        var functions = new RenderFunction[array.Count];
        for (var i = 0; i < array.Count; i++)
        {
            functions[i] = Read(array.GetResolved(i), depth + 1);
        }

        if (functions.Length == 0)
            throw new InvalidPdfContentException("A stitching function has no function");

        var bounds = ReadNumbers(dictionary.Get<PdfArray>(PdfName.Create("Bounds"))) ?? [];
        var encode = ReadNumbers(dictionary.Get<PdfArray>(PdfName.Create("Encode"))) ?? [];
        if (bounds.Length != functions.Length - 1)
            throw new InvalidPdfContentException("The /Bounds of a stitching function do not match its /Functions");

        if (encode.Length < functions.Length * 2)
        {
            encode = [.. Enumerable.Range(0, functions.Length).SelectMany(_ => new double[] { 0, 1 })];
        }

        return new StitchingFunction(domain, range, functions, bounds, encode);
    }

    private sealed class ArrayFunction(RenderFunction[] functions) : RenderFunction([0, 1], range: null)
    {
        public override int OutputCount => functions.Length;

        private protected override void EvaluateCore(ReadOnlySpan<double> inputs, Span<double> outputs)
        {
            Span<double> one = stackalloc double[Math.Max(1, functions.Max(function => function.OutputCount))];
            for (var i = 0; i < functions.Length && i < outputs.Length; i++)
            {
                functions[i].Evaluate(inputs, one);
                outputs[i] = one[0];
            }
        }
    }

    private sealed class SampledFunction(double[] domain, double[]? range, int[] sizes, double[] encode, double[] decode, double[] samples) : RenderFunction(domain, range)
    {
        public override int OutputCount => decode.Length / 2;

        private protected override void EvaluateCore(ReadOnlySpan<double> inputs, Span<double> outputs)
        {
            // The position of the inputs in the table of samples, and a multilinear interpolation between the samples
            // around it (7.10.2)
            var inputCount = sizes.Length;
            Span<int> lower = stackalloc int[inputCount];
            Span<double> fraction = stackalloc double[inputCount];
            for (var i = 0; i < inputCount; i++)
            {
                var e = Interpolate(inputs[i], DomainStart(i), DomainEnd(i), encode[2 * i], encode[(2 * i) + 1]);
                e = Math.Clamp(e, 0, sizes[i] - 1);
                var floor = (int)Math.Floor(e);
                if (floor >= sizes[i] - 1)
                {
                    floor = Math.Max(0, sizes[i] - 2);
                }

                lower[i] = floor;
                fraction[i] = sizes[i] == 1 ? 0 : e - floor;
            }

            var outputCount = OutputCount;
            Span<double> sum = stackalloc double[outputCount];
            var corners = inputCount <= 8 ? 1 << inputCount : 1;
            for (var corner = 0; corner < corners; corner++)
            {
                var weight = 1.0;
                long index = 0;
                long stride = 1;
                for (var i = 0; i < inputCount; i++)
                {
                    var high = (corner >> i) & 1;
                    weight *= high == 1 ? fraction[i] : 1 - fraction[i];
                    var position = Math.Min(lower[i] + high, sizes[i] - 1);
                    index += position * stride;
                    stride *= sizes[i];
                }

                if (weight == 0)
                    continue;

                for (var o = 0; o < outputCount; o++)
                {
                    var sampleIndex = (index * outputCount) + o;
                    sum[o] += weight * (sampleIndex < samples.Length ? samples[sampleIndex] : 0);
                }
            }

            for (var o = 0; o < outputCount && o < outputs.Length; o++)
            {
                outputs[o] = decode[2 * o] + (sum[o] * (decode[(2 * o) + 1] - decode[2 * o]));
            }
        }
    }

    private sealed class ExponentialFunction(double[] domain, double[]? range, double[] c0, double[] c1, double exponent) : RenderFunction(domain, range)
    {
        public override int OutputCount => c0.Length;

        private protected override void EvaluateCore(ReadOnlySpan<double> inputs, Span<double> outputs)
        {
            var x = inputs.Length > 0 ? inputs[0] : 0;
            var power = exponent == 1 ? x : Math.Pow(x, exponent);
            for (var i = 0; i < c0.Length && i < outputs.Length; i++)
            {
                outputs[i] = c0[i] + (power * (c1[i] - c0[i]));
            }
        }
    }

    private sealed class StitchingFunction(double[] domain, double[]? range, RenderFunction[] functions, double[] bounds, double[] encode) : RenderFunction(domain, range)
    {
        public override int OutputCount => functions[0].OutputCount;

        private protected override void EvaluateCore(ReadOnlySpan<double> inputs, Span<double> outputs)
        {
            var x = inputs.Length > 0 ? inputs[0] : 0;
            var index = 0;
            while (index < bounds.Length && x >= bounds[index])
            {
                index++;
            }

            var low = index == 0 ? DomainStart(0) : bounds[index - 1];
            var high = index == bounds.Length ? DomainEnd(0) : bounds[index];
            Span<double> input = [Interpolate(x, low, high, encode[2 * index], encode[(2 * index) + 1])];
            functions[index].Evaluate(input, outputs);
        }
    }

    private sealed class PostScriptFunction(PostScriptProgram program, double[] domain, double[] range, int outputCount) : RenderFunction(domain, range)
    {
        public override int OutputCount => outputCount;

        private protected override void EvaluateCore(ReadOnlySpan<double> inputs, Span<double> outputs) => program.Execute(inputs, outputs);
    }
}
