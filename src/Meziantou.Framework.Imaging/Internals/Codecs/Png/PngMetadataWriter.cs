using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Serializes the metadata of a <see cref="MetadataWritePlan"/> (already reconciled and filtered by the save-time policy)
/// as PNG ancillary chunks, all written before the image data, in this order:
/// <list type="bullet">
/// <item><description><c>iCCP</c>: profile name <c>ICC profile</c>, zlib-compressed profile (the plan checked that its color space matches the pixels);</description></item>
/// <item><description><c>pHYs</c>: nearest pixels per meter, unit 1 (<see cref="ResolutionConversion.ToPngPhys"/>);</description></item>
/// <item><description><c>eXIf</c>: the TIFF-structured EXIF data with the typed orientation and the canvas dimensions reconciled;</description></item>
/// <item><description><c>iTXt</c> <c>XML:com.adobe.xmp</c>: the XMP packet, uncompressed (as the XMP specification recommends), empty language and translated keyword;</description></item>
/// <item><description>
/// text entries in order: <c>tEXt</c> for Latin-1 values without language tag or translated keyword, <c>iTXt</c> (UTF-8)
/// otherwise; a value of at least <see cref="CompressionThreshold"/> bytes is compressed (<c>zTXt</c>, or a compressed
/// <c>iTXt</c>) when the compression level is not <see cref="CompressionLevel.NoCompression"/> and compression makes it smaller.
/// </description></item>
/// </list>
/// Each chunk is produced by one call (<see cref="Write"/>), so the encoder writes the metadata in bounded steps; sizes are
/// checked on creation so that an unrepresentable payload fails before any output.
/// </summary>
internal sealed class PngMetadataWriter
{
    /// <summary>The encoded text length from which compression is attempted.</summary>
    public const int CompressionThreshold = 1024;

    private const string IccProfileName = "ICC profile";
    private const string XmpKeyword = "XML:com.adobe.xmp";

    private readonly MetadataWritePlan _plan;
    private readonly CompressionLevel _compressionLevel;
    private readonly AllocationScope _scope;
    private readonly List<Item> _items = [];

    /// <summary>Plans the chunks of the metadata.</summary>
    /// <exception cref="UnsupportedImageFeatureException">A payload is too large for a PNG chunk.</exception>
    public PngMetadataWriter(MetadataWritePlan plan, CompressionLevel compressionLevel, AllocationScope scope)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(scope);
        _plan = plan;
        _compressionLevel = compressionLevel;
        _scope = scope;
        if (plan.IccProfile is { } icc)
        {
            // The compressed profile is checked again when written; a stored deflate block adds 5 bytes per 16 KiB at most
            EnsureFits(IccProfileName.Length + 2 + GetMaxCompressedLength(icc.Data.Length), "ICC color profile");
            _items.Add(new Item(ItemKind.Icc, -1));
        }

        if (plan.Resolution is not null)
        {
            _items.Add(new Item(ItemKind.Resolution, -1));
        }

        if (plan.Exif is { } exif)
        {
            EnsureFits(exif.Length, "EXIF profile");
            _items.Add(new Item(ItemKind.Exif, -1));
        }

        if (plan.XmpProfile is { } xmp)
        {
            EnsureFits(XmpKeyword.Length + 5L + xmp.Data.Length, "XMP packet");
            _items.Add(new Item(ItemKind.Xmp, -1));
        }

