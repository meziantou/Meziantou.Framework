using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Writes a KTX 2.0 file (KTX File Format Specification 2.0): the header, the level index, the data format descriptor
/// (Khronos Data Format Specification 1.4, section 5), the key/value data, and the mip levels from the smallest.
/// </summary>
/// <remarks>
/// The data format descriptor has the samples of <see cref="Ktx2VkFormats"/>, which the decoder checks, and the sample
/// bounds KTX-Software 4.4 writes: 0 and the largest value of an unsigned integer sample (all ones for the 64-bit and
/// 128-bit samples of the block-compressed formats), the smallest and the largest value of a signed one, and -1.0 and
/// 1.0 (0 and 1.0 when unsigned) for floating-point samples.
/// </remarks>
internal static class Ktx2TextureWriter
{
    /// <summary>The value of <c>KTXwriter</c>.</summary>
    public const string WriterName = "Meziantou.Framework.Imaging";

    /// <summary>Gets the format of the default output of a pixel format: uncompressed, without loss.</summary>
    public static Ktx2TextureFormat GetDefaultFormat(PixelFormat pixelFormat) => pixelFormat switch
    {
        PixelFormat.Gray8 => Ktx2TextureFormat.R8,
        PixelFormat.Gray16 => Ktx2TextureFormat.R16,
        PixelFormat.RgbaFloat => Ktx2TextureFormat.R32G32B32A32Sfloat,
        PixelFormat.RgbaHalf => Ktx2TextureFormat.R16G16B16A16Sfloat,
        _ when PixelFormats.GetBitsPerComponent(pixelFormat) > 8 => Ktx2TextureFormat.R16G16B16A16,
        _ => Ktx2TextureFormat.R8G8B8A8,
    };

    /// <summary>Gets the format an encoder writes for pixels of a format.</summary>
    public static Ktx2TextureFormat ResolveFormat(Ktx2Encoder encoder, PixelFormat pixelFormat) => encoder.TextureFormat ?? GetDefaultFormat(pixelFormat);

    /// <summary>Determines whether a format stores floating-point or signed samples, which are linear light.</summary>
    public static bool IsFloatingPoint(Ktx2TextureFormat format) => GetTarget(format, srgb: false).IsFloatingPoint;

    /// <summary>Gets the texels of a format, with the sRGB transfer function or a linear one.</summary>
    public static TextureTarget GetTarget(Ktx2TextureFormat format, bool srgb)
    {
        var vkFormat = GetVkFormat(format, srgb);
        return format switch
        {
            Ktx2TextureFormat.R8 => TextureTarget.Create(TextureEncoding.R8, srgb),
            Ktx2TextureFormat.R8G8 => TextureTarget.Create(TextureEncoding.Rg8, srgb),
            Ktx2TextureFormat.R8G8B8A8 => TextureTarget.Create(TextureEncoding.Rgba8, srgb),
            Ktx2TextureFormat.R16 => TextureTarget.Create(TextureEncoding.R16),
            Ktx2TextureFormat.R16G16B16A16 => TextureTarget.Create(TextureEncoding.Rgba16),
            Ktx2TextureFormat.R16G16B16A16Sfloat => TextureTarget.Create(TextureEncoding.RgbaHalf),
            Ktx2TextureFormat.R32G32B32A32Sfloat => TextureTarget.Create(TextureEncoding.RgbaFloat),
            Ktx2TextureFormat.Bc1Rgb => TextureTarget.Create(TextureEncoding.Bc1Opaque, srgb),
            Ktx2TextureFormat.Bc1Rgba => TextureTarget.Create(TextureEncoding.Bc1, srgb),
            Ktx2TextureFormat.Bc2 => TextureTarget.Create(TextureEncoding.Bc2, srgb),
            Ktx2TextureFormat.Bc3 => TextureTarget.Create(TextureEncoding.Bc3, srgb),
            Ktx2TextureFormat.Bc4Unorm => TextureTarget.Create(TextureEncoding.Bc4),
            Ktx2TextureFormat.Bc4Snorm => TextureTarget.Create(TextureEncoding.Bc4Signed),
            Ktx2TextureFormat.Bc5Unorm => TextureTarget.Create(TextureEncoding.Bc5),
            Ktx2TextureFormat.Bc5Snorm => TextureTarget.Create(TextureEncoding.Bc5Signed),
            Ktx2TextureFormat.Bc6HUfloat => TextureTarget.Create(TextureEncoding.Bc6H),
            Ktx2TextureFormat.Bc6HSfloat => TextureTarget.Create(TextureEncoding.Bc6HSigned),
            Ktx2TextureFormat.Bc7 => TextureTarget.Create(TextureEncoding.Bc7, srgb),
            Ktx2TextureFormat.Etc2Rgb => TextureTarget.Create(TextureEncoding.Etc2Rgb, srgb),
            Ktx2TextureFormat.Etc2RgbA1 => TextureTarget.Create(TextureEncoding.Etc2RgbA1, srgb),
            Ktx2TextureFormat.Etc2Rgba => TextureTarget.Create(TextureEncoding.Etc2Rgba, srgb),
            Ktx2TextureFormat.EacR11Unorm => TextureTarget.Create(TextureEncoding.EacR11),
            Ktx2TextureFormat.EacR11Snorm => TextureTarget.Create(TextureEncoding.EacR11Signed),
            Ktx2TextureFormat.EacR11G11Unorm => TextureTarget.Create(TextureEncoding.EacRg11),
            Ktx2TextureFormat.EacR11G11Snorm => TextureTarget.Create(TextureEncoding.EacRg11Signed),
            _ => TextureTarget.Create(TextureEncoding.Astc, vkFormat.IsSrgb, vkFormat.BlockWidth, vkFormat.BlockHeight),
        };
    }

