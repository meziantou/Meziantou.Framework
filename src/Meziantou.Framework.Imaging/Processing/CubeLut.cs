using System.Text;

namespace Meziantou.Framework.Imaging;

/// <summary>
/// An immutable color lookup table read from a <c>.cube</c> file, for
/// <see cref="ImageProcessingExtensions.ApplyLut(Image, CubeLut, LutInterpolation, CancellationToken)"/>: the color
/// grades and film looks that video and photo tools export.
/// </summary>
/// <remarks>
/// <para>
/// The format is the one of the Adobe Cube LUT Specification 1.0. A file holds either a three-dimensional table
/// (<c>LUT_3D_SIZE n</c>, from 2 to 256: <c>n^3</c> output colors on a regular lattice of input colors) or a
/// one-dimensional one (<c>LUT_1D_SIZE n</c>, from 2 to 65,536: <c>n</c> output values per channel), never both. Each
/// data line holds three numbers, red, green and blue; the lines of a three-dimensional table run with red changing
/// fastest, then green, then blue. <c>DOMAIN_MIN</c> and <c>DOMAIN_MAX</c> give the input range of each channel (0 to 1
/// by default); the <c>LUT_1D_INPUT_RANGE</c> and <c>LUT_3D_INPUT_RANGE</c> keywords of DaVinci Resolve give one range for
/// the three channels. <c>TITLE</c> names the table. Lines that start with <c>#</c> are comments, blank lines are
/// ignored, and every keyword comes before the data. Any other line is an error.
/// </para>
/// <para>
/// An input value is clamped to the domain of its channel, then located on the lattice: a one-dimensional table is
/// interpolated linearly, a three-dimensional one as <see cref="LutInterpolation"/> says. The output values are used as
/// they are written, without clamping: values outside [0, 1] reach floating-point images, and integer images clamp them
/// to their sample range.
/// </para>
/// </remarks>
public sealed class CubeLut
{
    /// <summary>The largest size of a three-dimensional table, from the specification.</summary>
    internal const int MaxSize3D = 256;

    /// <summary>The largest size of a one-dimensional table, from the specification.</summary>
    internal const int MaxSize1D = 65536;

    private readonly float[] _values;
    private readonly double[] _domainMin;
    private readonly double[] _domainMax;

    private CubeLut(string? title, int size, bool threeDimensional, float[] values, double[] domainMin, double[] domainMax)
    {
        Title = title;
        Size = size;
        IsThreeDimensional = threeDimensional;
        _values = values;
        _domainMin = domainMin;
        _domainMax = domainMax;
    }

    /// <summary>Gets the title of the table (the <c>TITLE</c> keyword), or <see langword="null"/> when the file has none.</summary>
    public string? Title { get; }

    /// <summary>Gets the number of points of the table along each axis: the value of <c>LUT_3D_SIZE</c> or <c>LUT_1D_SIZE</c>.</summary>
    public int Size { get; }

    /// <summary>Gets a value indicating whether the table is three-dimensional (<c>LUT_3D_SIZE</c>): each output color depends on the three input samples. A one-dimensional table maps each channel on its own.</summary>
    public bool IsThreeDimensional { get; }

    /// <summary>Gets the output values, three per point (red, green, blue), red changing fastest.</summary>
    internal ReadOnlySpan<float> Values => _values;

    /// <summary>Gets the lowest input value of each channel (red, green, blue).</summary>
    internal ReadOnlySpan<double> DomainMin => _domainMin;

    /// <summary>Gets the highest input value of each channel (red, green, blue).</summary>
    internal ReadOnlySpan<double> DomainMax => _domainMax;