        for (var i = 0; i < plan.TextEntries.Count; i++)
        {
            var entry = plan.TextEntries[i];
            // At most 3 UTF-8 bytes per UTF-16 code unit
            var maxLength = entry.Keyword.Length + 5L + (3L * ((entry.LanguageTag?.Length ?? 0) + (entry.TranslatedKeyword?.Length ?? 0) + (long)entry.Value.Length));
            EnsureFits(maxLength, string.Create(CultureInfo.InvariantCulture, $"text entry '{entry.Keyword}'"));
            _items.Add(new Item(ItemKind.Text, i));
        }
    }

    private enum ItemKind
    {
        Icc,
        Resolution,
        Exif,
        Xmp,
        Text,
    }

    /// <summary>Gets the number of chunks.</summary>
    public int Count => _items.Count;

    /// <summary>Writes one chunk.</summary>
    /// <param name="index">The chunk index, 0 to <see cref="Count"/> - 1.</param>
    /// <param name="output">The output.</param>
    public void Write(int index, IBufferWriter<byte> output)
    {
        var item = _items[index];
        switch (item.Kind)
        {
            case ItemKind.Icc:
                WriteIccProfile(output, _plan.IccProfile!);
                break;

            case ItemKind.Resolution:
                WriteResolution(output, _plan.Resolution!);
                break;

            case ItemKind.Exif:
                PngChunkWriter.Write(output, PngChunkWriter.Exif, _plan.Exif);
                break;

            case ItemKind.Xmp:
                WriteInternationalText(output, XmpKeyword, languageTag: "", translatedKeyword: "", _plan.XmpProfile!.Data.Span, allowCompression: false);
                break;

            default:
                WriteText(output, _plan.TextEntries[item.TextIndex]);
                break;
        }
    }

    private static long GetMaxCompressedLength(long length) => length + (5 * ((length / 16_383) + 1)) + 6;

    private static void EnsureFits(long length, string item)
    {
        if (length > PngChunkWriter.MaxDataLength)
            throw new UnsupportedImageFeatureException($"The {item} is too large for a PNG chunk (at most {PngChunkWriter.MaxDataLength.ToString(CultureInfo.InvariantCulture)} bytes).", ImageFormat.Png, "Metadata: " + item);
    }

    private static void WriteResolution(IBufferWriter<byte> output, ImageResolution resolution)
    {
        var (x, y) = ResolutionConversion.ToPngPhys(resolution);
        Span<byte> data = stackalloc byte[9];
        BinaryPrimitives.WriteUInt32BigEndian(data, x);
        BinaryPrimitives.WriteUInt32BigEndian(data[4..], y);
        data[8] = 1; // unit: meter
        PngChunkWriter.Write(output, PngChunkWriter.Phys, data);
    }

    private void WriteIccProfile(IBufferWriter<byte> output, IccProfile profile)
    {
        // Profile name, NUL, compression method 0, zlib datastream (always compressed, as iCCP requires)
        using var compressed = Compress(profile.Data.Span);
        var data = compressed.WrittenMemory.Span;
        var crc = PngChunkWriter.Begin(output, PngChunkWriter.Iccp, IccProfileName.Length + 2L + data.Length);
        PngChunkWriter.Append(output, ref crc, Encoding.Latin1.GetBytes(IccProfileName));
        PngChunkWriter.Append(output, ref crc, [0, 0]);
        PngChunkWriter.Append(output, ref crc, data);
        PngChunkWriter.End(output, crc);
    }

    private void WriteText(IBufferWriter<byte> output, ImageTextEntry entry)
    {
        if (entry.LanguageTag is null && entry.TranslatedKeyword is null && !entry.Value.AsSpan().ContainsAnyExceptInRange('\0', 'ÿ'))
        {
            WriteLatin1Text(output, entry.Keyword, entry.Value);
        }
        else
        {
            WriteInternationalText(output, entry.Keyword, entry.LanguageTag ?? "", entry.TranslatedKeyword ?? "", Encoding.UTF8.GetBytes(entry.Value), allowCompression: true);
        }
    }

    private void WriteLatin1Text(IBufferWriter<byte> output, string keyword, string value)
    {
        var keywordBytes = Encoding.Latin1.GetBytes(keyword);
        var text = Encoding.Latin1.GetBytes(value);
        using var compressed = TryCompress(text);
        if (compressed is not null)
        {
            // zTXt: keyword, NUL, compression method 0, zlib datastream
            var data = compressed.WrittenMemory.Span;
            var crc = PngChunkWriter.Begin(output, PngChunkWriter.Ztxt, keywordBytes.Length + 2L + data.Length);
            PngChunkWriter.Append(output, ref crc, keywordBytes);
            PngChunkWriter.Append(output, ref crc, [0, 0]);
            PngChunkWriter.Append(output, ref crc, data);
            PngChunkWriter.End(output, crc);
        }
        else
        {
            // tEXt: keyword, NUL, text
            var crc = PngChunkWriter.Begin(output, PngChunkWriter.Text, keywordBytes.Length + 1L + text.Length);
            PngChunkWriter.Append(output, ref crc, keywordBytes);
            PngChunkWriter.Append(output, ref crc, 0);
            PngChunkWriter.Append(output, ref crc, text);
            PngChunkWriter.End(output, crc);
        }
    }

    private void WriteInternationalText(IBufferWriter<byte> output, string keyword, string languageTag, string translatedKeyword, ReadOnlySpan<byte> text, bool allowCompression)
    {
        // iTXt: keyword, NUL, compression flag, compression method, language tag, NUL, translated keyword (UTF-8), NUL, text (UTF-8)
        var keywordBytes = Encoding.Latin1.GetBytes(keyword);
        var languageBytes = Encoding.ASCII.GetBytes(languageTag);
        var translatedBytes = Encoding.UTF8.GetBytes(translatedKeyword);
        using var compressed = allowCompression ? TryCompress(text) : null;
        var payload = compressed is null ? text : compressed.WrittenMemory.Span;
        var crc = PngChunkWriter.Begin(output, PngChunkWriter.Itxt, keywordBytes.Length + 5L + languageBytes.Length + translatedBytes.Length + payload.Length);
        PngChunkWriter.Append(output, ref crc, keywordBytes);
        PngChunkWriter.Append(output, ref crc, [0, compressed is null ? (byte)0 : (byte)1, 0]);
        PngChunkWriter.Append(output, ref crc, languageBytes);
        PngChunkWriter.Append(output, ref crc, 0);
        PngChunkWriter.Append(output, ref crc, translatedBytes);
        PngChunkWriter.Append(output, ref crc, 0);
        PngChunkWriter.Append(output, ref crc, payload);
        PngChunkWriter.End(output, crc);
    }

    /// <summary>Compresses a text payload when it is long enough and compression makes it smaller.</summary>
    private ImageOutputBuffer? TryCompress(ReadOnlySpan<byte> text)
    {
        if (text.Length < CompressionThreshold || _compressionLevel == CompressionLevel.NoCompression)
            return null;

        var compressed = Compress(text);
        if (compressed.Length < text.Length)
            return compressed;

        compressed.Dispose();
        return null;
    }

    /// <summary>Compresses a payload as a zlib datastream into a buffer charged to the writer scope.</summary>
    private ImageOutputBuffer Compress(ReadOnlySpan<byte> data)
    {
        var buffer = new ImageOutputBuffer(_scope);
        try
        {
            using (var zlib = new ZLibStream(new BufferWriterStream(buffer), _compressionLevel, leaveOpen: false))
            {
                zlib.Write(data);
            }

            EnsureFits(buffer.Length, "compressed metadata");
            return buffer;
        }
        catch
        {
            buffer.Dispose();
            throw;
        }
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Item(ItemKind Kind, int TextIndex);

    /// <summary>A write-only stream appending to a buffer writer.</summary>
    private sealed class BufferWriterStream(IBufferWriter<byte> output) : Stream
    {
        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            buffer.CopyTo(output.GetSpan(buffer.Length));
            output.Advance(buffer.Length);
        }

        public override void WriteByte(byte value) => Write([value]);

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