    /// <summary>Gets the <c>VkFormat</c> of a format: its <c>_SRGB</c> variant for sRGB-encoded colors, when it has one.</summary>
    public static Ktx2VkFormat GetVkFormat(Ktx2TextureFormat format, bool srgb)
    {
        var (unorm, srgbValue) = format switch
        {
            Ktx2TextureFormat.R8 => (9, 15),
            Ktx2TextureFormat.R8G8 => (16, 22),
            Ktx2TextureFormat.R8G8B8A8 => (37, 43),
            Ktx2TextureFormat.R16 => (70, 0),
            Ktx2TextureFormat.R16G16B16A16 => (91, 0),
            Ktx2TextureFormat.R16G16B16A16Sfloat => (97, 0),
            Ktx2TextureFormat.R32G32B32A32Sfloat => (109, 0),
            Ktx2TextureFormat.Bc1Rgb => (131, 132),
            Ktx2TextureFormat.Bc1Rgba => (133, 134),
            Ktx2TextureFormat.Bc2 => (135, 136),
            Ktx2TextureFormat.Bc3 => (137, 138),
            Ktx2TextureFormat.Bc4Unorm => (139, 0),
            Ktx2TextureFormat.Bc4Snorm => (140, 0),
            Ktx2TextureFormat.Bc5Unorm => (141, 0),
            Ktx2TextureFormat.Bc5Snorm => (142, 0),
            Ktx2TextureFormat.Bc6HUfloat => (143, 0),
            Ktx2TextureFormat.Bc6HSfloat => (144, 0),
            Ktx2TextureFormat.Bc7 => (145, 146),
            Ktx2TextureFormat.Etc2Rgb => (147, 148),
            Ktx2TextureFormat.Etc2RgbA1 => (149, 150),
            Ktx2TextureFormat.Etc2Rgba => (151, 152),
            Ktx2TextureFormat.EacR11Unorm => (153, 0),
            Ktx2TextureFormat.EacR11Snorm => (154, 0),
            Ktx2TextureFormat.EacR11G11Unorm => (155, 0),
            Ktx2TextureFormat.EacR11G11Snorm => (156, 0),
            >= Ktx2TextureFormat.Astc4x4 and <= Ktx2TextureFormat.Astc12x12 => (157 + (2 * (format - Ktx2TextureFormat.Astc4x4)), 158 + (2 * (format - Ktx2TextureFormat.Astc4x4))),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Not a KTX 2.0 texture format."),
        };

        var value = srgb && srgbValue != 0 ? srgbValue : unorm;
        return Ktx2VkFormats.TryGet(value, out var vkFormat) ? vkFormat : throw new InvalidOperationException("The VkFormat is not known.");
    }

