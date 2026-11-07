namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Writes open bitstream units (AV1 specification, sections 5.3.1 and 5.3.2): a header without extension, the size of the
/// payload, then the payload. Every unit has a size field, which is what the samples and the items of an AVIF file require
/// of all the units but the last one, and what a raw stream of units requires of all of them.
/// </summary>
internal static class Av1ObuWriter
{
    /// <summary>Appends a unit of type <paramref name="type"/> to <paramref name="output"/>.</summary>
    public static void Write(PooledPayloadWriter output, int type, ReadOnlySpan<byte> payload)
    {
        WriteHeader(output, type, payload.Length);
        output.Write(payload);
    }

    /// <summary>Appends the header of a unit whose payload of <paramref name="payloadLength"/> bytes is written next.</summary>
    public static void WriteHeader(PooledPayloadWriter output, int type, int payloadLength)
    {
        var writer = new Av1BitWriter(output);
        writer.WriteBits(0, 1); // obu_forbidden_bit
        writer.WriteBits((uint)type, 4);
        writer.WriteBits(0, 1); // obu_extension_flag
        writer.WriteBits(1, 1); // obu_has_size_field
        writer.WriteBits(0, 1); // obu_reserved_1bit
        writer.WriteLeb128((uint)payloadLength);
    }

    /// <summary>Appends a temporal delimiter, which starts a temporal unit of a raw stream. AVIF samples and items have none.</summary>
    public static void WriteTemporalDelimiter(PooledPayloadWriter output) => WriteHeader(output, Av1Constants.ObuTemporalDelimiter, 0);
}
