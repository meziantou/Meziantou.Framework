using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Serializes the metadata of a <see cref="MetadataWritePlan"/> (already reconciled and filtered by the save-time policy)
/// as JPEG marker segments, written after the JFIF APP0 segment and before the tables, in this
/// order:
/// <list type="bullet">
/// <item><description>APP1 <c>Exif\0\0</c>: the TIFF-structured EXIF data, typed orientation and canvas dimensions reconciled (at most <see cref="MetadataWritePlan.MaxJpegExifBytes"/> bytes);</description></item>
/// <item><description>APP1 <c>http://ns.adobe.com/xap/1.0/\0</c>: the standard XMP packet (at most <see cref="MetadataWritePlan.MaxJpegStandardXmpBytes"/> bytes; extended XMP is not written);</description></item>
/// <item><description>
/// APP2 <c>ICC_PROFILE\0</c>: the profile split into chunks of at most <see cref="MaxIccChunkBytes"/> bytes, each preceded by
/// its 1-based sequence number and the chunk count (ICC.1 Annex B.4; at most 255 chunks);
/// </description></item>
/// <item><description>COM: each <c>Comment</c> text entry, Latin-1, one segment per entry (at most <see cref="MaxCommentBytes"/> bytes).</description></item>
/// </list>
/// The JFIF density is part of the APP0 segment (<see cref="WriteJfif"/>). Each segment is produced by one call
/// (<see cref="Write"/>), so the encoder writes the metadata in bounded steps.
/// </summary>
internal sealed class JpegMetadataWriter
{
    /// <summary>The largest payload of a marker segment (the 16-bit length counts itself).</summary>
    public const int MaxSegmentPayload = ushort.MaxValue - 2;

    /// <summary>The largest ICC data of one APP2 segment (the payload minus the 12-byte signature and the 2 counters).</summary>
    public const int MaxIccChunkBytes = MaxSegmentPayload - 14;

    /// <summary>The largest COM payload.</summary>
    public const int MaxCommentBytes = MaxSegmentPayload;

    private const byte MarkerApp0 = 0xE0;
    private const byte MarkerApp1 = 0xE1;
    private const byte MarkerApp2 = 0xE2;
    private const byte MarkerCom = 0xFE;

    private readonly MetadataWritePlan _plan;
    private readonly List<Item> _items = [];
    private readonly int _iccChunkCount;

    /// <summary>Plans the segments of the metadata.</summary>
    /// <param name="plan">The metadata plan (sizes already checked against the JPEG limits).</param>
    /// <exception cref="UnsupportedImageFeatureException">A payload does not fit its segments.</exception>
    public JpegMetadataWriter(MetadataWritePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        _plan = plan;
        if (plan.Exif is { } exif)
        {
            EnsureFits(exif.Length, MetadataWritePlan.MaxJpegExifBytes, "EXIF profile");
            _items.Add(new Item(ItemKind.Exif, 0));
        }

        if (plan.XmpProfile is { } xmp)
        {
            EnsureFits(xmp.Data.Length, MetadataWritePlan.MaxJpegStandardXmpBytes, "XMP packet");
            _items.Add(new Item(ItemKind.Xmp, 0));
        }

        if (plan.IccProfile is { } icc)
        {
            EnsureFits(icc.Data.Length, MetadataWritePlan.MaxJpegIccBytes, "ICC color profile");
            _iccChunkCount = Math.Max(1, (icc.Data.Length + MaxIccChunkBytes - 1) / MaxIccChunkBytes);
            for (var i = 0; i < _iccChunkCount; i++)
            {
                _items.Add(new Item(ItemKind.Icc, i));
            }
        }

        for (var i = 0; i < plan.TextEntries.Count; i++)
        {
            EnsureFits(plan.TextEntries[i].Value.Length, MaxCommentBytes, "comment");
            _items.Add(new Item(ItemKind.Comment, i));
        }
    }

    private enum ItemKind
    {
        Exif,
        Xmp,
        Icc,
        Comment,
    }

    /// <summary>Gets the number of segments.</summary>
    public int Count => _items.Count;

    /// <summary>Gets the number of APP2 segments of the ICC profile (0 without a profile).</summary>
    public int IccChunkCount => _iccChunkCount;

    private static ReadOnlySpan<byte> ExifSignature => "Exif\0\0"u8;