    /// <summary>Gets the KTX color primaries of a CICP description, or <see langword="null"/> when KTX has none for them.</summary>
    public static int? GetPrimaries(CicpColorDescription cicp) => cicp.ColorPrimaries switch
    {
        1 => Ktx2Format.PrimariesBt709,
        5 => Ktx2Format.PrimariesBt601Ebu,
        6 => Ktx2Format.PrimariesBt601Smpte,
        9 => Ktx2Format.PrimariesBt2020,
        12 => Ktx2Format.PrimariesDisplayP3,
        _ => null,
    };

    /// <summary>Determines whether the data format descriptor can carry a CICP description: KTX primaries, and the sRGB or the linear transfer characteristics.</summary>
    public static bool CanStore(CicpColorDescription cicp) => GetPrimaries(cicp) is not null && cicp.TransferCharacteristics is 8 or 13 && cicp.MatrixCoefficients == 0 && cicp.FullRange;

    /// <summary>
    /// Determines whether the samples written with a metadata plan are sRGB-encoded: the transfer function of its CICP
    /// description, otherwise its transfer function. Floating-point and signed formats are always linear.
    /// </summary>
    public static bool IsSrgbTransfer(MetadataWritePlan metadata, bool floatingPoint)
    {
        if (floatingPoint)
            return false;

        if (metadata.Cicp is { } cicp)
            return cicp.TransferCharacteristics != 8;

        return metadata.TransferFunction != ColorTransferFunction.Linear;
    }

    /// <summary>Gets the value of <c>KTXorientation</c> of an orientation, or <see langword="null"/> for one KTX cannot express (a transposition).</summary>
    public static string? GetOrientation(ExifOrientation orientation) => orientation switch
    {
        ExifOrientation.TopLeft => "rd",
        ExifOrientation.TopRight => "ld",
        ExifOrientation.BottomRight => "lu",
        ExifOrientation.BottomLeft => "ru",
        _ => null,
    };

