using System.Buffers;

namespace Meziantou.Framework.Bencode;

public static class BencodeValueExtensions
{
    /// <summary>Encodes a bencode value to a UTF-8 byte array.</summary>
    /// <param name="value">The value to encode.</param>
    /// <param name="canonical">
    /// <see langword="true"/> to sort dictionary keys by their byte order (canonical bencode);
    /// <see langword="false"/> to keep dictionary insertion order.
    /// </param>
    /// <remarks>
    /// Canonical ordering is important when deterministic output is required. Since dictionary order changes the encoded bytes,
    /// sorting keys ensures equivalent dictionaries produce the same serialized value, which is required for stable hashes
    /// (for example torrent info-hashes) and cross-implementation consistency.
    /// </remarks>
    public static byte[] ToUtf8ByteArray(this BencodeValue value, bool canonical = true)
    {
        ArgumentNullException.ThrowIfNull(value);

        var buffer = new ArrayBufferWriter<byte>();
        var writer = new BencodeWriter(buffer);
        value.WriteTo(writer, canonical);
        writer.Complete();
        return buffer.WrittenSpan.ToArray();
    }

    public static async ValueTask WriteToAsync(this BencodeValue value, Stream stream, bool canonical = true, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(stream);

        var data = value.ToUtf8ByteArray(canonical);
        await stream.WriteAsync(data.AsMemory(), cancellationToken).ConfigureAwait(false);
    }
}
