using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Writes an ICC profile after the image header (ISO/IEC 18181-1, annex E), so that a decoder gives it back byte for byte:
/// the header is coded as differences from its prediction, and the rest of the profile is inserted as it is.
/// </summary>
internal static class JpegXlIccEncoder
{
    public static void Write(LsbBitWriter writer, ReadOnlySpan<byte> profile, AllocationScope scope)
    {
        // The commands: no tag list (a count of zero), then one insertion of everything that follows the header
        var headerLength = Math.Min(profile.Length, JpegXlIccDecoder.HeaderSize);
        var tailLength = profile.Length - headerLength;
        Span<byte> commands = stackalloc byte[16];
        var commandLength = 0;
        if (profile.Length > JpegXlIccDecoder.HeaderSize)
        {
            commands[commandLength++] = 0;
            commands[commandLength++] = 1;
            commandLength += WriteVarInt(commands[commandLength..], (ulong)tailLength);
        }

        Span<byte> prefix = stackalloc byte[24];
        var prefixLength = WriteVarInt(prefix, (ulong)profile.Length);
        prefixLength += WriteVarInt(prefix[prefixLength..], (ulong)commandLength);

        var encodedLength = prefixLength + commandLength + profile.Length;
        using var encoded = scope.Rent(encodedLength, AllocationKind.Temporary, clear: false);
        var data = encoded.RawBuffer.AsSpan(0, encodedLength);
        prefix[..prefixLength].CopyTo(data);
        commands[..commandLength].CopyTo(data[prefixLength..]);
        var body = data[(prefixLength + commandLength)..];

        Span<byte> prediction = stackalloc byte[JpegXlIccDecoder.HeaderSize];
        JpegXlIccDecoder.InitialHeaderPrediction.CopyTo(prediction);
        BinaryPrimitives.WriteUInt32BigEndian(prediction, (uint)profile.Length);
        for (var i = 0; i < headerLength; i++)
        {
            JpegXlIccDecoder.PredictHeader(profile[..i], prediction, i);
            body[i] = (byte)(profile[i] - prediction[i]);
        }

        profile[headerLength..].CopyTo(body[headerLength..]);

        writer.WriteU64((ulong)encodedLength);
        using var stream = new JpegXlEntropyEncoder(JpegXlIccDecoder.ContextCount, new JpegXlHybridUintConfig(8, 0, 0), scope);
        byte previous = 0;
        byte beforePrevious = 0;
        for (var i = 0; i < data.Length; i++)
        {
            stream.Add(JpegXlIccDecoder.GetContext(i, previous, beforePrevious), data[i]);
            beforePrevious = previous;
            previous = data[i];
        }

        stream.Write(writer);
    }

    private static int WriteVarInt(Span<byte> destination, ulong value)
    {
        var length = 0;
        while (value >= 0x80)
        {
            destination[length++] = (byte)(value | 0x80);
            value >>= 7;
        }

        destination[length++] = (byte)value;
        return length;
    }
}