    /// <summary>Writes the file.</summary>
    /// <param name="texture">The encoded surfaces.</param>
    /// <param name="vkFormat">The format of the texels.</param>
    /// <param name="metadata">The metadata plan: the CICP description and the orientation.</param>
    /// <param name="srgbTransfer">Whether the transfer function is sRGB rather than linear.</param>
    /// <param name="supercompression">The supercompression of the levels.</param>
    /// <param name="scope">The scope charged for the compressed levels.</param>
    /// <returns>The parts of the file, in order; the caller writes them and disposes the result.</returns>
    public static Ktx2FileParts Create(EncodedTexture texture, Ktx2VkFormat vkFormat, MetadataWritePlan metadata, bool srgbTransfer, Ktx2Supercompression supercompression, AllocationScope scope)
    {
        var levelCount = texture.LevelCount;
        var dfd = CreateDataFormatDescriptor(vkFormat, metadata, srgbTransfer);
        var kvd = CreateKeyValueData(texture, metadata);

        // Each level: every layer, then every face (no depth)
        var levels = new ReadOnlyMemory<byte>[levelCount][];
        var uncompressedLengths = new long[levelCount];
        for (var level = 0; level < levelCount; level++)
        {
            var parts = new ReadOnlyMemory<byte>[texture.LayerCount * texture.FaceCount];
            for (var layer = 0; layer < texture.LayerCount; layer++)
            {
                for (var face = 0; face < texture.FaceCount; face++)
                {
                    parts[(layer * texture.FaceCount) + face] = texture.Get(layer, face, level);
                    uncompressedLengths[level] += parts[(layer * texture.FaceCount) + face].Length;
                }
            }

            levels[level] = parts;
        }

        var result = new Ktx2FileParts();
        try
        {
            var lengths = new long[levelCount];
            if (supercompression == Ktx2Supercompression.Zlib)
            {
                for (var level = 0; level < levelCount; level++)
                {
                    var (compressed, charge) = Compress(levels[level], scope);
                    result.AddCharge(charge);
                    levels[level] = [compressed];
                    lengths[level] = compressed.Length;
                }
            }
            else
            {
                uncompressedLengths.CopyTo(lengths, 0);
            }

            // The layout: header, level index, data format descriptor, key/value data, then the levels from the smallest
            var levelIndexOffset = Ktx2Format.HeaderLength;
            var dfdOffset = levelIndexOffset + (levelCount * Ktx2Format.LevelIndexEntryLength);
            var kvdOffset = dfdOffset + dfd.Length;
            long offset = kvdOffset + kvd.Length;

            // Section 3.13.2: without supercompression, each level starts at a multiple of lcm(texel block size, 4)
            var alignment = supercompression == Ktx2Supercompression.None ? LeastCommonMultiple(vkFormat.BytesPerBlock, 4) : 1;
            var offsets = new long[levelCount];
            var paddings = new int[levelCount];
            for (var level = levelCount - 1; level >= 0; level--)
            {
                var padding = (int)((alignment - (offset % alignment)) % alignment);
                paddings[level] = padding;
                offset += padding;
                offsets[level] = offset;
                offset += lengths[level];
            }

            var header = new byte[kvdOffset + kvd.Length];
            var span = header.AsSpan();
            Ktx2Format.Identifier.CopyTo(span);
            BinaryPrimitives.WriteUInt32LittleEndian(span[12..], (uint)vkFormat.Value);
            BinaryPrimitives.WriteUInt32LittleEndian(span[16..], (uint)vkFormat.TypeSize);
            BinaryPrimitives.WriteUInt32LittleEndian(span[20..], (uint)texture.Width);
            BinaryPrimitives.WriteUInt32LittleEndian(span[24..], (uint)texture.Height);
            BinaryPrimitives.WriteUInt32LittleEndian(span[28..], 0);
            BinaryPrimitives.WriteUInt32LittleEndian(span[32..], texture.IsArray ? (uint)texture.LayerCount : 0);
            BinaryPrimitives.WriteUInt32LittleEndian(span[36..], (uint)texture.FaceCount);
            BinaryPrimitives.WriteUInt32LittleEndian(span[40..], (uint)levelCount);
            BinaryPrimitives.WriteUInt32LittleEndian(span[44..], (uint)supercompression);
            BinaryPrimitives.WriteUInt32LittleEndian(span[48..], (uint)dfdOffset);
            BinaryPrimitives.WriteUInt32LittleEndian(span[52..], (uint)dfd.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(span[56..], kvd.Length == 0 ? 0 : (uint)kvdOffset);
            BinaryPrimitives.WriteUInt32LittleEndian(span[60..], (uint)kvd.Length);

            // No supercompression global data: sgdByteOffset and sgdByteLength are 0
            for (var level = 0; level < levelCount; level++)
            {
                var entry = span[(levelIndexOffset + (level * Ktx2Format.LevelIndexEntryLength))..];
                BinaryPrimitives.WriteUInt64LittleEndian(entry, (ulong)offsets[level]);
                BinaryPrimitives.WriteUInt64LittleEndian(entry[8..], (ulong)lengths[level]);
                BinaryPrimitives.WriteUInt64LittleEndian(entry[16..], (ulong)uncompressedLengths[level]);
            }

            dfd.CopyTo(span[dfdOffset..]);
            kvd.CopyTo(span[kvdOffset..]);
            result.Add(header);
            for (var level = levelCount - 1; level >= 0; level--)
            {
                if (paddings[level] > 0)
                {
                    result.Add(new byte[paddings[level]]);
                }

                foreach (var part in levels[level])
                {
                    result.Add(part);
                }
            }

            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    /// <summary>Creates the data format descriptor: its total size, then one basic descriptor block (section 5.5).</summary>
    internal static byte[] CreateDataFormatDescriptor(Ktx2VkFormat vkFormat, MetadataWritePlan metadata, bool srgbTransfer)
    {
        var samples = vkFormat.Samples;
        var blockSize = 24 + (16 * samples.Length);
        var dfd = new byte[4 + blockSize];
        var span = dfd.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(span, (uint)dfd.Length);

        // vendorId 0 (Khronos) and descriptorType 0 (basic), versionNumber 2 (KDFS 1.3) and the size of the block
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], 0);
        BinaryPrimitives.WriteUInt32LittleEndian(span[8..], 2u | ((uint)blockSize << 16));
        span[12] = (byte)vkFormat.ColorModel;
        span[13] = (byte)(metadata.Cicp is { } cicp ? GetPrimaries(cicp) ?? Ktx2Format.PrimariesBt709 : Ktx2Format.PrimariesBt709);
        span[14] = (byte)(srgbTransfer ? Ktx2Format.TransferSrgb : Ktx2Format.TransferLinear);
        span[15] = 0;
        span[16] = (byte)(vkFormat.BlockWidth - 1);
        span[17] = (byte)(vkFormat.BlockHeight - 1);
        span[20] = (byte)vkFormat.BytesPerBlock;
        for (var i = 0; i < samples.Length; i++)
        {
            var sample = samples[i];
            var entry = span[(28 + (16 * i))..];
            BinaryPrimitives.WriteUInt16LittleEndian(entry, (ushort)sample.BitOffset);
            entry[2] = (byte)(sample.BitLength - 1);
            entry[3] = (byte)(sample.Channel | sample.Qualifiers);
            var (lower, upper) = GetSampleBounds(sample);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[8..], lower);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[12..], upper);
        }