    private static ReadOnlySpan<byte> XmpSignature => "http://ns.adobe.com/xap/1.0/\0"u8;

    private static ReadOnlySpan<byte> IccSignature => "ICC_PROFILE\0"u8;

    /// <summary>
    /// Writes the JFIF APP0 segment (JFIF 1.02): version 1.02, the density of <see cref="MetadataWritePlan.Resolution"/>
    /// (<see cref="ResolutionConversion.ToJfifDensity"/>: dots per inch, or dots per centimeter when exact) or a 1:1 aspect
    /// ratio without unit, and no thumbnail.
    /// </summary>
    /// <param name="output">The output.</param>
    /// <param name="plan">The metadata plan.</param>
    public static void WriteJfif(IBufferWriter<byte> output, MetadataWritePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var (units, x, y) = plan.Resolution is { } resolution ? ResolutionConversion.ToJfifDensity(resolution) : ((byte)0, (ushort)1, (ushort)1);
        Span<byte> payload = stackalloc byte[14];
        "JFIF\0"u8.CopyTo(payload);
        payload[5] = 1; // major version
        payload[6] = 2; // minor version
        payload[7] = units;
        BinaryPrimitives.WriteUInt16BigEndian(payload[8..], x);
        BinaryPrimitives.WriteUInt16BigEndian(payload[10..], y);
        payload[12] = 0; // no thumbnail
        payload[13] = 0;
        WriteSegment(output, MarkerApp0, payload);
    }

    /// <summary>Writes a marker segment: <c>FF</c>, the marker, the big-endian length (payload + 2) and the payload.</summary>
    /// <param name="output">The output.</param>
    /// <param name="marker">The marker code.</param>
    /// <param name="payload">The payload (at most <see cref="MaxSegmentPayload"/> bytes).</param>
    public static void WriteSegment(IBufferWriter<byte> output, byte marker, ReadOnlySpan<byte> payload) => WriteSegment(output, marker, [], payload);

    /// <summary>Writes one segment.</summary>
    /// <param name="index">The segment index, 0 to <see cref="Count"/> - 1.</param>
    /// <param name="output">The output.</param>
    public void Write(int index, IBufferWriter<byte> output)
    {
        var item = _items[index];
        switch (item.Kind)
        {
            case ItemKind.Exif:
                WriteSegment(output, MarkerApp1, ExifSignature, _plan.Exif);
                break;

            case ItemKind.Xmp:
                WriteSegment(output, MarkerApp1, XmpSignature, _plan.XmpProfile!.Data.Span);
                break;

            case ItemKind.Icc:
            {
                var data = _plan.IccProfile!.Data.Span;
                var start = item.Index * MaxIccChunkBytes;
                var chunk = data.Slice(start, Math.Min(MaxIccChunkBytes, data.Length - start));
                Span<byte> header = stackalloc byte[14];
                IccSignature.CopyTo(header);
                header[12] = (byte)(item.Index + 1); // sequence number, 1-based
                header[13] = (byte)_iccChunkCount;
                WriteSegment(output, MarkerApp2, header, chunk);
                break;
            }

            default:
                WriteSegment(output, MarkerCom, [], Encoding.Latin1.GetBytes(_plan.TextEntries[item.Index].Value));
                break;
        }
    }

    private static void WriteSegment(IBufferWriter<byte> output, byte marker, ReadOnlySpan<byte> header, ReadOnlySpan<byte> payload)
    {
        var length = header.Length + payload.Length;
        if (length > MaxSegmentPayload)
            throw new InvalidOperationException("The JPEG marker segment is too long.");

        var span = output.GetSpan(4 + length);
        span[0] = 0xFF;
        span[1] = marker;
        BinaryPrimitives.WriteUInt16BigEndian(span[2..], (ushort)(length + 2));
        header.CopyTo(span[4..]);
        payload.CopyTo(span[(4 + header.Length)..]);
        output.Advance(4 + length);
    }

    private static void EnsureFits(long length, long max, string item)
    {
        if (length > max)
            throw new UnsupportedImageFeatureException(string.Create(CultureInfo.InvariantCulture, $"The {item} is too large for JPEG marker segments (at most {max} bytes)."), ImageFormat.Jpeg, "Metadata: " + item);
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Item(ItemKind Kind, int Index);
}
