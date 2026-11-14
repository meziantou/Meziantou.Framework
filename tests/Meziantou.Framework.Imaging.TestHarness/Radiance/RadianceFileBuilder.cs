using System.Text;

namespace Meziantou.Framework.Imaging.TestHarness.Radiance;

/// <summary>
/// Assembles Radiance HDR files from an information header, a resolution string and scanlines, for the tests and the
/// fixtures that need a file the encoders do not write (flat and "old" encoded scanlines, the seven other orientations,
/// XYZE samples, header variables) and for malformed files. It is written from the Radiance file format documentation
/// ("Picture File Format") and "Real Pixels" (Graphics Gems II), and never uses the library under test. It writes what it
/// is told to: nothing is validated.
/// </summary>
public sealed class RadianceFileBuilder
{
    /// <summary>The scan orders of the resolution string, the standard one first.</summary>
    public static IReadOnlyList<string> ScanOrders { get; } = ["-Y +X", "-Y -X", "+Y -X", "+Y +X", "+X +Y", "-X +Y", "-X -Y", "+X -Y"];

    /// <summary>Gets or sets the first line, without its line feed.</summary>
    public string Signature { get; set; } = "#?RADIANCE";

    /// <summary>Gets the lines of the information header after the first one, without their line feeds.</summary>
    public IList<string> HeaderLines { get; } = [];

    /// <summary>Gets or sets the resolution string, without its line feed, such as <c>-Y 2 +X 3</c>.</summary>
    public string Resolution { get; set; } = "";

    /// <summary>Gets the encoded scanlines, in file order.</summary>
    public IList<byte[]> Scanlines { get; } = [];

    /// <summary>Gets or sets bytes written after the last scanline.</summary>
    public ReadOnlyMemory<byte> Trailer { get; set; }

    /// <summary>
    /// Creates the builder of a picture from its pixels in the standard orientation (rows from the top, left to right),
    /// stored in a scan order with one scanline encoding.
    /// </summary>
    /// <param name="width">The width of the picture.</param>
    /// <param name="height">The height of the picture.</param>
    /// <param name="rgbe">Four bytes per pixel, the rows from the top.</param>
    /// <param name="encoding">The encoding of every scanline.</param>
    /// <param name="scanOrder">The orientation of the resolution string (<see cref="ScanOrders"/>).</param>
    /// <param name="format">The value of the <c>FORMAT</c> line, or <see langword="null"/> to write none.</param>
    /// <returns>The builder.</returns>
    public static RadianceFileBuilder Create(int width, int height, ReadOnlySpan<byte> rgbe, RadianceScanlineEncoding encoding, string scanOrder = "-Y +X", string? format = "32-bit_rle_rgbe")
    {
        var builder = new RadianceFileBuilder();
        if (format is not null)
        {
            builder.HeaderLines.Add("FORMAT=" + format);
        }

        var scanlines = GetScanlines(width, height, rgbe, scanOrder);
        var major = scanOrder[1];
        var minor = scanOrder[4];
        builder.Resolution = string.Create(CultureInfo.InvariantCulture, $"{scanOrder[..2]} {(major == 'Y' ? height : width)} {scanOrder[3..5]} {(minor == 'Y' ? height : width)}");
        foreach (var scanline in scanlines)
        {
            builder.Scanlines.Add(Encode(scanline, encoding));
        }

        return builder;
    }

    /// <summary>
    /// Gets the scanlines of a picture in a scan order: for <c>sA sB</c>, the scanlines go along the axis A in the direction
    /// of its sign, and their pixels along the axis B. The origin of the coordinates of Radiance is the bottom-left corner:
    /// row r of the picture (from the top) is at Y = height - 1 - r.
    /// </summary>
    public static IReadOnlyList<byte[]> GetScanlines(int width, int height, ReadOnlySpan<byte> rgbe, string scanOrder)
    {
        var majorIsX = scanOrder[1] == 'X';
        var majorDecreasing = scanOrder[0] == '-';
        var minorDecreasing = scanOrder[3] == '-';
        var count = majorIsX ? width : height;
        var length = majorIsX ? height : width;
        var result = new List<byte[]>(count);
        for (var i = 0; i < count; i++)
        {
            var major = majorDecreasing ? count - 1 - i : i;
            var scanline = new byte[length * 4];
            for (var j = 0; j < length; j++)
            {
                var minor = minorDecreasing ? length - 1 - j : j;
                var x = majorIsX ? major : minor;
                var y = majorIsX ? minor : major;
                var row = height - 1 - y;
                rgbe.Slice(((row * width) + x) * 4, 4).CopyTo(scanline.AsSpan(j * 4));
            }

            result.Add(scanline);
        }

        return result;
    }

