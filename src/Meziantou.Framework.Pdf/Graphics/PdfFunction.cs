using System.Text;
using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>
/// A function (ISO 32000-2, 7.10): it maps <c>m</c> inputs to <c>n</c> outputs, such as the position in a gradient to
/// the components of its color. A function does not belong to a document: each document that uses it gets its own
/// object. The instances are immutable.
/// </summary>
public abstract class PdfFunction
{
    private protected PdfFunction(double[] domain, double[]? range)
    {
        if (domain.Length == 0 || domain.Length % 2 != 0)
            throw new ArgumentException("The domain must have two values per input", nameof(domain));

        if (range is not null && range.Length % 2 != 0)
            throw new ArgumentException("The range must have two values per output", nameof(range));

        CheckFinite(domain, nameof(domain));
        if (range is not null)
        {
            CheckFinite(range, nameof(range));
        }

        Domain = domain;
        Range = range;
    }

    /// <summary>Gets the number of inputs.</summary>
    public int InputCount => Domain.Length / 2;

    /// <summary>Gets the number of outputs.</summary>
    public abstract int OutputCount { get; }

    internal double[] Domain { get; }

    internal double[]? Range { get; }

    /// <summary>Creates an exponential interpolation function (type 2, 7.10.3): <c>C0 + x^N × (C1 − C0)</c>, with one input from 0 to 1.</summary>
    /// <param name="c0">The outputs at 0.</param>
    /// <param name="c1">The outputs at 1; the same number of values as <paramref name="c0"/>.</param>
    /// <param name="exponent">The interpolation exponent <c>N</c>; 1 for a linear interpolation.</param>
    /// <returns>The function.</returns>
    public static PdfFunction Exponential(ReadOnlySpan<double> c0, ReadOnlySpan<double> c1, double exponent = 1)
    {
        if (c0.Length == 0 || c0.Length != c1.Length)
            throw new ArgumentException("C0 and C1 must have the same number of values", nameof(c1));

        if (!double.IsFinite(exponent))
            throw new ArgumentOutOfRangeException(nameof(exponent), exponent, "The exponent must be finite");

        return new ExponentialFunction(c0.ToArray(), c1.ToArray(), exponent);
    }

    /// <summary>Creates an exponential interpolation function between two colors of the same color space.</summary>
    /// <param name="start">The color at 0.</param>
    /// <param name="end">The color at 1.</param>
    /// <returns>The function.</returns>
    public static PdfFunction Exponential(PdfColor start, PdfColor end)
    {
        if (start.ColorSpace != end.ColorSpace)
            throw new ArgumentException("The colors must be in the same color space", nameof(end));

        return Exponential(start.GetComponents(), end.GetComponents());
    }

    /// <summary>
    /// Creates a stitching function (type 3, 7.10.4): the domain is split by the bounds into intervals, each mapped to one
    /// of the functions.
    /// </summary>
    /// <param name="functions">The functions of the intervals: one input each, the same number of outputs.</param>
    /// <param name="bounds">The values that split the domain, in increasing order: one less than the functions.</param>
    /// <param name="encode">The interval of the input of each function the interval of the domain is mapped to: two values per function; <see langword="null"/> for <c>[0 1]</c> each.</param>
    /// <param name="domainStart">The start of the domain.</param>
    /// <param name="domainEnd">The end of the domain.</param>
    /// <returns>The function.</returns>
    public static PdfFunction Stitching(IReadOnlyList<PdfFunction> functions, ReadOnlySpan<double> bounds, ReadOnlySpan<double> encode = default, double domainStart = 0, double domainEnd = 1)
    {
        ArgumentNullException.ThrowIfNull(functions);
        if (functions.Count == 0)
            throw new ArgumentException("A stitching function needs at least one function", nameof(functions));

        if (bounds.Length != functions.Count - 1)
            throw new ArgumentException("There must be one bound less than functions", nameof(bounds));

        if (!encode.IsEmpty && encode.Length != functions.Count * 2)
            throw new ArgumentException("There must be two values per function", nameof(encode));

        var outputs = functions[0].OutputCount;
        foreach (var function in functions)
        {
            ArgumentNullException.ThrowIfNull(function, nameof(functions));
            if (function.InputCount != 1 || function.OutputCount != outputs)
                throw new ArgumentException("The functions must have one input and the same number of outputs", nameof(functions));
        }

        var previous = domainStart;
        foreach (var bound in bounds)
        {
            if (!double.IsFinite(bound) || bound < previous || bound > domainEnd)
                throw new ArgumentException("The bounds must be in increasing order, in the domain", nameof(bounds));

            previous = bound;
        }

        var encodeValues = encode.IsEmpty ? [.. Enumerable.Range(0, functions.Count).SelectMany(_ => new double[] { 0, 1 })] : encode.ToArray();
        CheckFinite(encodeValues, nameof(encode));
        return new StitchingFunction([.. functions], bounds.ToArray(), encodeValues, [domainStart, domainEnd]);
    }

