using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.Benchmarks.Workloads;

/// <summary>
/// Writes the TIFF inputs with the horizontal differencing predictor (<c>Predictor</c> 2), which <c>TiffEncoder</c> does
/// not write: one uncompressed RGB or RGBA page, strips of 64 rows, every sample stored as its difference with the sample
/// of the same channel on its left, in the byte order the caller chooses. Without the predictor, it writes the 16-bit RGB
/// pages (48 bits per pixel) that <c>TiffEncoder</c> does not write either.
/// </summary>
internal static class TiffPredictorInput
{
    private const int RowsPerStrip = 64;

    /// <summary>Encodes interleaved samples (8-bit, or 16-bit native-endian) as a TIFF file with the predictor.</summary>
    /// <param name="samples">The samples, row after row.</param>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <param name="samplesPerPixel">3 (RGB) or 4 (RGBA, unassociated alpha).</param>
    /// <param name="bitsPerSample">8 or 16.</param>
    /// <param name="bigEndian">Whether the file is big-endian (<c>MM</c>).</param>
    /// <param name="predictor">Whether the samples are stored as differences (<c>Predictor</c> 2) or as they are (<c>Predictor</c> 1).</param>
    public static byte[] Encode(ReadOnlySpan<byte> samples, int width, int height, int samplesPerPixel, int bitsPerSample, bool bigEndian, bool predictor = true)
    {
        var bytesPerSample = bitsPerSample / 8;
        var rowLength = width * samplesPerPixel * bytesPerSample;
        var stripCount = (height + RowsPerStrip - 1) / RowsPerStrip;
        var tagCount = samplesPerPixel == 4 ? 12 : 11;
        const int IfdOffset = 8;
        var ifdLength = 2 + (tagCount * 12) + 4;
        var bitsOffset = IfdOffset + ifdLength;
        var offsetsOffset = bitsOffset + (samplesPerPixel * 2);
        var countsOffset = offsetsOffset + (stripCount * 4);
        var dataOffset = countsOffset + (stripCount * 4);
        var file = new byte[dataOffset + (rowLength * height)];
        var span = file.AsSpan();
        span[0] = span[1] = bigEndian ? (byte)'M' : (byte)'I';
        Write16(span[2..], 42);
        Write32(span[4..], IfdOffset);
        Write16(span[IfdOffset..], (ushort)tagCount);
        var entry = IfdOffset + 2;
        WriteEntry(256, 4, 1, (uint)width);
        WriteEntry(257, 4, 1, (uint)height);
        WriteEntry(258, 3, (uint)samplesPerPixel, (uint)bitsOffset);
        WriteEntry(259, 3, 1, 1);
        WriteEntry(262, 3, 1, 2);
        WriteEntry(273, 4, (uint)stripCount, stripCount == 1 ? (uint)dataOffset : (uint)offsetsOffset);
        WriteEntry(277, 3, 1, (uint)samplesPerPixel);
        WriteEntry(278, 4, 1, RowsPerStrip);
        WriteEntry(279, 4, (uint)stripCount, stripCount == 1 ? (uint)(rowLength * height) : (uint)countsOffset);
        WriteEntry(284, 3, 1, 1);
        WriteEntry(317, 3, 1, predictor ? 2u : 1u);
        if (samplesPerPixel == 4)
        {
            WriteEntry(338, 3, 1, 2);
        }

        for (var i = 0; i < samplesPerPixel; i++)
        {
            Write16(span[(bitsOffset + (i * 2))..], (ushort)bitsPerSample);
        }

        for (var strip = 0; strip < stripCount; strip++)
        {
            var rows = Math.Min(RowsPerStrip, height - (strip * RowsPerStrip));
            Write32(span[(offsetsOffset + (strip * 4))..], (uint)(dataOffset + (strip * RowsPerStrip * rowLength)));
            Write32(span[(countsOffset + (strip * 4))..], (uint)(rows * rowLength));
        }

        for (var y = 0; y < height; y++)
        {
            var source = samples.Slice(y * rowLength, rowLength);
            var target = span.Slice(dataOffset + (y * rowLength), rowLength);
            var count = width * samplesPerPixel;
            for (var i = count - 1; i >= 0; i--)
            {
                var value = Read(source, i, bytesPerSample);
                var left = predictor && i >= samplesPerPixel ? Read(source, i - samplesPerPixel, bytesPerSample) : 0;
                var difference = (value - left) & ((1 << bitsPerSample) - 1);
                if (bytesPerSample == 1)
                {
                    target[i] = (byte)difference;
                }
                else
                {
                    Write16(target[(i * 2)..], (ushort)difference);
                }
            }
        }

        return file;

        void WriteEntry(ushort tag, ushort type, uint count, uint value)
        {
            var target = file.AsSpan(entry);
            Write16(target, tag);
            Write16(target[2..], type);
            Write32(target[4..], count);
            if (type == 3 && count == 1)
            {
                Write16(target[8..], (ushort)value);
            }
            else
            {
                Write32(target[8..], value);
            }

            entry += 12;
        }

        void Write16(Span<byte> target, ushort value)
        {
            if (bigEndian)
            {
                BinaryPrimitives.WriteUInt16BigEndian(target, value);
            }
            else
            {
                BinaryPrimitives.WriteUInt16LittleEndian(target, value);
            }
        }

        void Write32(Span<byte> target, uint value)
        {
            if (bigEndian)
            {
                BinaryPrimitives.WriteUInt32BigEndian(target, value);
            }
            else
            {
                BinaryPrimitives.WriteUInt32LittleEndian(target, value);
            }
        }

        static int Read(ReadOnlySpan<byte> row, int index, int bytesPerSample)
            => bytesPerSample == 1 ? row[index] : BitConverter.ToUInt16(row.Slice(index * 2, 2));
    }
}