    /// <summary>Encodes a scanline.</summary>
    public static byte[] Encode(ReadOnlySpan<byte> rgbe, RadianceScanlineEncoding encoding) => encoding switch
    {
        RadianceScanlineEncoding.Flat => rgbe.ToArray(),
        RadianceScanlineEncoding.OldRunLength => EncodeOld(rgbe),
        RadianceScanlineEncoding.NewRunLength => EncodeNew(rgbe),
        _ => throw new ArgumentOutOfRangeException(nameof(encoding)),
    };

    /// <summary>
    /// The "old" run-length encoding: a pixel 1, 1, 1, n repeats the previous pixel n times, and the next such pixels hold
    /// the higher bytes of the count. Runs of fewer than <paramref name="minRun"/> pixels are written as they are.
    /// </summary>
    public static byte[] EncodeOld(ReadOnlySpan<byte> rgbe, int minRun = 2)
    {
        var output = new List<byte>();
        var count = rgbe.Length / 4;
        var i = 0;
        while (i < count)
        {
            var pixel = rgbe.Slice(i * 4, 4);
            output.AddRange(pixel);
            var run = 0;
            while (i + 1 + run < count && rgbe.Slice((i + 1 + run) * 4, 4).SequenceEqual(pixel))
            {
                run++;
            }

            if (run >= minRun)
            {
                // The low byte first, then the higher bytes
                var remaining = run;
                do
                {
                    output.AddRange([1, 1, 1, (byte)remaining]);
                    remaining >>= 8;
                }
                while (remaining > 0);

                i += 1 + run;
            }
            else
            {
                i++;
            }
        }

        return [.. output];
    }

    /// <summary>
    /// The new run-length encoding: the marker 2, 2 and the 15-bit length, then each component apart, as runs of at
    /// least <paramref name="minRun"/> equal bytes (code 128 + n, then the byte) and literals of up to 128 bytes (code n, then
    /// the bytes).
    /// </summary>
    public static byte[] EncodeNew(ReadOnlySpan<byte> rgbe, int minRun = 4)
    {
        var count = rgbe.Length / 4;
        var output = new List<byte> { 2, 2, (byte)(count >> 8), (byte)count };
        for (var component = 0; component < 4; component++)
        {
            var plane = new byte[count];
            for (var i = 0; i < count; i++)
            {
                plane[i] = rgbe[(i * 4) + component];
            }

            var position = 0;
            while (position < count)
            {
                var run = 1;
                while (position + run < count && run < 127 && plane[position + run] == plane[position])
                {
                    run++;
                }

                if (run >= minRun)
                {
                    output.Add((byte)(128 + run));
                    output.Add(plane[position]);
                    position += run;
                    continue;
                }

                var start = position;
                while (position < count && position - start < 128)
                {
                    var next = 1;
                    while (position + next < count && next < minRun && plane[position + next] == plane[position])
                    {
                        next++;
                    }

                    if (next >= minRun)
                        break;

                    position++;
                }

                output.Add((byte)(position - start));
                output.AddRange(plane.AsSpan(start, position - start));
            }
        }

        return [.. output];
    }

    /// <summary>
    /// Converts a color to an RGBE pixel as "Real Pixels" describes it: the largest color is f × 2^e with f in [0.5, 1),
    /// each color gets the mantissa floor(c × 256 / 2^e), and the exponent byte is e + 128. A largest color of at most
    /// 1E-32 is black.
    /// </summary>
    public static byte[] ToRgbe(double red, double green, double blue)
    {
        var max = Math.Max(red, Math.Max(green, blue));
        if (max <= 1e-32)
            return [0, 0, 0, 0];

        var exponent = 0;
        var fraction = max;
        while (fraction >= 1)
        {
            fraction /= 2;
            exponent++;
        }

        while (fraction < 0.5)
        {
            fraction *= 2;
            exponent--;
        }

        var scale = 256 / Math.Pow(2, exponent);
        return [(byte)Math.Floor(red * scale), (byte)Math.Floor(green * scale), (byte)Math.Floor(blue * scale), (byte)(exponent + 128)];
    }

    /// <summary>Assembles the file.</summary>
    public byte[] Build()
    {
        using var output = new MemoryStream();
        var header = new StringBuilder();
        header.Append(Signature).Append('\n');
        foreach (var line in HeaderLines)
        {
            header.Append(line).Append('\n');
        }

        header.Append('\n').Append(Resolution).Append('\n');
        var bytes = Encoding.UTF8.GetBytes(header.ToString());
        output.Write(bytes);
        foreach (var scanline in Scanlines)
        {
            output.Write(scanline);
        }

        output.Write(Trailer.Span);
        return output.ToArray();
    }
}