    /// <summary>Reads a table from the text of a <c>.cube</c> file.</summary>
    /// <param name="text">The text of the file.</param>
    /// <returns>The table.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    /// <exception cref="FormatException">The text is not a valid <c>.cube</c> file. The message gives the line.</exception>
    public static CubeLut Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        using var reader = new StringReader(text);
        return Parse(reader);
    }

    /// <summary>Reads a table from a <c>.cube</c> file.</summary>
    /// <param name="path">The path of the file.</param>
    /// <returns>The table.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="FormatException">The file is not a valid <c>.cube</c> file. The message gives the line.</exception>
    /// <exception cref="IOException">The file cannot be read.</exception>
    public static CubeLut Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return Parse(reader);
    }

    /// <summary>Reads a table from a stream that holds a <c>.cube</c> file, from its current position to its end. The stream is not closed.</summary>
    /// <param name="stream">The stream.</param>
    /// <returns>The table.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
    /// <exception cref="FormatException">The stream does not hold a valid <c>.cube</c> file. The message gives the line.</exception>
    /// <exception cref="IOException">The stream cannot be read.</exception>
    public static CubeLut Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: -1, leaveOpen: true);
        return Parse(reader);
    }

    private static CubeLut Parse(TextReader reader)
    {
        string? title = null;
        var size = 0;
        bool? threeDimensional = null;
        double[] domainMin = [0, 0, 0];
        double[] domainMax = [1, 1, 1];
        float[]? values = null;
        var count = 0;
        var expected = 0;
        var lineNumber = 0;
        Span<double> numbers = stackalloc double[3];
        Span<double> range = stackalloc double[2];
        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            var content = line.AsSpan().Trim();
            if (content.IsEmpty || content[0] == '#')
                continue;

            if (IsDataLine(content))
            {
                if (values is null)
                {
                    if (threeDimensional is null)
                        throw Error(lineNumber, "The data starts before LUT_3D_SIZE or LUT_1D_SIZE.");

                    for (var c = 0; c < 3; c++)
                    {
                        if (!(domainMin[c] < domainMax[c]))
                            throw Error(lineNumber, "DOMAIN_MIN must be less than DOMAIN_MAX for every channel.");
                    }

                    expected = threeDimensional.Value ? size * size * size : size;
                    values = new float[3 * expected];
                }

                if (count == expected)
                    throw Error(lineNumber, string.Create(CultureInfo.InvariantCulture, $"The table has more than the {expected} lines its size gives."));

                ParseNumbers(content, numbers, lineNumber);
                for (var c = 0; c < 3; c++)
                {
                    var value = (float)numbers[c];
                    if (!float.IsFinite(value))
                        throw Error(lineNumber, "The values of the table must be finite.");

                    values[(3 * count) + c] = value;
                }

                count++;
                continue;
            }

            if (values is not null)
                throw Error(lineNumber, "A keyword follows the data.");

            var keywordEnd = content.IndexOfAny(' ', '\t');
            var keyword = keywordEnd < 0 ? content : content[..keywordEnd];
            var arguments = keywordEnd < 0 ? [] : content[keywordEnd..].TrimStart();
            switch (keyword)
            {
                case "TITLE":
                    if (title is not null)
                        throw Error(lineNumber, "The file has more than one TITLE.");

                    if (arguments.Length < 2 || arguments[0] != '"' || arguments[^1] != '"')
                        throw Error(lineNumber, "TITLE must be followed by a text between double quotes.");

                    title = arguments[1..^1].ToString();
                    break;

                case "LUT_3D_SIZE" or "LUT_1D_SIZE":
                    if (threeDimensional is not null)
                        throw Error(lineNumber, "The file has more than one LUT_3D_SIZE or LUT_1D_SIZE: a table is either three-dimensional or one-dimensional.");

                    threeDimensional = keyword is "LUT_3D_SIZE";
                    var max = threeDimensional.Value ? MaxSize3D : MaxSize1D;
                    if (!int.TryParse(arguments, NumberStyles.None, CultureInfo.InvariantCulture, out size) || size < 2 || size > max)
                        throw Error(lineNumber, string.Create(CultureInfo.InvariantCulture, $"{keyword} must be followed by an integer from 2 to {max}."));

                    break;

                case "DOMAIN_MIN" or "DOMAIN_MAX":
                    ParseNumbers(arguments, numbers, lineNumber);
                    SetDomain(keyword is "DOMAIN_MIN" ? domainMin : domainMax, numbers, lineNumber);
                    break;

                case "LUT_3D_INPUT_RANGE" or "LUT_1D_INPUT_RANGE":
                    ParseNumbers(arguments, range, lineNumber);
                    numbers.Fill(range[0]);
                    SetDomain(domainMin, numbers, lineNumber);
                    numbers.Fill(range[1]);
                    SetDomain(domainMax, numbers, lineNumber);
                    break;

                default:
                    throw Error(lineNumber, string.Create(CultureInfo.InvariantCulture, $"'{keyword}' is not a keyword of the Cube LUT format."));
            }
        }

        if (threeDimensional is null)
            throw Error(lineNumber, "The file has no LUT_3D_SIZE or LUT_1D_SIZE.");

        if (count != expected || values is null)
            throw Error(lineNumber, string.Create(CultureInfo.InvariantCulture, $"The table has {count} lines, but its size gives {(threeDimensional.Value ? (long)size * size * size : size)}."));

        return new CubeLut(title, size, threeDimensional.Value, values, domainMin, domainMax);
    }

    private static void SetDomain(double[] domain, ReadOnlySpan<double> numbers, int lineNumber)
    {
        foreach (var number in numbers)
        {
            if (!double.IsFinite(number))
                throw Error(lineNumber, "The domain must be finite.");
        }

        numbers.CopyTo(domain);
    }

    /// <summary>Gets a value indicating whether a line holds numbers: it starts with a digit, a sign or a decimal point.</summary>
    private static bool IsDataLine(ReadOnlySpan<char> line) => line[0] is (>= '0' and <= '9') or '-' or '+' or '.';

    private static void ParseNumbers(ReadOnlySpan<char> text, Span<double> numbers, int lineNumber)
    {
        for (var i = 0; i < numbers.Length; i++)
        {
            text = text.TrimStart();
            var end = text.IndexOfAny(' ', '\t');
            var token = end < 0 ? text : text[..end];
            if (token.IsEmpty || !double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[i]))
                throw Error(lineNumber, string.Create(CultureInfo.InvariantCulture, $"The line must hold {numbers.Length} numbers."));

            text = end < 0 ? [] : text[end..];
        }

        if (!text.Trim().IsEmpty)
            throw Error(lineNumber, string.Create(CultureInfo.InvariantCulture, $"The line must hold {numbers.Length} numbers."));
    }

    private static FormatException Error(int lineNumber, string message)
        => new(string.Create(CultureInfo.InvariantCulture, $"Invalid .cube file, line {lineNumber}: {message}"));
}