    /// <summary>Creates a stitching function of linear interpolations between colors at positions from 0 to 1: the stops of a gradient.</summary>
    /// <param name="stops">The positions, in increasing order from 0 to 1, and the colors, all in the same color space.</param>
    /// <returns>The function.</returns>
    public static PdfFunction Gradient(params ReadOnlySpan<(double Position, PdfColor Color)> stops)
    {
        if (stops.Length < 2)
            throw new ArgumentException("A gradient needs at least two stops", nameof(stops));

        if (stops.Length == 2 && stops[0].Position == 0 && stops[1].Position == 1)
            return Exponential(stops[0].Color, stops[1].Color);

        var functions = new List<PdfFunction>();
        var bounds = new List<double>();
        for (var i = 0; i + 1 < stops.Length; i++)
        {
            functions.Add(Exponential(stops[i].Color, stops[i + 1].Color));
            if (i > 0)
            {
                bounds.Add(stops[i].Position);
            }
        }

        return Stitching(functions, bounds.ToArray(), default, stops[0].Position, stops[^1].Position);
    }

    /// <summary>
    /// Creates a sampled function (type 0, 7.10.2): a table of samples, interpolated linearly between them.
    /// </summary>
    /// <param name="size">The number of samples in each input dimension, at least 1.</param>
    /// <param name="bitsPerSample">The bits of each sample: 1, 2, 4, 8, 12, 16, 24 or 32.</param>
    /// <param name="domain">Two values per input.</param>
    /// <param name="range">Two values per output: a sample of 0 is the first value, the largest sample the second.</param>
    /// <param name="samples">The samples, the first dimension varying fastest, the outputs of a sample together, packed in big-endian bits.</param>
    /// <returns>The function.</returns>
    public static PdfFunction Sampled(ReadOnlySpan<int> size, int bitsPerSample, ReadOnlySpan<double> domain, ReadOnlySpan<double> range, ReadOnlySpan<byte> samples)
    {
        if (bitsPerSample is not (1 or 2 or 4 or 8 or 12 or 16 or 24 or 32))
            throw new ArgumentOutOfRangeException(nameof(bitsPerSample), bitsPerSample, "The bits per sample must be 1, 2, 4, 8, 12, 16, 24 or 32");

        if (size.Length == 0 || size.Length * 2 != domain.Length)
            throw new ArgumentException("There must be one size per input", nameof(size));

        if (range.Length == 0 || range.Length % 2 != 0)
            throw new ArgumentException("The range must have two values per output", nameof(range));

        long count = range.Length / 2;
        foreach (var value in size)
        {
            if (value < 1)
                throw new ArgumentOutOfRangeException(nameof(size), value, "Each size must be at least 1");

            count *= value;
            if (count > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(size), "The table of samples is too large");
        }

        if (samples.Length < ((count * bitsPerSample) + 7) / 8)
            throw new ArgumentException("The table of samples is shorter than the sizes give", nameof(samples));

        return new SampledFunction(size.ToArray(), bitsPerSample, domain.ToArray(), range.ToArray(), samples.ToArray());
    }

    /// <summary>
    /// Creates a PostScript calculator function (type 4, 7.10.5): a program of the operators of 7.10.5 between braces,
    /// such as <c>{ dup 0.5 mul exch }</c>.
    /// </summary>
    /// <param name="program">The program.</param>
    /// <param name="domain">Two values per input.</param>
    /// <param name="range">Two values per output.</param>
    /// <returns>The function.</returns>
    public static PdfFunction PostScript(string program, ReadOnlySpan<double> domain, ReadOnlySpan<double> range)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(program);
        var trimmed = program.Trim();
        if (trimmed[0] != '{' || trimmed[^1] != '}')
            throw new ArgumentException("The program must be between braces", nameof(program));

