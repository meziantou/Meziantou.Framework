using System.Text;
using System.Text.RegularExpressions;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.WebTransport.Http3;

namespace Meziantou.Framework.WebTransport.Fuzz;

/// <summary>
/// The decoders of the HTTP/3 layer of WebTransport read what the peer sends: the frames of a stream, QPACK field
/// sections and their Huffman strings, capsules, SETTINGS frames, variable-length integers and the structured fields of
/// the protocol negotiation.
/// They reject invalid input with an <c>Http3ProtocolException</c> only, and what they accept round-trips through the
/// encoders.
/// </summary>
public static partial class WebTransportFuzzTargets
{
    private const int MaxFieldSectionSize = 64 * 1024;
    private const int MaxFramePayloadLength = 1024;

    private static readonly string[] Tokens =
    [
        ":method", ":protocol", ":scheme", ":authority", ":path", ":status", "CONNECT", "webtransport", "https", "200", "404",
        "origin", "wt-available-protocols", "wt-protocol", "\"", "\\", ",", ";", " ",
    ];

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("webtransport-qpack-decoder", QPackDecoderTarget) { Seeds = GetQPackSeeds, Dictionary = Tokens },
        new("webtransport-huffman", HuffmanTarget) { Seeds = GetHuffmanSeeds },
        new("webtransport-capsules", CapsulesTarget) { Seeds = GetCapsuleSeeds },
        new("webtransport-settings", SettingsTarget) { Seeds = GetSettingsSeeds },
        new("webtransport-variable-length-integer", VariableLengthIntegerTarget) { Seeds = GetVariableLengthIntegerSeeds },
        new("webtransport-structured-fields", StructuredFieldsTarget) { Seeds = GetStructuredFieldSeeds, Dictionary = Tokens },
        new("webtransport-frame-reader", FrameReaderTarget) { Seeds = GetFrameReaderSeeds },
    ];

    /// <summary>
    /// The first byte gives the size of the chunks (1 to 16 bytes) a stream returns the rest of the input in. The frames the
    /// reader reads from it are the ones of a parse of the whole input: same types, lengths and payloads (frames of an odd
    /// type are skipped). The input ends between two frames, or the reader fails with H3_FRAME_ERROR for a truncated frame
    /// and H3_EXCESSIVE_LOAD for a payload over the limit.
    /// </summary>
    private static void FrameReaderTarget(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            return;

        var content = data[1..].ToArray();
        var reader = new Http3FrameReader(new ChunkedStream(content, chunkSize: (data[0] & 0x0F) + 1));
        var position = 0;
        while (true)
        {
            // The header of the next frame, parsed from the whole input
            var lengthLength = 0;
            var expectedLength = 0L;
            var hasHeader = VariableLengthInteger.TryRead(content.AsSpan(position), out var expectedType, out var typeLength)
                && VariableLengthInteger.TryRead(content.AsSpan(position + typeLength), out expectedLength, out lengthLength);

            (long Type, long Length)? header;
            try
            {
                header = Wait(reader.ReadFrameHeaderAsync(CancellationToken.None));
            }
            catch (Http3ProtocolException ex)
            {
                FuzzAssert.False(hasHeader, "A complete frame header was rejected");
                FuzzAssert.True(position < content.Length, "The end of the input between two frames was rejected");
                FuzzAssert.Equal(Http3ErrorCode.FrameError, ex.ErrorCode, "A truncated frame header has the wrong error");
                return;
            }

            if (header is null)
            {
                FuzzAssert.Equal(content.Length, position, "The reader ended before the end of the input");
                return;
            }

            FuzzAssert.True(hasHeader, "A truncated frame header was read");
            FuzzAssert.Equal((expectedType, expectedLength), header.Value, "The frame header differs from the input");
            position += typeLength + lengthLength;

            var isTruncated = expectedLength > content.Length - position;
            try
            {
                if ((expectedType & 1) == 0)
                {
                    var payload = Wait(reader.ReadPayloadAsync(expectedLength, MaxFramePayloadLength, CancellationToken.None));
                    FuzzAssert.True(expectedLength <= MaxFramePayloadLength && !isTruncated, "A payload over the limit or truncated was read");
                    FuzzAssert.True(payload.AsSpan().SequenceEqual(content.AsSpan(position, (int)expectedLength)), "The payload differs from the input");
                }
                else
                {
                    Wait(reader.SkipAsync(expectedLength, CancellationToken.None));
                    FuzzAssert.False(isTruncated, "A truncated payload was skipped");
                }
            }
            catch (Http3ProtocolException ex)
            {
                var expectedError = (expectedType & 1) == 0 && expectedLength > MaxFramePayloadLength ? Http3ErrorCode.ExcessiveLoad : Http3ErrorCode.FrameError;
                FuzzAssert.True(expectedError == Http3ErrorCode.ExcessiveLoad || isTruncated, "A complete payload was rejected");
                FuzzAssert.Equal(expectedError, ex.ErrorCode, "The payload was rejected with the wrong error");
                return;
            }

            position += (int)expectedLength;
        }
    }

    // The reads of the chunked stream complete synchronously
    private static T Wait<T>(ValueTask<T> task) => task.AsTask().GetAwaiter().GetResult();

    private static void Wait(ValueTask task) => task.AsTask().GetAwaiter().GetResult();

    // A stream that returns its content in chunks of at most chunkSize bytes, as a network stream does
    private sealed class ChunkedStream(byte[] content, int chunkSize) : Stream
    {
        private int _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            var count = Math.Min(Math.Min(buffer.Length, chunkSize), content.Length - _position);
            content.AsSpan(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => ValueTask.FromResult(Read(buffer.Span));

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>A decoded field section is re-encoded, and decodes to the same fields.</summary>
    private static void QPackDecoderTarget(ReadOnlySpan<byte> data)
    {
        List<QPackField> fields;
        try
        {
            fields = QPackDecoder.Decode(data, MaxFieldSectionSize);
        }
        catch (Http3ProtocolException)
        {
            return;
        }

        FuzzAssert.True(fields.Sum(field => field.Size) <= MaxFieldSectionSize, "The field section exceeds the limit");
        var encoded = QPackEncoder.Encode(fields);
        var decoded = QPackDecoder.Decode(encoded, int.MaxValue);
        FuzzAssert.True(fields.SequenceEqual(decoded), "The re-encoded field section does not decode to the same fields");
    }

    /// <summary>
    /// The input decodes to bytes whose encoding is the input (the code is prefix-free and the padding is canonical), and the
    /// encoding of the input decodes to the input.
    /// </summary>
    private static void HuffmanTarget(ReadOnlySpan<byte> data)
    {
        var encoded = new List<byte>();
        Huffman.Encode(data, encoded);
        FuzzAssert.Equal(Huffman.GetEncodedLength(data), encoded.Count, "The encoded length is wrong");
        FuzzAssert.True(Huffman.Decode([.. encoded]).AsSpan().SequenceEqual(data), "The encoding does not decode to the input");

        byte[] decoded;
        try
        {
            decoded = Huffman.Decode(data);
        }
        catch (Http3ProtocolException)
        {
            return;
        }

        var reencoded = new List<byte>();
        Huffman.Encode(decoded, reencoded);
        FuzzAssert.True(reencoded.SequenceEqual(data.ToArray()), "Re-encoding the decoded string does not give the input");
    }

    /// <summary>
    /// The capsules read from the input use a prefix of it, and re-encoding them gives capsules that read the same. A
    /// CLOSE_WEBTRANSPORT_SESSION capsule that decodes re-encodes to the same code and reason.
    /// </summary>
    private static void CapsulesTarget(ReadOnlySpan<byte> data)
    {
        var capsules = new List<Capsule>();
        int consumed;
        try
        {
            consumed = Capsule.ReadCapsules(data, capsules, maxCapsuleSize: 4096);
        }
        catch (Http3ProtocolException)
        {
            return;
        }

        FuzzAssert.True(consumed >= 0 && consumed <= data.Length, "The consumed length is out of bounds");
        var reencoded = capsules.SelectMany(capsule => Capsule.Encode(capsule.Type, capsule.Value.Span)).ToArray();
        var reread = new List<Capsule>();
        FuzzAssert.Equal(reencoded.Length, Capsule.ReadCapsules(reencoded, reread, maxCapsuleSize: 4096), "The re-encoded capsules are not read entirely");
        FuzzAssert.Equal(capsules.Count, reread.Count, "The re-encoded capsules differ");
        for (var i = 0; i < capsules.Count; i++)
        {
            FuzzAssert.Equal(capsules[i].Type, reread[i].Type, "The re-encoded capsule type differs");
            FuzzAssert.True(capsules[i].Value.Span.SequenceEqual(reread[i].Value.Span), "The re-encoded capsule value differs");
            if (capsules[i].Type == Capsule.CloseWebTransportSession)
            {
                (uint ErrorCode, string Reason) close;
                try
                {
                    close = Capsule.DecodeClose(capsules[i].Value.Span);
                }
                catch (Http3ProtocolException)
                {
                    continue;
                }

                var capsule = Capsule.EncodeClose(close.ErrorCode, close.Reason);
                var closeCapsules = new List<Capsule>();
                Capsule.ReadCapsules(capsule, closeCapsules, maxCapsuleSize: 4096);
                FuzzAssert.Equal(close, Capsule.DecodeClose(closeCapsules[0].Value.Span), "The CLOSE capsule does not round-trip");
            }
        }
    }

    /// <summary>A SETTINGS frame that parses has unique, non-reserved identifiers.</summary>
    private static void SettingsTarget(ReadOnlySpan<byte> data)
    {
        try
        {
            var settings = Http3Settings.Parse(data);
            _ = settings.SupportsWebTransport;
        }
        catch (Http3ProtocolException)
        {
        }
    }

    /// <summary>A variable-length integer that reads re-encodes in at most as many bytes, to the same value.</summary>
    private static void VariableLengthIntegerTarget(ReadOnlySpan<byte> data)
    {
        if (!VariableLengthInteger.TryRead(data, out var value, out var length))
        {
            FuzzAssert.True(data.IsEmpty || VariableLengthInteger.GetLength(data[0]) > data.Length, "A complete integer was not read");
            return;
        }

        FuzzAssert.True(value is >= 0 and <= VariableLengthInteger.MaxValue, "The value is out of range");
        var buffer = new byte[8];
        var written = VariableLengthInteger.Write(buffer, value);
        FuzzAssert.True(written <= length, "The minimal encoding is longer than the input");
        FuzzAssert.True(VariableLengthInteger.TryRead(buffer.AsSpan(0, written), out var reread, out _) && reread == value, "The integer does not round-trip");
    }

    /// <summary>A list of strings (wt-available-protocols) or a string (wt-protocol) that parses round-trips through serialization.</summary>
    private static void StructuredFieldsTarget(ReadOnlySpan<byte> data)
    {
        var text = FuzzText.Decode(data);
        if (StructuredFieldStrings.ParseList(text) is { } list)
        {
            FuzzAssert.True(list.SequenceEqual(StructuredFieldStrings.ParseList(StructuredFieldStrings.SerializeList(list)) ?? []), "The list does not round-trip");
        }

        if (StructuredFieldStrings.ParseItem(text) is { } item)
        {
            FuzzAssert.Equal(item, StructuredFieldStrings.ParseItem(StructuredFieldStrings.SerializeString(item)), "The string does not round-trip");
        }
    }

    // The hexadecimal vectors of the unit tests
    private static IEnumerable<byte[]> GetTestVectors()
    {
        var source = FuzzResources.ReadText(typeof(WebTransportFuzzTargets).Assembly, "Http3ProtocolTests.cs");
        foreach (Match match in HexStringRegex().Matches(source))
        {
            yield return Convert.FromHexString(match.Groups["hex"].Value);
        }
    }

    private static IEnumerable<FuzzSeed> GetQPackSeeds()
    {
        var index = 0;
        foreach (var vector in GetTestVectors())
        {
            yield return new FuzzSeed("vector-" + index++, vector);
        }

        yield return new FuzzSeed("connect-request", QPackEncoder.Encode(
        [
            new(":method", "CONNECT"),
            new(":protocol", "webtransport"),
            new(":scheme", "https"),
            new(":authority", "example.com:4433"),
            new(":path", "/echo?query=1"),
            new("origin", "https://example.com"),
            new("wt-available-protocols", "\"a\", \"b\""),
        ]));
        yield return new FuzzSeed("response", QPackEncoder.Encode([new(":status", "200"), new("wt-protocol", "\"a\"")]));
    }

    private static IEnumerable<FuzzSeed> GetHuffmanSeeds()
    {
        string[] values = ["www.example.com", "no-cache", "custom-key", "custom-value", "webtransport", "https://example.com"];
        foreach (var value in values)
        {
            var encoded = new List<byte>();
            Huffman.Encode(Encoding.ASCII.GetBytes(value), encoded);
            yield return new FuzzSeed(value, encoded.ToArray());
        }
    }

    // The first byte is the size of the chunks, minus 1
    private static IEnumerable<FuzzSeed> GetFrameReaderSeeds()
    {
        var settings = Http3Settings.EncodeLocalSettings(MaxFieldSectionSize, maxSessions: 100);
        var headers = Http3FrameReader.EncodeFrame(Http3FrameType.Headers, QPackEncoder.Encode([new(":status", "200")]));
        var data = Http3FrameReader.EncodeFrame(Http3FrameType.Data, Capsule.EncodeClose(42, "bye"));
        var unknown = Http3FrameReader.EncodeFrame(0x21, "reserved"u8);
        yield return new FuzzSeed("settings", (byte[])[0, .. settings]);
        yield return new FuzzSeed("headers-and-data", (byte[])[2, .. headers, .. unknown, .. data]);
        yield return new FuzzSeed("truncated-payload", (byte[])[15, .. data.AsSpan(0, data.Length - 1)]);
        yield return new FuzzSeed("truncated-header", (byte[])[0, 0x40]);
        yield return new FuzzSeed("payload-over-the-limit", (byte[])[3, 0x00, 0x48, 0x00]);
    }

    private static IEnumerable<FuzzSeed> GetCapsuleSeeds()
    {
        yield return new FuzzSeed("close", Capsule.EncodeClose(42, "bye"));
        yield return new FuzzSeed("drain", Capsule.Encode(Capsule.DrainWebTransportSession, []));
        yield return new FuzzSeed("datagram-and-close", (byte[])[.. Capsule.Encode(Capsule.Datagram, "payload"u8), .. Capsule.EncodeClose(0, "")]);
    }

    private static IEnumerable<FuzzSeed> GetSettingsSeeds()
    {
        var frame = Http3Settings.EncodeLocalSettings(MaxFieldSectionSize, maxSessions: 100);
        VariableLengthInteger.TryRead(frame, out _, out var typeLength);
        VariableLengthInteger.TryRead(frame.AsSpan(typeLength), out _, out var lengthLength);
        yield return new FuzzSeed("local-settings", frame.AsMemory(typeLength + lengthLength));
        yield return new FuzzSeed("duplicate", new byte[] { 0x06, 0x01, 0x06, 0x02 });
    }

    private static IEnumerable<FuzzSeed> GetVariableLengthIntegerSeeds()
    {
        string[] vectors = ["c2197c5eff14e88c", "9d7f3e7d", "7bbd", "25", "4025"];
        return vectors.Select(vector => new FuzzSeed(vector, Convert.FromHexString(vector)));
    }

    private static IEnumerable<FuzzSeed> GetStructuredFieldSeeds()
    {
        yield return FuzzSeed.FromText("list", "\"a\", \"b c\", \"quote\\\"backslash\\\\\"");
        yield return FuzzSeed.FromText("item", " \"proto\";param=1 ");
        yield return FuzzSeed.FromText("token", "token");
    }

    [GeneratedRegex("\"(?<hex>(?:[0-9a-f]{2}){2,})\"", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 10000)]
    private static partial Regex HexStringRegex();
}
