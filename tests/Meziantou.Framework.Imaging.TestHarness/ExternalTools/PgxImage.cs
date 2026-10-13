using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Imaging.TestHarness.ExternalTools;

/// <summary>
/// One component written by a JPEG 2000 reference decoder in the PGX format of the JPEG 2000 conformance tests
/// (Rec. ITU-T T.803): a text header (<c>PG</c>, the byte order, the sign, the precision, the width and the height), then
/// the samples, in 1, 2 or 4 bytes each. The samples are those of the codestream: no color conversion, no scaling, no
/// upsampling.
/// </summary>
public sealed class PgxImage
{
    private PgxImage(int width, int height, int precision, bool isSigned, int[] samples)
    {
        Width = width;
        Height = height;
        Precision = precision;
        IsSigned = isSigned;
        Samples = samples;
    }

    /// <summary>Gets the width of the component.</summary>
    public int Width { get; }

    /// <summary>Gets the height of the component.</summary>
    public int Height { get; }

    /// <summary>Gets the number of bits of a sample.</summary>
    public int Precision { get; }

    /// <summary>Gets a value indicating whether the samples are signed.</summary>
    public bool IsSigned { get; }

    /// <summary>Gets the samples, row by row.</summary>
    public ReadOnlyMemory<int> Samples { get; }

    /// <summary>Parses a PGX file.</summary>
    /// <param name="data">The file.</param>
    /// <returns>The component.</returns>
    /// <exception cref="FormatException">The file is not a PGX file.</exception>
    public static PgxImage Parse(ReadOnlySpan<byte> data)
    {
        var end = data.IndexOf((byte)'\n');
        if (end < 0)
            throw new FormatException("The PGX file has no header line.");

        // "PG ML + 16 33 17": the sign is optional and may be glued to the precision
        var fields = Encoding.ASCII.GetString(data[..end]).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (fields.Count < 5 || fields[0] != "PG" || fields[1] is not ("ML" or "LM"))
            throw new FormatException("The PGX header is not 'PG <ML|LM> [+|-] <precision> <width> <height>'.");

        var isBigEndian = fields[1] == "ML";
        var isSigned = false;
        var index = 2;
        if (fields[index] is "+" or "-")
        {
            isSigned = fields[index] == "-";
            index++;
        }
        else if (fields[index][0] is '+' or '-')
        {
            isSigned = fields[index][0] == '-';
            fields[index] = fields[index][1..];
        }

        if (fields.Count != index + 3)
            throw new FormatException("The PGX header is not 'PG <ML|LM> [+|-] <precision> <width> <height>'.");

        var precision = int.Parse(fields[index], NumberStyles.None, CultureInfo.InvariantCulture);
        var width = int.Parse(fields[index + 1], NumberStyles.None, CultureInfo.InvariantCulture);
        var height = int.Parse(fields[index + 2], NumberStyles.None, CultureInfo.InvariantCulture);
        var bytesPerSample = precision <= 8 ? 1 : precision <= 16 ? 2 : 4;
        var body = data[(end + 1)..];
        if (body.Length != (long)width * height * bytesPerSample)
            throw new FormatException($"The PGX file holds {body.Length} bytes of samples; {(long)width * height * bytesPerSample} are expected.");

        var samples = new int[width * height];
        for (var i = 0; i < samples.Length; i++)
        {
            var sample = body.Slice(i * bytesPerSample, bytesPerSample);
            samples[i] = (bytesPerSample, isSigned, isBigEndian) switch
            {
                (1, false, _) => sample[0],
                (1, true, _) => (sbyte)sample[0],
                (2, false, true) => BinaryPrimitives.ReadUInt16BigEndian(sample),
                (2, false, false) => BinaryPrimitives.ReadUInt16LittleEndian(sample),
                (2, true, true) => BinaryPrimitives.ReadInt16BigEndian(sample),
                (2, true, false) => BinaryPrimitives.ReadInt16LittleEndian(sample),
                (_, _, true) => BinaryPrimitives.ReadInt32BigEndian(sample),
                _ => BinaryPrimitives.ReadInt32LittleEndian(sample),
            };
        }

        return new PgxImage(width, height, precision, isSigned, samples);
    }
}