        if (range.Length == 0 || range.Length % 2 != 0)
            throw new ArgumentException("The range must have two values per output", nameof(range));

        foreach (var c in trimmed)
        {
            if (c > 0x7E || (c < 0x20 && c is not ('\n' or '\r' or '\t')))
                throw new ArgumentException("The program must be printable ASCII", nameof(program));
        }

        return new PostScriptFunction(trimmed, domain.ToArray(), range.ToArray());
    }

    /// <summary>Gets the object of the function in a document: a dictionary, or a reference to a stream.</summary>
    internal abstract PdfObject GetObject(PdfDocument document);

    /// <summary>Adds the entries every function has: <c>/FunctionType</c>, <c>/Domain</c>, <c>/Range</c>.</summary>
    private protected void AddCommonEntries(PdfDictionary dictionary, int type)
    {
        dictionary[PdfName.Create("FunctionType")] = PdfInteger.Get(type);
        dictionary[PdfName.Create("Domain")] = PdfArray.FromNumbers(Domain);
        if (Range is not null)
        {
            dictionary[PdfName.Create("Range")] = PdfArray.FromNumbers(Range);
        }
    }

    private static void CheckFinite(double[] values, string parameterName)
    {
        foreach (var value in values)
        {
            if (!double.IsFinite(value))
                throw new ArgumentException("The values must be finite", parameterName);
        }
    }

    private sealed class ExponentialFunction(double[] c0, double[] c1, double exponent) : PdfFunction([0, 1], range: null)
    {
        public override int OutputCount => c0.Length;

        internal override PdfObject GetObject(PdfDocument document)
        {
            var dictionary = new PdfDictionary();
            AddCommonEntries(dictionary, 2);
            dictionary[PdfName.Create("C0")] = PdfArray.FromNumbers(c0);
            dictionary[PdfName.Create("C1")] = PdfArray.FromNumbers(c1);
            dictionary[PdfNames.N] = PdfObjectFactory.Number(exponent);
            return dictionary;
        }
    }

    private sealed class StitchingFunction(PdfFunction[] functions, double[] bounds, double[] encode, double[] domain) : PdfFunction(domain, range: null)
    {
        public override int OutputCount => functions[0].OutputCount;

        internal override PdfObject GetObject(PdfDocument document)
        {
            var dictionary = new PdfDictionary();
            AddCommonEntries(dictionary, 3);
            dictionary[PdfName.Create("Functions")] = new PdfArray(functions.Select(function => function.GetObject(document)));
            dictionary[PdfName.Create("Bounds")] = PdfArray.FromNumbers(bounds);
            dictionary[PdfName.Create("Encode")] = PdfArray.FromNumbers(encode);
            return dictionary;
        }
    }

    private sealed class SampledFunction(int[] size, int bitsPerSample, double[] domain, double[] range, byte[] samples) : PdfFunction(domain, range)
    {
        public override int OutputCount => Range!.Length / 2;

        internal override PdfObject GetObject(PdfDocument document)
        {
            if (document.SharedResources.TryGetValue(this, out var reference))
                return reference;

            var stream = new PdfStream();
            AddCommonEntries(stream.Dictionary, 0);
            stream.Dictionary[PdfName.Create("Size")] = new PdfArray(size.Select(value => (PdfObject)PdfInteger.Get(value)));
            stream.Dictionary[PdfName.Create("BitsPerSample")] = PdfInteger.Get(bitsPerSample);
            stream.SetData(samples);
            reference = document.Objects.Add(stream);
            document.SharedResources[this] = reference;
            return reference;
        }
    }

    private sealed class PostScriptFunction(string program, double[] domain, double[] range) : PdfFunction(domain, range)
    {
        public override int OutputCount => Range!.Length / 2;

        internal override PdfObject GetObject(PdfDocument document)
        {
            if (document.SharedResources.TryGetValue(this, out var reference))
                return reference;

            var stream = new PdfStream();
            AddCommonEntries(stream.Dictionary, 4);
            stream.SetData(Encoding.ASCII.GetBytes(program));
            reference = document.Objects.Add(stream);
            document.SharedResources[this] = reference;
            return reference;
        }
    }
}
