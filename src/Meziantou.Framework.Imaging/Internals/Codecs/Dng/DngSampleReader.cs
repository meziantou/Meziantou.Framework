using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Reads the stored samples of a DNG raw image into the stage 1 image of the pipeline: the strips or tiles are decoded by
/// <see cref="TiffBlockReader"/> (compression and predictors), and each sample becomes a single-precision value: an
/// integer divided by 65,535 (exact once multiplied back and rounded), or the value of a floating-point sample.
/// </summary>
internal static class DngSampleReader
{
    /// <summary>Reads every sample of the raw image.</summary>
    /// <exception cref="InvalidImageContentException">A strip or tile is missing, malformed or outside the input.</exception>
    public static DngPlanes Read(RandomAccessSource source, DngRawDescriptor raw, ImageCodecContext context)
        => Deinterleave(ReadStored(source, raw, context), raw, context);

    private static DngPlanes ReadStored(RandomAccessSource source, DngRawDescriptor raw, ImageCodecContext context)
    {
        var page = raw.Page;
        var samples = page.SamplesPerPixel;
        var image = DngPlanes.Create(context, page.Size.Width, page.Size.Height, samples);
        try
        {
            if (page.Compression == TiffFormat.CompressionJpegXl)
            {
                ReadJpegXl(source, page, image, context);
                return image;
            }

            using var reader = new TiffBlockReader(source, page);
            for (var blockRow = 0; blockRow < page.BlocksDown; blockRow++)
            {
                for (var blockColumn = 0; blockColumn < page.BlocksAcross; blockColumn++)
                {
                    context.CancellationToken.ThrowIfCancellationRequested();
                    var index = (blockRow * page.BlocksAcross) + blockColumn;
                    var top = blockRow * page.BlockHeight;
                    var left = blockColumn * page.BlockWidth;
                    var rows = page.IsTiled ? page.BlockHeight : Math.Min(page.BlockHeight, page.Size.Height - top);
                    var block = reader.ReadBlock(index, rows);
                    var visibleRows = Math.Min(rows, page.Size.Height - top);
                    var visibleColumns = Math.Min(page.BlockWidth, page.Size.Width - left);
                    for (var y = 0; y < visibleRows; y++)
                    {
                        var stored = block.Slice(y * page.BlockRowLength, page.BlockRowLength);
                        var row = image.GetRow(top + y).Slice(left * samples, visibleColumns * samples);
                        Expand(page, stored, row);
                    }
                }
            }

            return image;
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Puts the rows and the columns stored in interleaved fields (<c>RowInterleaveFactor</c>, <c>ColumnInterleaveFactor</c>)
    /// back in their order: field f holds the rows f, f + F, f + 2F, ... of the image, and the fields are stored one after
    /// the other, over the whole raw image.
    /// </summary>
    private static DngPlanes Deinterleave(DngPlanes stored, DngRawDescriptor raw, ImageCodecContext context)
    {
        if (raw.RowInterleaveFactor == 1 && raw.ColumnInterleaveFactor == 1)
            return stored;

        using (stored)
        {
            var result = DngPlanes.Create(context, stored.Width, stored.Height, stored.Planes);
            var rows = GetFieldOrder(stored.Height, raw.RowInterleaveFactor);
            var columns = GetFieldOrder(stored.Width, raw.ColumnInterleaveFactor);
            var planes = stored.Planes;
            for (var storedY = 0; storedY < stored.Height; storedY++)
            {
                var source = stored.GetRow(storedY);
                var destination = result.GetRow(rows[storedY]);
                for (var storedX = 0; storedX < stored.Width; storedX++)
                {
                    source.Slice(storedX * planes, planes).CopyTo(destination[(columns[storedX] * planes)..]);
                }
            }

            return result;
        }
    }

    /// <summary>Gets the line of the image of each stored line of fields.</summary>
    private static int[] GetFieldOrder(int length, int fields)
    {
        var order = new int[length];
        var position = 0;
        for (var field = 0; field < fields; field++)
        {
            for (var line = field; line < length; line += fields)
            {
                order[position++] = line;
            }
        }

        return order;
    }

    /// <summary>
    /// Reads the blocks of a raw image compressed with JPEG XL (DNG 1.7): each block is a JPEG XL codestream or file of the
    /// size of the block, decoded by the JPEG XL decoder of the library; integer samples coded with fewer than 16 bits are
    /// given back at their own precision.
    /// </summary>
    private static void ReadJpegXl(RandomAccessSource source, TiffPage page, DngPlanes image, ImageCodecContext context)
    {
        var samples = page.SamplesPerPixel;
        var options = new ImageDecodeOptions { Configuration = context.Configuration };
        var pixelFormat = page.IsFloatingPoint ? PixelFormat.RgbaFloat : PixelFormat.Rgba64;
        for (var blockRow = 0; blockRow < page.BlocksDown; blockRow++)
        {
            for (var blockColumn = 0; blockColumn < page.BlocksAcross; blockColumn++)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                var index = (blockRow * page.BlocksAcross) + blockColumn;
                var offset = page.BlockOffsets[index];
                var length = page.BlockByteCounts[index];
                if (length == 0 || offset > (ulong)source.Length || (ulong)source.Length - offset < length)
                    throw DngFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The JPEG XL block {index} of the raw image of the DNG file is outside the input."));

                using var bytes = source.ReadToBuffer((long)offset, (long)length);
                var embedded = context.ForEmbeddedDatastream();
                int bits;
                Image decoded;
                try
                {
                    using (var identify = JpegXlCodec.Instance.CreateIdentifyParser(ImageIdentifyMode.Header, embedded))
                    {
                        bits = ImageInputPump.Run(bytes.Span, identify, embedded).BitsPerComponent;
                    }

                    using var parser = JpegXlCodec.Instance.CreateDecodeParser(ImageDecodeRequest.Create(options, pixelFormat), embedded);
                    decoded = ImageInputPump.Run(bytes.Span, parser, embedded);
                }
                catch (InvalidImageContentException exception) when (exception.Format == ImageFormat.JpegXl)
                {
                    throw DngFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The JPEG XL block {index} of the raw image of the DNG file is malformed: {exception.Message}"), exception);
                }
                catch (UnknownImageFormatException exception)
                {
                    throw DngFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The JPEG XL block {index} of the raw image of the DNG file is not a JPEG XL codestream."), exception);
                }

                using (decoded)
                {
                    var top = blockRow * page.BlockHeight;
                    var left = blockColumn * page.BlockWidth;
                    var rows = Math.Min(page.BlockHeight, page.Size.Height - top);
                    var columns = Math.Min(page.BlockWidth, page.Size.Width - left);
                    if (decoded.Width < columns || decoded.Height < rows || decoded.Width > page.BlockWidth || decoded.Height > page.BlockHeight)
                        throw DngFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The JPEG XL block {index} of the raw image of the DNG file is {decoded.Width}x{decoded.Height}; the block is {page.BlockWidth}x{page.BlockHeight}."));

                    CopyDecodedBlock(decoded, image, left, top, columns, rows, samples, page.IsFloatingPoint, bits);
                }
            }
        }
    }

    private static void CopyDecodedBlock(Image decoded, DngPlanes image, int left, int top, int columns, int rows, int samples, bool isFloatingPoint, int bits)
    {
        var max = (1 << Math.Clamp(bits, 1, 16)) - 1;
        using var lease = decoded.FrameCore(0).GetStorage().AcquireLease();
        for (var y = 0; y < rows; y++)
        {
            var row = image.GetRow(top + y);
            var source = lease.GetRowBytes(y);
            for (var x = 0; x < columns; x++)
            {
                for (var s = 0; s < samples; s++)
                {
                    float value;
                    if (isFloatingPoint)
                    {
                        // RgbaFloat: four single-precision samples per pixel
                        value = BinaryPrimitives.ReadSingleLittleEndian(source[((x * 16) + (s * 4))..]);
                    }
                    else
                    {
                        // Rgba64: four 16-bit samples per pixel; the decoder scales samples of fewer bits to 16 bits, and
                        // their value is the nearest one at their precision
                        var scaled = BitConverter.IsLittleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(source[((x * 8) + (s * 2))..]) : BinaryPrimitives.ReadUInt16BigEndian(source[((x * 8) + (s * 2))..]);
                        value = Normalize(max == 65535 ? scaled : (int)Math.Round(scaled * (double)max / 65535, MidpointRounding.AwayFromZero));
                    }

                    row[((left + x) * samples) + s] = value;
                }
            }
        }
    }

    /// <summary>Expands the first samples of a stored row of a block into values.</summary>
    private static void Expand(TiffPage page, ReadOnlySpan<byte> stored, Span<float> destination)
    {
        var bits = page.DecodedBitsPerSample;
        var bigEndian = page.IsBigEndian;
        if (page.IsFloatingPoint)
        {
            for (var i = 0; i < destination.Length; i++)
            {
                destination[i] = bits switch
                {
                    16 => (float)BitConverter.UInt16BitsToHalf(bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(stored[(i * 2)..]) : BinaryPrimitives.ReadUInt16LittleEndian(stored[(i * 2)..])),
                    24 => ReadFloat24(stored.Slice(i * 3, 3), bigEndian),
                    _ => bigEndian ? BinaryPrimitives.ReadSingleBigEndian(stored[(i * 4)..]) : BinaryPrimitives.ReadSingleLittleEndian(stored[(i * 4)..]),
                };
            }

            return;
        }

        switch (bits)
        {
            case 8:
                for (var i = 0; i < destination.Length; i++)
                {
                    destination[i] = Normalize(stored[i]);
                }

                break;

            case 16:
                for (var i = 0; i < destination.Length; i++)
                {
                    destination[i] = Normalize(bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(stored[(i * 2)..]) : BinaryPrimitives.ReadUInt16LittleEndian(stored[(i * 2)..]));
                }

                break;

            default:
                // Packed most significant bit first, whatever the byte order of the file (DNG 1.7.1, chapter 3)
                var position = 0L;
                for (var i = 0; i < destination.Length; i++)
                {
                    var value = 0;
                    for (var bit = 0; bit < bits; bit++, position++)
                    {
                        value = (value << 1) | ((stored[(int)(position >> 3)] >> (7 - (int)(position & 7))) & 1);
                    }

                    destination[i] = Normalize(value);
                }

                break;
        }
    }

    /// <summary>Gets the value of an integer sample in stage 1: the sample divided by 65,535.</summary>
    public static float Normalize(int value) => (float)(value / 65535.0);

    /// <summary>Gets the integer sample of a value of stage 1.</summary>
    public static int Denormalize(float value) => (int)Math.Round(value * 65535.0, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Reads a 24-bit floating-point sample (Adobe Photoshop TIFF Technical Note 3): a sign bit, 7 bits of exponent biased
    /// by 63 and 16 bits of mantissa; the exponent 0 is the subnormal numbers, 127 the infinities and the not-a-numbers.
    /// </summary>
    public static float ReadFloat24(ReadOnlySpan<byte> bytes, bool bigEndian)
    {
        var bits = bigEndian ? (bytes[0] << 16) | (bytes[1] << 8) | bytes[2] : (bytes[2] << 16) | (bytes[1] << 8) | bytes[0];
        var sign = (bits >> 23) & 1;
        var exponent = (bits >> 16) & 0x7F;
        var mantissa = bits & 0xFFFF;
        float value;
        if (exponent == 0)
        {
            value = mantissa == 0 ? 0 : (float)(mantissa * Math.Pow(2, -62 - 16));
        }
        else if (exponent == 0x7F)
        {
            value = mantissa == 0 ? float.PositiveInfinity : float.NaN;
        }
        else
        {
            value = (float)((1 + (mantissa / 65536.0)) * Math.Pow(2, exponent - 63));
        }

        return sign == 0 ? value : -value;
    }
}
