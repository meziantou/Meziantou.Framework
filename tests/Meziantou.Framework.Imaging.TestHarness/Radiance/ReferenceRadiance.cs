using System.Buffers.Binary;
using System.Text;

namespace Meziantou.Framework.Imaging.TestHarness.Radiance;

/// <summary>
/// An independent reader of Radiance HDR files, written from the file format documentation ("Picture File Format") and
/// "Real Pixels" (Graphics Gems II), with no dependency on the library under test: it gives the RGBE pixels of a picture in
/// the standard orientation, and the encoding of each scanline. It throws <see cref="InvalidDataException"/> on any defect.
/// </summary>
public static class ReferenceRadiance
{
    /// <summary>Gets a color as the reference routines of Radiance read it: the middle of the interval of its mantissa, or with <paramref name="middle"/> false the bottom of it (as FFmpeg reads it).</summary>
    public static float ToFloat(byte mantissa, byte exponent, bool middle = true)
        => exponent == 0 ? 0f : (float)((mantissa + (middle ? 0.5 : 0)) * Math.Pow(2, exponent - 136));

    /// <summary>Reads a file.</summary>
    public static RadiancePicture Read(ReadOnlySpan<byte> data)
    {
        var position = 0;
        var first = ReadLine(data, ref position);
        if (first is not ("#?RADIANCE" or "#?RGBE"))
            throw new InvalidDataException("No Radiance identifier.");

        var lines = new List<string>();
        while (true)
        {
            var line = ReadLine(data, ref position);
            if (line.Length == 0)
                break;

            lines.Add(line);
        }

        var resolution = ReadLine(data, ref position).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (resolution.Length != 4)
            throw new InvalidDataException("Bad resolution string.");

        var scanOrder = resolution[0] + " " + resolution[2];
        if (!RadianceFileBuilder.ScanOrders.Contains(scanOrder))
            throw new InvalidDataException("Bad scan order.");

        var count = int.Parse(resolution[1], CultureInfo.InvariantCulture);
        var length = int.Parse(resolution[3], CultureInfo.InvariantCulture);
        var majorIsX = scanOrder[1] == 'X';
        var width = majorIsX ? count : length;
        var height = majorIsX ? length : count;
        var rgbe = new byte[width * height * 4];
        var encodings = new List<RadianceScanlineEncoding>();
        for (var i = 0; i < count; i++)
        {
            var scanline = ReadScanline(data, ref position, length, out var encoding);
            encodings.Add(encoding);
            var major = scanOrder[0] == '-' ? count - 1 - i : i;
            for (var j = 0; j < length; j++)
            {
                var minor = scanOrder[3] == '-' ? length - 1 - j : j;
                var x = majorIsX ? major : minor;
                var y = majorIsX ? minor : major;
                var row = height - 1 - y;
                scanline.AsSpan(j * 4, 4).CopyTo(rgbe.AsSpan(((row * width) + x) * 4));
            }
        }

        return new RadiancePicture(width, height, rgbe, lines, scanOrder, encodings);
    }

    private static byte[] ReadScanline(ReadOnlySpan<byte> data, ref int position, int length, out RadianceScanlineEncoding encoding)
    {
        var scanline = new byte[length * 4];
        Require(data, position, 4);
        if (length is >= 8 and <= 0x7FFF && data[position] == 2 && data[position + 1] == 2 && data[position + 2] < 128)
        {
            if (((data[position + 2] << 8) | data[position + 3]) != length)
                throw new InvalidDataException("Bad scanline length.");

            position += 4;
            encoding = RadianceScanlineEncoding.NewRunLength;
            for (var component = 0; component < 4; component++)
            {
                var pixel = 0;
                while (pixel < length)
                {
                    Require(data, position, 1);
                    var code = data[position++];
                    if (code > 128)
                    {
                        Require(data, position, 1);
                        var value = data[position++];
                        for (var k = 0; k < code - 128; k++)
                        {
                            scanline[(Next(ref pixel, length) * 4) + component] = value;
                        }
                    }
                    else
                    {
                        if (code == 0)
                            throw new InvalidDataException("Code 0.");

                        Require(data, position, code);
                        for (var k = 0; k < code; k++)
                        {
                            scanline[(Next(ref pixel, length) * 4) + component] = data[position++];
                        }
                    }
                }
            }

            return scanline;
        }

        encoding = RadianceScanlineEncoding.Flat;
        var index = 0;
        var shift = 0;
        while (index < length)
        {
            Require(data, position, 4);
            var pixel = data.Slice(position, 4);
            position += 4;
            if (pixel[0] == 1 && pixel[1] == 1 && pixel[2] == 1)
            {
                encoding = RadianceScanlineEncoding.OldRunLength;
                if (index == 0)
                    throw new InvalidDataException("Repetition without a previous pixel.");

                var repeat = (long)pixel[3] << shift;
                for (var k = 0L; k < repeat; k++)
                {
                    scanline.AsSpan((index - 1) * 4, 4).CopyTo(scanline.AsSpan(Next(ref index, length) * 4));
                }

                shift += 8;
            }
            else
            {
                pixel.CopyTo(scanline.AsSpan(Next(ref index, length) * 4));
                shift = 0;
            }
        }

        return scanline;
    }

    private static int Next(ref int index, int length)
    {
        if (index >= length)
            throw new InvalidDataException("Run past the end of the scanline.");

        return index++;
    }

    private static void Require(ReadOnlySpan<byte> data, int position, int count)
    {
        if (data.Length - position < count)
            throw new InvalidDataException("Truncated.");
    }

    private static string ReadLine(ReadOnlySpan<byte> data, ref int position)
    {
        var end = data[position..].IndexOf((byte)'\n');
        if (end < 0)
            throw new InvalidDataException("Truncated header.");

        var line = Encoding.UTF8.GetString(data.Slice(position, end));
        position += end + 1;
        return line;
    }
}