        return dfd;
    }

    private static (uint Lower, uint Upper) GetSampleBounds(Ktx2Sample sample)
    {
        var isFloat = (sample.Qualifiers & Ktx2Format.QualifierFloat) != 0;
        var isSigned = (sample.Qualifiers & Ktx2Format.QualifierSigned) != 0;
        if (isFloat)
            return (isSigned ? BitConverter.SingleToUInt32Bits(-1f) : 0, BitConverter.SingleToUInt32Bits(1f));

        if (isSigned)
            return (0x80000000, 0x7FFFFFFF);

        return (0, sample.BitLength >= 32 ? uint.MaxValue : (1u << sample.BitLength) - 1);
    }

    /// <summary>Creates the key/value data (section 3.11), sorted by key: the orientation, the swizzle of the gray formats, the writer.</summary>
    private static byte[] CreateKeyValueData(EncodedTexture texture, MetadataWritePlan metadata)
    {
        var entries = new List<(string Key, string Value)>();
        if (metadata.Orientation != ExifOrientation.TopLeft && GetOrientation(metadata.Orientation) is { } orientation)
        {
            entries.Add((Ktx2Format.OrientationKey, orientation));
        }

        // A one-channel format is red: the swizzle makes it gray, as it was (section 5.4)
        if (texture.Target.Channels == TextureChannels.Gray)
        {
            entries.Add((Ktx2Format.SwizzleKey, "rrr1"));
        }

        entries.Add((Ktx2Format.WriterKey, WriterName));
        entries.Sort((left, right) => string.CompareOrdinal(left.Key, right.Key));

        var output = new List<byte>();
        Span<byte> prefix = stackalloc byte[4];
        foreach (var (key, value) in entries)
        {
            var length = Encoding.UTF8.GetByteCount(key) + 1 + Encoding.UTF8.GetByteCount(value) + 1;
            BinaryPrimitives.WriteUInt32LittleEndian(prefix, (uint)length);
            output.AddRange(prefix);
            output.AddRange(Encoding.UTF8.GetBytes(key));
            output.Add(0);
            output.AddRange(Encoding.UTF8.GetBytes(value));
            output.Add(0);
            while (output.Count % 4 != 0)
            {
                output.Add(0);
            }
        }

        return [.. output];
    }

    /// <summary>
    /// Compresses the images of a level with ZLIB into a buffer charged to the scope before it is allocated: the largest
    /// size deflate can produce (stored blocks add 5 bytes per 65,535, the ZLIB header and checksum 6).
    /// </summary>
    private static (ReadOnlyMemory<byte> Data, AllocationCharge Charge) Compress(ReadOnlyMemory<byte>[] parts, AllocationScope scope)
    {
        var length = 0L;
        foreach (var part in parts)
        {
            length += part.Length;
        }

        var bound = length + (length >> 10) + 64;
        if (bound > CheckedSizes.MaxBufferLength)
            throw CheckedSizes.CreateOverflowException(scope.Limits);

        var charge = scope.Charge(bound, AllocationKind.Temporary);
        try
        {
            var stream = new MemoryStream((int)bound);
            using (var zlib = new ZLibStream(stream, CompressionLevel.SmallestSize, leaveOpen: true))
            {
                foreach (var part in parts)
                {
                    zlib.Write(part.Span);
                }
            }

            return (stream.GetBuffer().AsMemory(0, (int)stream.Length), charge);
        }
        catch
        {
            charge.Dispose();
            throw;
        }
    }

    private static int LeastCommonMultiple(int a, int b)
    {
        var x = a;
        var y = b;
        while (y != 0)
        {
            (x, y) = (y, x % y);
        }

        return a / x * b;
    }
}
