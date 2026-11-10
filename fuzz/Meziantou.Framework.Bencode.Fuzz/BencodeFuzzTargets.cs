using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Unicode;
using Meziantou.Framework.Bencode.Torrent;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Bencode.Fuzz;

/// <summary>
/// The decoders accept exactly the bencode documents a reference validator written from the specification accepts (one
/// encoding per integer and string, unique dictionary keys, at most 64 nested containers, no trailing data) and reject the
/// others with a <see cref="FormatException"/>. The span decoder and the <see cref="System.IO.Pipelines.PipeReader"/>
/// decoder agree whatever the chunks the data arrives in. A decoded value written back in insertion order gives the input,
/// and its canonical encoding is a fixed point. <see cref="BencodeWriter"/> rejects an out-of-order call with an
/// <see cref="InvalidOperationException"/> that leaves its output unchanged. <see cref="BencodeReader"/> returns the tokens of
/// the decoded value. A torrent that parses has a consistent model, its info-hash is the hash of the bytes of its 'info'
/// dictionary, and it round-trips through its writer.
/// </summary>
public static partial class BencodeFuzzTargets
{
    // BencodeDecoder.MaxDepth
    private const int MaxDepth = 64;

    // BencodeDecoder.MaxIntegerDigits
    private const int MaxIntegerDigits = 4096;

    private static readonly BencodeString InfoKey = new("info"u8.ToArray());

    private static readonly string[] Tokens =
    [
        "i", "e", "l", "d", ":", "-", "0", "le", "de", "lle", "dde", "i0e", "i1e", "i-1e", "i-0e", "i01e", "ie", "i-e", "i9223372036854775807e", "i-9223372036854775808e", "i9223372036854775808e", "i-9223372036854775809e",
        "0:", "1:", "2:", "4:", "20:", "00:", "01:", "2147483647:", "2147483648:", "4294967296:", "1:a", "1:b", "3:cow", "3:moo", "4:spam", "4:eggs",
        "4:info", "4:name", "6:length", "12:piece length", "6:pieces", "5:files", "4:path", "7:private", "6:source", "6:md5sum", "8:announce", "13:announce-list", "7:comment", "10:created by", "13:creation date",
        "8:url-list", "9:httpseeds", "5:nodes", "8:encoding", "9:publisher", "13:publisher-url", "2:..", "1:.", "1:/", "1:\\", "i65535e", "i65536e", "i16384e", "i253402300799e", "i253402300800e", "i-62135596801e",
        "i99999999999999999999e", "i-99999999999999999999e", "12:meta version", "9:file tree", "11:pieces root", "12:piece layers", "4:attr", "1:p", "i2e", "i32768e",
        "3:GBK", "12:windows-1251", "9:shift_jis", "10:iso-8859-1", "5:UTF-8", "6:utf-16", "32:", "64:", "96:",
    ];

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("bencode-parse", Parse) { Seeds = GetParseSeeds, Dictionary = Tokens },
        new("bencode-pipe-reader", PipeReaderParse) { Seeds = GetPipeReaderSeeds, Dictionary = Tokens },
        new("bencode-reader", Reader) { Seeds = GetPipeReaderSeeds, Dictionary = Tokens },
        new("bencode-writer", Writer) { Seeds = GetWriterSeeds },
        new("bencode-torrent", Torrent) { Seeds = GetTorrentSeeds, Dictionary = Tokens },
    ];

    /// <summary>
    /// The input is a document. <see cref="BencodeDocument.Parse"/> accepts it exactly when the reference validator does. A
    /// decoded value is consistent (dictionary lookups, depth, UTF-8 decoding), writing it in insertion order gives the input
    /// back, and its canonical encoding sorts the keys, decodes to the same value, and is a fixed point.
    /// </summary>
    private static void Parse(ReadOnlySpan<byte> data)
    {
        var isValid = IsValidDocument(data);
        BencodeDocument document;
        try
        {
            document = BencodeDocument.Parse(data);
        }
        catch (FormatException ex)
        {
            FuzzAssert.False(isValid, "A valid document was rejected: " + ex.Message);
            return;
        }

        FuzzAssert.True(isValid, "An invalid document was accepted");
        var root = document.Root;
        CheckValue(root, depth: 0);

        // Each integer and string has a single encoding, and the decoder keeps the order of the keys
        FuzzAssert.True(root.ToUtf8ByteArray(canonical: false).AsSpan().SequenceEqual(data), "Writing the value in insertion order does not give the input back");

        var canonical = document.ToArray();
        using (var stream = new MemoryStream())
        {
            document.WriteToAsync(stream).AsTask().GetAwaiter().GetResult();
            FuzzAssert.True(stream.ToArray().AsSpan().SequenceEqual(canonical), "WriteToAsync and ToArray write different bytes");
        }

        var reparsed = BencodeDocument.Parse(canonical).Root;
        FuzzAssert.True(AreEqual(root, reparsed, ordered: false), "The canonical encoding does not decode to the same value");
        CheckCanonicalOrder(reparsed);
        FuzzAssert.True(reparsed.ToUtf8ByteArray(canonical: false).AsSpan().SequenceEqual(canonical), "The canonical encoding is not a fixed point");
    }

    /// <summary>
    /// <c>mode, chunks, document</c>: the document reaches <see cref="BencodeDocument.ParseAsync(System.IO.Pipelines.PipeReader, CancellationToken)"/>
    /// in chunks whose sizes come from <c>chunks</c>, through a <see cref="ChunkedPipeReader"/> (even <c>mode</c>) or a stream
    /// (odd <c>mode</c>). It decodes to the value the span decoder returns, or both reject it. The decoder follows the
    /// <see cref="System.IO.Pipelines.PipeReader"/> contract and consumes the whole document when it succeeds.
    /// </summary>
    private static void PipeReaderParse(ReadOnlySpan<byte> data)
    {
        var mode = FuzzInput.ReadByte(ref data);
        var sizer = new ChunkSizer(FuzzInput.ReadByte(ref data));
        var bytes = data.ToArray();

        BencodeValue? expected;
        try
        {
            expected = BencodeDocument.Parse(bytes).Root;
        }
        catch (FormatException)
        {
            expected = null;
        }

        BencodeValue? actual;
        if ((mode & 1) is 0)
        {
            var reader = new ChunkedPipeReader(bytes, sizer);
            try
            {
                actual = BencodeDocument.ParseAsync(reader).AsTask().GetAwaiter().GetResult().Root;
                FuzzAssert.Equal(bytes.Length, reader.Consumed, "The pipe decoder did not consume the whole document");
            }
            catch (FormatException)
            {
                actual = null;
            }

            FuzzAssert.False(reader.IsReading, "The pipe decoder returned without advancing the reader past its last read");
        }
        else
        {
            using var stream = new ShortReadStream(bytes, sizer);
            try
            {
                actual = BencodeDocument.ParseAsync(stream).AsTask().GetAwaiter().GetResult().Root;
            }
            catch (FormatException)
            {
                actual = null;
            }
        }

        FuzzAssert.Equal(expected is not null, actual is not null, "The span decoder and the pipe decoder disagree on whether the document is valid");
        if (expected is not null && actual is not null)
        {
            FuzzAssert.True(AreEqual(expected, actual, ordered: true), "The span decoder and the pipe decoder decode different values");
        }
    }

    /// <summary>
    /// <c>mode, chunks, document</c>: <see cref="BencodeReader"/> reads the document in chunks whose sizes come from
    /// <c>chunks</c>, through a <see cref="ChunkedPipeReader"/> (even <c>mode</c>) or a stream (odd <c>mode</c>). It reads
    /// exactly the documents the reference validator accepts, ignoring duplicate keys that only <see cref="BencodeReader.ReadValueAsync"/>
    /// detects, and returns the tokens of the value the span decoder returns, with their depth and position. The bits of
    /// <c>mode</c> choose the containers it reads with <see cref="BencodeReader.ReadValueAsync"/> or skips with
    /// <see cref="BencodeReader.SkipAsync"/>, which give the value or the end of the container.
    /// </summary>
    private static void Reader(ReadOnlySpan<byte> data)
    {
        var mode = FuzzInput.ReadByte(ref data);
        var sizer = new ChunkSizer(FuzzInput.ReadByte(ref data));
        var bytes = data.ToArray();
        var isValid = IsValidDocument(bytes, allowDuplicateKeys: true);

        // The tokens of the value the span decoder returns, when the document has no duplicate key
        BencodeValue? expectedValue;
        try
        {
            expectedValue = BencodeDocument.Parse(bytes).Root;
        }
        catch (FormatException)
        {
            expectedValue = null;
        }

        List<ExpectedToken>? expected = null;
        if (expectedValue is not null)
        {
            expected = [];
            AddTokens(expectedValue, depth: 0, expected);
        }

        ChunkedPipeReader? pipeReader = (mode & 1) is 0 ? new ChunkedPipeReader(bytes, sizer) : null;
        var reader = pipeReader is not null ? new BencodeReader(pipeReader) : new BencodeReader(new ShortReadStream(bytes, sizer));
        var choices = mode >> 1;
        var tokenIndex = 0;
        var succeeded = false;
        try
        {
            long previousPosition = 0;
            while (reader.ReadAsync().AsTask().GetAwaiter().GetResult())
            {
                FuzzAssert.True(reader.BytesConsumed > previousPosition && reader.BytesConsumed <= bytes.Length, "BytesConsumed does not grow within the document");
                previousPosition = reader.BytesConsumed;
                if (expected is not null)
                {
                    FuzzAssert.True(tokenIndex < expected.Count, "The reader returned more tokens than the value has");
                    CheckToken(reader, expected[tokenIndex]);
                }

                var token = expected?[tokenIndex];
                tokenIndex++;
                if (reader.TokenType is BencodeTokenType.StartList or BencodeTokenType.StartDictionary)
                {
                    var choice = choices & 3;
                    choices = ((choices >> 2) | (choices << 5)) & 0x7F;
                    if (choice is 1)
                    {
                        var value = reader.ReadValueAsync().AsTask().GetAwaiter().GetResult();
                        if (token is not null)
                        {
                            FuzzAssert.True(AreEqual(token.Value!, value, ordered: true), "ReadValueAsync returned a different value");
                        }

                        tokenIndex = token?.EndIndex + 1 ?? tokenIndex;
                        FuzzAssert.True(reader.TokenType is BencodeTokenType.EndList or BencodeTokenType.EndDictionary, "ReadValueAsync did not stop at the end of the container");
                    }
                    else if (choice is 2)
                    {
                        reader.SkipAsync().AsTask().GetAwaiter().GetResult();
                        tokenIndex = token?.EndIndex + 1 ?? tokenIndex;
                        FuzzAssert.True(reader.TokenType is BencodeTokenType.EndList or BencodeTokenType.EndDictionary, "SkipAsync did not stop at the end of the container");
                    }

                    previousPosition = reader.BytesConsumed;
                }
            }

            succeeded = true;
            FuzzAssert.Equal(BencodeTokenType.None, reader.TokenType, "The token after the end is not None");
            FuzzAssert.Equal((long)bytes.Length, reader.BytesConsumed, "The reader did not consume the whole document");
            FuzzAssert.False(reader.ReadAsync().AsTask().GetAwaiter().GetResult(), "The reader read past the end");
        }
        catch (FormatException ex)
        {
            // ReadValueAsync detects duplicate keys
            FuzzAssert.True(!isValid || expectedValue is null, "The reader rejected a valid document: " + ex.Message);
        }
        finally
        {
            reader.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        if (succeeded)
        {
            FuzzAssert.True(isValid, "The reader accepted an invalid document");
            if (expected is not null)
            {
                FuzzAssert.Equal(expected.Count, tokenIndex, "The reader returned fewer tokens than the value has");
            }
        }

        if (pipeReader is not null)
        {
            FuzzAssert.False(pipeReader.IsReading, "The reader was disposed without advancing the pipe past its last read");
        }

        static void CheckToken(BencodeReader reader, ExpectedToken token)
        {
            FuzzAssert.Equal(token.Type, reader.TokenType, "The reader returned another token");
            FuzzAssert.Equal(token.Depth, reader.CurrentDepth, "The reader reported another depth");
            switch (token.Value)
            {
                case BencodeInteger integer:
                    FuzzAssert.Equal(integer.BigIntegerValue, reader.GetBigInteger(), "The reader returned another integer");
                    FuzzAssert.Equal(integer.IsInt64, reader.TryGetInt64(out var value), "TryGetInt64 does not match the size of the integer");
                    if (integer.IsInt64)
                    {
                        FuzzAssert.Equal(integer.Value, value, "TryGetInt64 returned another integer");
                        FuzzAssert.Equal(integer.Value, reader.GetInt64(), "GetInt64 returned another integer");
                    }

                    break;

                case BencodeString text when token.Type is BencodeTokenType.String or BencodeTokenType.Key:
                    FuzzAssert.True(text.Value.Span.SequenceEqual(reader.GetBytes().Span), "The reader returned other bytes");
                    break;
            }
        }
    }

    private static void AddTokens(BencodeValue value, int depth, List<ExpectedToken> tokens)
    {
        switch (value)
        {
            case BencodeInteger:
                tokens.Add(new ExpectedToken(BencodeTokenType.Integer, depth, value));
                break;

            case BencodeString:
                tokens.Add(new ExpectedToken(BencodeTokenType.String, depth, value));
                break;

            case BencodeList list:
                var listToken = new ExpectedToken(BencodeTokenType.StartList, depth, value);
                tokens.Add(listToken);
                foreach (var item in list)
                {
                    AddTokens(item, depth + 1, tokens);
                }

                listToken.EndIndex = tokens.Count;
                tokens.Add(new ExpectedToken(BencodeTokenType.EndList, depth, Value: null));
                break;

            case BencodeDictionary dictionary:
                var dictionaryToken = new ExpectedToken(BencodeTokenType.StartDictionary, depth, value);
                tokens.Add(dictionaryToken);
                foreach (var entry in dictionary)
                {
                    tokens.Add(new ExpectedToken(BencodeTokenType.Key, depth + 1, entry.Key));
                    AddTokens(entry.Value, depth + 1, tokens);
                }

                dictionaryToken.EndIndex = tokens.Count;
                tokens.Add(new ExpectedToken(BencodeTokenType.EndDictionary, depth, Value: null));
                break;
        }
    }

    private sealed record ExpectedToken(BencodeTokenType Type, int Depth, BencodeValue? Value)
    {
        public int EndIndex { get; set; }
    }

    /// <summary>
    /// The input is a sequence of <see cref="BencodeWriter"/> calls (an opcode byte, then its operand). A model of the
    /// encoding says which calls are valid and what they write: a valid call succeeds, an invalid one throws
    /// <see cref="InvalidOperationException"/> and writes nothing, and the output is always the model's. A complete output
    /// decodes, unless it holds a duplicate key or too many nested containers, and writes back to the same bytes.
    /// </summary>
    private static void Writer(ReadOnlySpan<byte> data)
    {
        var output = new ArrayBufferWriter<byte>();
        var writer = new BencodeWriter(output);
        var model = new WriterModel();
        while (!data.IsEmpty)
        {
            var opcode = FuzzInput.ReadByte(ref data) % 12;
            byte[] expectedBytes;
            bool isValid;
            Action call;
            switch (opcode)
            {
                case 0:
                    var integer = ReadInt64(ref data);
                    isValid = model.CanWriteValue;
                    expectedBytes = Encoding.ASCII.GetBytes("i" + integer.ToString(CultureInfo.InvariantCulture) + "e");
                    call = () => writer.WriteInteger(integer);
                    break;

                case 1:
                    var stringBytes = ReadBytes(ref data);
                    isValid = model.CanWriteValue;
                    expectedBytes = EncodeString(stringBytes);
                    call = () => writer.WriteString(stringBytes.AsSpan());
                    break;

                case 2:
                    var memoryBytes = ReadBytes(ref data);
                    isValid = model.CanWriteValue;
                    expectedBytes = EncodeString(memoryBytes);
                    call = () => writer.WriteString(memoryBytes.AsMemory());
                    break;

                case 3:
                    var text = FuzzText.Decode(ReadBytes(ref data));
                    isValid = model.CanWriteValue;
                    expectedBytes = EncodeString(Encoding.UTF8.GetBytes(text));
                    call = () => writer.WriteUtf8String(text);
                    break;

                case 4:
                    isValid = model.CanWriteValue;
                    expectedBytes = "l"u8.ToArray();
                    call = writer.WriteStartList;
                    break;

                case 5:
                    isValid = model.CanEnd(isDictionary: false);
                    expectedBytes = "e"u8.ToArray();
                    call = writer.WriteEndList;
                    break;

                case 6:
                    isValid = model.CanWriteValue;
                    expectedBytes = "d"u8.ToArray();
                    call = writer.WriteStartDictionary;
                    break;

                case 7:
                    var keyBytes = ReadBytes(ref data);
                    isValid = model.CanWriteKey;
                    expectedBytes = EncodeString(keyBytes);
                    call = () => writer.WriteKey(keyBytes);
                    break;

                case 8:
                    var key = FuzzText.Decode(ReadBytes(ref data));
                    isValid = model.CanWriteKey;
                    expectedBytes = EncodeString(Encoding.UTF8.GetBytes(key));
                    call = () => writer.WriteUtf8Key(key);
                    break;

                case 9:
                    isValid = model.CanEnd(isDictionary: true);
                    expectedBytes = "e"u8.ToArray();
                    call = writer.WriteEndDictionary;
                    break;

                case 11:
                    var bigInteger = new BigInteger(ReadBytes(ref data), isUnsigned: false, isBigEndian: false);
                    isValid = model.CanWriteValue;
                    expectedBytes = Encoding.ASCII.GetBytes("i" + bigInteger.ToString(CultureInfo.InvariantCulture) + "e");
                    call = () => writer.WriteInteger(bigInteger);
                    break;

                default:
                    isValid = model.IsComplete;
                    expectedBytes = [];
                    call = writer.Complete;
                    break;
            }

            try
            {
                call();
            }
            catch (InvalidOperationException ex)
            {
                FuzzAssert.False(isValid, $"The writer rejected a valid call (opcode {opcode}): {ex.Message}");
                FuzzAssert.True(output.WrittenSpan.SequenceEqual(model.Output.WrittenSpan), "A rejected call changed the output");
                continue;
            }

            FuzzAssert.True(isValid, $"The writer accepted an invalid call (opcode {opcode})");
            model.Apply(opcode, expectedBytes);
            FuzzAssert.True(output.WrittenSpan.SequenceEqual(model.Output.WrittenSpan), $"The writer wrote unexpected bytes (opcode {opcode})");
        }

        if (!model.IsComplete)
            return;

        var written = output.WrittenSpan.ToArray();
        BencodeValue value;
        try
        {
            value = BencodeDocument.Parse(written).Root;
        }
        catch (FormatException ex)
        {
            FuzzAssert.True(model.HasDuplicateKey || model.MaxDepth > MaxDepth, "The output of the writer does not decode: " + ex.Message);
            return;
        }

        FuzzAssert.False(model.HasDuplicateKey || model.MaxDepth > MaxDepth, "A document with a duplicate key or too many nested containers was decoded");
        FuzzAssert.True(value.ToUtf8ByteArray(canonical: false).AsSpan().SequenceEqual(written), "The output of the writer does not write back to the same bytes");
    }

    /// <summary>
    /// <c>chunks, document</c>: <see cref="TorrentFile.Parse"/>, <see cref="TorrentFile.TryParse"/>, and
    /// <see cref="TorrentFile.ParseAsync(Stream, CancellationToken)"/> or <see cref="TorrentFile.ParseAsync(System.IO.Pipelines.PipeReader, CancellationToken)"/>
    /// (reading in chunks, the high bit of <c>chunks</c> choosing the pipe) agree. A parsed torrent satisfies the rules of its model, its
    /// info-hashes are the hashes of the exact bytes of its 'info' dictionary, and writing it gives a torrent with the same
    /// model whose encoding is a fixed point and whose info-hash is the one of the model.
    /// </summary>
    [SuppressMessage("Security", "CA5350:Do Not Use Weak Cryptographic Algorithms", Justification = "BitTorrent v1 info-hash is SHA-1.")]
    private static void Torrent(ReadOnlySpan<byte> data)
    {
        var sizerSeed = FuzzInput.ReadByte(ref data);
        var sizer = new ChunkSizer(sizerSeed);
        var bytes = data.ToArray();

        TorrentFile? torrent;
        try
        {
            torrent = TorrentFile.Parse(bytes);
        }
        catch (FormatException)
        {
            torrent = null;
        }

        var tryParsed = TorrentFile.TryParse(bytes, out var tryResult);
        FuzzAssert.Equal(torrent is not null, tryParsed, "Parse and TryParse disagree");

        TorrentFile? asyncResult;
        ChunkedPipeReader? pipeReader = null;
        try
        {
            if ((sizerSeed & 0x80) is not 0)
            {
                pipeReader = new ChunkedPipeReader(bytes, sizer);
                asyncResult = TorrentFile.ParseAsync(pipeReader).AsTask().GetAwaiter().GetResult();
                FuzzAssert.Equal(bytes.Length, pipeReader.Consumed, "ParseAsync did not consume the whole torrent");
            }
            else
            {
                using var stream = new ShortReadStream(bytes, sizer);
                asyncResult = TorrentFile.ParseAsync(stream).AsTask().GetAwaiter().GetResult();
            }
        }
        catch (FormatException)
        {
            asyncResult = null;
        }

        if (pipeReader is not null)
        {
            FuzzAssert.False(pipeReader.IsReading, "ParseAsync returned without advancing the reader past its last read");
        }

        FuzzAssert.Equal(torrent is not null, asyncResult is not null, "Parse and ParseAsync disagree");
        if (torrent is null || tryResult is null || asyncResult is null)
            return;

        CheckSameTorrent(torrent, tryResult, "TryParse");
        CheckSameTorrent(torrent, asyncResult, "ParseAsync");
        CheckTorrentModel(torrent);

        // The decoder writes back the bytes it read, so the encoding of the 'info' dictionary in insertion order is the slice of the input
        var info = ((BencodeDictionary)BencodeDocument.Parse(bytes).Root)[InfoKey];
        var infoBytes = info.ToUtf8ByteArray(canonical: false);
        FuzzAssert.True(SHA1.HashData(infoBytes).AsSpan().SequenceEqual(torrent.GetInfoHashSha1()), "The SHA-1 info-hash is not the hash of the 'info' dictionary");
        FuzzAssert.True(SHA256.HashData(infoBytes).AsSpan().SequenceEqual(torrent.GetInfoHashSha256()), "The SHA-256 info-hash is not the hash of the 'info' dictionary");
        FuzzAssert.True(asyncResult.GetInfoHashSha1().AsSpan().SequenceEqual(torrent.GetInfoHashSha1()), "Parse and ParseAsync compute different info-hashes");

        byte[] written;
        try
        {
            written = torrent.ToUtf8ByteArray();
        }
        catch (FormatException ex)
        {
            // The parser accepts an empty URL or host that the writer rejects, and text that decodes to characters a code
            // page cannot encode back, or to paths of the file tree that are no longer distinct
            FuzzAssert.True(HasEmptyListEntry(torrent) || ResolveEncoding(torrent.Encoding) is not null, "A parsed torrent cannot be written: " + ex.Message);
            return;
        }

        var reparsed = TorrentFile.Parse(written);
        CheckSameTorrent(torrent, reparsed, "The written torrent");
        FuzzAssert.True(reparsed.ToUtf8ByteArray().AsSpan().SequenceEqual(written), "Writing a torrent is not a fixed point");
        CheckSameTorrent(torrent, TorrentFile.Parse(torrent.ToUtf8ByteArray(canonical: false)), "The torrent written in insertion order");

        // Replacing the info hashes the model, which is what the written torrent holds
        torrent.Info = torrent.Info;
        FuzzAssert.True(torrent.GetInfoHashSha1().AsSpan().SequenceEqual(reparsed.GetInfoHashSha1()), "The info-hash of the model is not the one of the written torrent");
    }

    private static void CheckValue(BencodeValue value, int depth)
    {
        switch (value)
        {
            case BencodeInteger integer:
                FuzzAssert.Equal(BencodeValueKind.Integer, integer.Kind, "Wrong kind");
                var big = integer.BigIntegerValue;
                FuzzAssert.Equal(big >= long.MinValue && big <= long.MaxValue, integer.IsInt64, "IsInt64 does not match the value");
                FuzzAssert.Equal(integer.IsInt64, integer.TryGetInt64(out var int64), "TryGetInt64 does not match IsInt64");
                if (integer.IsInt64)
                {
                    FuzzAssert.Equal(big, (BigInteger)int64, "TryGetInt64 returned another value");
                    FuzzAssert.Equal(int64, integer.Value, "Value is not the value of TryGetInt64");
                }

                var integerCopy = new BencodeInteger(big);
                FuzzAssert.True(integerCopy.Equals(integer) && integerCopy.GetHashCode() == integer.GetHashCode(), "Equal integers are not equal or have different hashes");
                FuzzAssert.Equal(big.ToString(CultureInfo.InvariantCulture), integer.ToString(), "ToString does not format the value");
                break;

            case BencodeString text:
                FuzzAssert.Equal(BencodeValueKind.String, text.Kind, "Wrong kind");
                _ = text.ToString();
                if (Utf8.IsValid(text.Value.Span))
                {
                    FuzzAssert.True(Encoding.UTF8.GetBytes(text.ToUtf8String()).AsSpan().SequenceEqual(text.Value.Span), "ToUtf8String does not decode a valid UTF-8 string");
                }
                else
                {
                    try
                    {
                        _ = text.ToUtf8String();
                        FuzzAssert.Fail("ToUtf8String decoded invalid UTF-8");
                    }
                    catch (DecoderFallbackException)
                    {
                    }
                }

                break;

            case BencodeList list:
                FuzzAssert.Equal(BencodeValueKind.List, list.Kind, "Wrong kind");
                FuzzAssert.True(depth < MaxDepth, "A container is nested too deeply");
                var index = 0;
                foreach (var item in list)
                {
                    FuzzAssert.True(ReferenceEquals(item, list[index]), "The indexer and the enumerator of a list disagree");
                    CheckValue(item, depth + 1);
                    index++;
                }

                FuzzAssert.Equal(index, list.Count, "The count of a list is not its number of items");
                break;

            case BencodeDictionary dictionary:
                FuzzAssert.Equal(BencodeValueKind.Dictionary, dictionary.Kind, "Wrong kind");
                FuzzAssert.True(depth < MaxDepth, "A container is nested too deeply");
                var count = 0;
                using (var keys = dictionary.Keys.GetEnumerator())
                {
                    foreach (var entry in dictionary)
                    {
                        FuzzAssert.True(keys.MoveNext() && ReferenceEquals(keys.Current, entry.Key), "Keys and the enumerator of a dictionary disagree");
                        var copy = new BencodeString(entry.Key.Value.ToArray());
                        FuzzAssert.True(copy.Equals(entry.Key) && copy.GetHashCode() == entry.Key.GetHashCode(), "Equal keys are not equal or have different hashes");
                        FuzzAssert.True(dictionary.ContainsKey(copy), "ContainsKey does not find a key");
                        FuzzAssert.True(dictionary.TryGetValue(copy, out var found) && ReferenceEquals(found, entry.Value), "TryGetValue does not find the value of a key");
                        FuzzAssert.True(ReferenceEquals(dictionary[copy], entry.Value), "The indexer does not find the value of a key");
                        CheckValue(entry.Value, depth + 1);
                        count++;
                    }

                    FuzzAssert.False(keys.MoveNext(), "Keys has more items than the dictionary");
                }

                FuzzAssert.Equal(count, dictionary.Count, "The count of a dictionary is not its number of entries");
                break;

            default:
                FuzzAssert.Fail("Unknown value type " + value.GetType());
                break;
        }
    }

    private static void CheckCanonicalOrder(BencodeValue value)
    {
        switch (value)
        {
            case BencodeList list:
                foreach (var item in list)
                {
                    CheckCanonicalOrder(item);
                }

                break;

            case BencodeDictionary dictionary:
                BencodeString? previous = null;
                foreach (var entry in dictionary)
                {
                    FuzzAssert.True(previous is null || previous.Value.Span.SequenceCompareTo(entry.Key.Value.Span) < 0, "The canonical encoding does not sort the keys");
                    previous = entry.Key;
                    CheckCanonicalOrder(entry.Value);
                }

                break;
        }
    }

    private static bool AreEqual(BencodeValue left, BencodeValue right, bool ordered)
    {
        switch (left, right)
        {
            case (BencodeInteger a, BencodeInteger b):
                return a.BigIntegerValue == b.BigIntegerValue;

            case (BencodeString a, BencodeString b):
                return a.Value.Span.SequenceEqual(b.Value.Span);

            case (BencodeList a, BencodeList b):
                if (a.Count != b.Count)
                    return false;

                for (var i = 0; i < a.Count; i++)
                {
                    if (!AreEqual(a[i], b[i], ordered))
                        return false;
                }

                return true;

            case (BencodeDictionary a, BencodeDictionary b):
                if (a.Count != b.Count)
                    return false;

                if (ordered)
                {
                    return a.Zip(b).All(pair => pair.First.Key.Equals(pair.Second.Key) && AreEqual(pair.First.Value, pair.Second.Value, ordered));
                }

                return a.All(entry => b.TryGetValue(entry.Key, out var other) && AreEqual(entry.Value, other, ordered));

            default:
                return false;
        }
    }

    private static void CheckTorrentModel(TorrentFile torrent)
    {
        var info = torrent.Info;
        CheckPathSegment(info.Name);
        FuzzAssert.True(info.PieceLength > 0, "The piece length is not positive");
        FuzzAssert.Equal(info.FileTree is not null, info.MetaVersion is 2, "A torrent has a file tree exactly when its meta version is 2");
        var isV2Only = info.FileTree is not null && info.Pieces.IsEmpty && info.Length is null && info.Files is null;
        if (!isV2Only)
        {
            FuzzAssert.True(!info.Pieces.IsEmpty && info.Pieces.Length % 20 is 0, "The piece hashes are not a non-empty multiple of 20 bytes");
            FuzzAssert.True(info.Length.HasValue != info.Files is not null, "A torrent must have either a length or files");
        }

        FuzzAssert.False(info.Length < 0, "The length is negative");
        if (info.FileTree is not null)
        {
            FuzzAssert.True(info.PieceLength >= 16384 && BitOperations.IsPow2(info.PieceLength), "The piece length of a v2 torrent is not a power of two of at least 16 KiB");
            FuzzAssert.True(info.FileTree.Count > 0, "A file tree has no file");
            var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in info.FileTree)
            {
                FuzzAssert.True(entry.Length >= 0, "A file tree length is negative");
                FuzzAssert.Equal(entry.Length > 0 ? 32 : 0, entry.PiecesRoot.Length, "A pieces root is not 32 bytes for a non-empty file and absent otherwise");
                FuzzAssert.True(entry.Path.Count > 0, "A file tree entry has no path");
                foreach (var segment in entry.Path)
                {
                    CheckPathSegment(segment);
                }

                // The paths are distinct, and a file is not the directory of another file
                for (var i = 1; i <= entry.Path.Count; i++)
                {
                    var prefix = string.Join('/', entry.Path.Take(i));
                    var isFile = i == entry.Path.Count;
                    var isValid = isFile ? !paths.Contains("d:" + prefix) && paths.Add("f:" + prefix) : !paths.Contains("f:" + prefix) && (paths.Add("d:" + prefix) || true);
                    FuzzAssert.True(isValid, "Two file tree entries conflict: " + FuzzAssert.Format(prefix));
                }
            }
        }

        foreach (var layer in torrent.PieceLayers ?? [])
        {
            FuzzAssert.True(layer.PiecesRoot.Length is 32 && !layer.Hashes.IsEmpty && layer.Hashes.Length % 32 is 0, "A piece layer has a pieces root that is not 32 bytes or hashes that are not a non-empty multiple of 32 bytes");
        }
        if (info.Files is not null)
        {
            FuzzAssert.True(info.Files.Count > 0, "A multi-file torrent has no file");
            foreach (var file in info.Files)
            {
                FuzzAssert.True(file.Length >= 0, "A file length is negative");
                FuzzAssert.True(file.Path.Count > 0, "A file has no path");
                foreach (var segment in file.Path)
                {
                    CheckPathSegment(segment);
                }
            }
        }

        foreach (var node in torrent.Nodes ?? [])
        {
            FuzzAssert.True(node.Port is >= 0 and <= 65535, "A node port is out of range");
        }

        static void CheckPathSegment(string segment)
        {
            FuzzAssert.True(segment is { Length: > 0 } and not "." and not ".." && !segment.AsSpan().ContainsAny('/', '\\'), "An unsafe path segment was accepted: " + FuzzAssert.Format(segment));
        }
    }

    /// <summary>The encoding the library uses for the text fields, from the same rules: <see langword="null"/> for UTF-8 or an unknown name.</summary>
    private static Encoding? ResolveEncoding(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        try
        {
            var encoding = CodePagesEncodingProvider.Instance.GetEncoding(name.Trim()) ?? Encoding.GetEncoding(name.Trim());
            return encoding.CodePage is 65001 ? null : encoding;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static bool HasEmptyListEntry(TorrentFile torrent)
    {
        return (torrent.AnnounceList?.Any(tier => tier.Any(string.IsNullOrEmpty)) ?? false)
            || (torrent.UrlList?.Any(string.IsNullOrEmpty) ?? false)
            || (torrent.HttpSeeds?.Any(string.IsNullOrEmpty) ?? false)
            || (torrent.Nodes?.Any(node => string.IsNullOrEmpty(node.Host)) ?? false);
    }

    private static void CheckSameTorrent(TorrentFile expected, TorrentFile actual, string source)
    {
        var message = source + " gives a different torrent: ";
        FuzzAssert.Equal(expected.Announce, actual.Announce, message + "announce");
        FuzzAssert.True(SameLists(expected.AnnounceList, actual.AnnounceList, (a, b) => a.SequenceEqual(b, StringComparer.Ordinal)), message + "announce-list");
        FuzzAssert.Equal(expected.Comment, actual.Comment, message + "comment");
        FuzzAssert.Equal(expected.CreatedBy, actual.CreatedBy, message + "created by");
        FuzzAssert.Equal(expected.CreationDate, actual.CreationDate, message + "creation date");
        FuzzAssert.True(SameLists(expected.UrlList, actual.UrlList, string.Equals), message + "url-list");
        FuzzAssert.True(SameLists(expected.HttpSeeds, actual.HttpSeeds, string.Equals), message + "httpseeds");
        FuzzAssert.True(SameLists(expected.Nodes, actual.Nodes, (a, b) => a.Host == b.Host && a.Port == b.Port), message + "nodes");
        FuzzAssert.Equal(expected.Encoding, actual.Encoding, message + "encoding");
        FuzzAssert.Equal(expected.Publisher, actual.Publisher, message + "publisher");
        FuzzAssert.Equal(expected.PublisherUrl, actual.PublisherUrl, message + "publisher-url");

        var expectedInfo = expected.Info;
        var actualInfo = actual.Info;
        FuzzAssert.Equal(expectedInfo.Name, actualInfo.Name, message + "name");
        FuzzAssert.Equal(expectedInfo.PieceLength, actualInfo.PieceLength, message + "piece length");
        FuzzAssert.True(expectedInfo.Pieces.Span.SequenceEqual(actualInfo.Pieces.Span), message + "pieces");
        FuzzAssert.Equal(expectedInfo.IsPrivate, actualInfo.IsPrivate, message + "private");
        FuzzAssert.Equal(expectedInfo.Length, actualInfo.Length, message + "length");
        FuzzAssert.Equal(expectedInfo.Source, actualInfo.Source, message + "source");
        FuzzAssert.Equal(expectedInfo.Md5Sum, actualInfo.Md5Sum, message + "md5sum");
        FuzzAssert.True(SameLists(expectedInfo.Files, actualInfo.Files, (a, b) => a.Length == b.Length && a.Md5Sum == b.Md5Sum && a.Path.SequenceEqual(b.Path, StringComparer.Ordinal) && SameKeys(a.AdditionalKeys, b.AdditionalKeys)), message + "files");
        FuzzAssert.Equal(expectedInfo.MetaVersion, actualInfo.MetaVersion, message + "meta version");
        FuzzAssert.True(SameLists(expectedInfo.FileTree?.OrderBy(entry => string.Join('/', entry.Path), StringComparer.Ordinal).ToList(), actualInfo.FileTree?.OrderBy(entry => string.Join('/', entry.Path), StringComparer.Ordinal).ToList(), (a, b) => a.Length == b.Length && a.PiecesRoot.Span.SequenceEqual(b.PiecesRoot.Span) && a.Path.SequenceEqual(b.Path, StringComparer.Ordinal) && SameKeys(a.AdditionalKeys, b.AdditionalKeys)), message + "file tree");
        FuzzAssert.True(SameLists(expected.PieceLayers?.OrderBy(layer => Convert.ToHexString(layer.PiecesRoot.Span), StringComparer.Ordinal).ToList(), actual.PieceLayers?.OrderBy(layer => Convert.ToHexString(layer.PiecesRoot.Span), StringComparer.Ordinal).ToList(), (a, b) => a.PiecesRoot.Span.SequenceEqual(b.PiecesRoot.Span) && a.Hashes.Span.SequenceEqual(b.Hashes.Span)), message + "piece layers");
        FuzzAssert.True(SameKeys(expected.AdditionalKeys, actual.AdditionalKeys), message + "additional keys");
        FuzzAssert.True(SameKeys(expectedInfo.AdditionalKeys, actualInfo.AdditionalKeys), message + "additional info keys");

        static bool SameKeys(IDictionary<BencodeString, BencodeValue> a, IDictionary<BencodeString, BencodeValue> b)
        {
            return a.Count == b.Count && a.All(entry => b.TryGetValue(entry.Key, out var other) && AreEqual(entry.Value, other, ordered: false));
        }

        static bool SameLists<T>(IReadOnlyList<T>? a, IReadOnlyList<T>? b, Func<T, T, bool> equals)
        {
            if (a is null || b is null)
                return a is null && b is null;

            return a.Count == b.Count && a.Zip(b).All(pair => equals(pair.First, pair.Second));
        }
    }

    private static bool IsValidDocument(ReadOnlySpan<byte> data, bool allowDuplicateKeys = false)
    {
        var index = 0;
        return IsValidValue(data, ref index, depth: 0, allowDuplicateKeys) && index == data.Length;
    }

    private static bool IsValidValue(ReadOnlySpan<byte> data, ref int index, int depth, bool allowDuplicateKeys)
    {
        if (index >= data.Length)
            return false;

        switch (data[index])
        {
            case (byte)'i':
                index++;
                var length = data[index..].IndexOf((byte)'e');
                if (length < 0)
                    return false;

                var digits = data.Slice(index, length);
                index += length + 1;
                var isNegative = !digits.IsEmpty && digits[0] is (byte)'-';
                var magnitude = isNegative ? digits[1..] : digits;
                return magnitude.Length <= MaxIntegerDigits
                    && IsCanonicalNumber(magnitude, max: [])
                    && !(isNegative && digits is [_, (byte)'0']);

            case >= (byte)'0' and <= (byte)'9':
                return TryReadString(data, ref index, out _);

            case (byte)'l':
                if (depth >= MaxDepth)
                    return false;

                index++;
                while (index < data.Length)
                {
                    if (data[index] is (byte)'e')
                    {
                        index++;
                        return true;
                    }

                    if (!IsValidValue(data, ref index, depth + 1, allowDuplicateKeys))
                        return false;
                }

                return false;

            case (byte)'d':
                if (depth >= MaxDepth)
                    return false;

                index++;
                var keys = new HashSet<string>(StringComparer.Ordinal);
                while (index < data.Length)
                {
                    if (data[index] is (byte)'e')
                    {
                        index++;
                        return true;
                    }

                    if (!TryReadString(data, ref index, out var key) || !(keys.Add(Convert.ToHexString(key)) || allowDuplicateKeys) || !IsValidValue(data, ref index, depth + 1, allowDuplicateKeys))
                        return false;
                }

                return false;

            default:
                return false;
        }
    }

    private static bool TryReadString(ReadOnlySpan<byte> data, ref int index, out ReadOnlySpan<byte> value)
    {
        value = default;
        var colon = data[index..].IndexOf((byte)':');
        if (colon < 0 || !IsCanonicalNumber(data.Slice(index, colon), "2147483647"u8))
            return false;

        var length = int.Parse(data.Slice(index, colon), CultureInfo.InvariantCulture);
        index += colon + 1;
        if (data.Length - index < length)
            return false;

        value = data.Slice(index, length);
        index += length;
        return true;
    }

    /// <summary>Gets whether the digits are <c>0</c> or a number without a leading zero that is at most <paramref name="max"/> (no bound when empty).</summary>
    private static bool IsCanonicalNumber(ReadOnlySpan<byte> digits, ReadOnlySpan<byte> max)
    {
        if (digits.IsEmpty || digits.ContainsAnyExceptInRange((byte)'0', (byte)'9') || (digits.Length > 1 && digits[0] is (byte)'0'))
            return false;

        if (max.IsEmpty)
            return true;

        // Numbers of the same length compare as their digits do
        return digits.Length < max.Length || (digits.Length == max.Length && digits.SequenceCompareTo(max) <= 0);
    }

    private static long ReadInt64(ref ReadOnlySpan<byte> data)
    {
        Span<byte> buffer = stackalloc byte[8];
        buffer.Clear();
        var length = Math.Min(8, data.Length);
        data[..length].CopyTo(buffer);
        data = data[length..];
        return BinaryPrimitives.ReadInt64LittleEndian(buffer);
    }

    private static byte[] ReadBytes(ref ReadOnlySpan<byte> data)
    {
        var length = Math.Min(FuzzInput.ReadByte(ref data) % 32, data.Length);
        var result = data[..length].ToArray();
        data = data[length..];
        return result;
    }

    private static byte[] EncodeString(ReadOnlySpan<byte> value) => [.. Encoding.ASCII.GetBytes(value.Length.ToString(CultureInfo.InvariantCulture) + ":"), .. value];

    private static IEnumerable<FuzzSeed> GetParseSeeds() => GetDocuments().Select(seed => new FuzzSeed(seed.Name, seed.Data));

    private static IEnumerable<FuzzSeed> GetPipeReaderSeeds()
    {
        var index = 0;
        foreach (var (name, document) in GetDocuments())
        {
            // Alternate the pipe and the stream, whole documents and small chunks
            var mode = (byte)(index & 1);
            var chunks = (byte)((index >> 1) % 3 switch { 0 => 0, 1 => 0x10, _ => 0x37 });
            yield return new FuzzSeed(name, (byte[])[mode, chunks, .. document]);
            index++;
        }
    }

    private static IEnumerable<FuzzSeed> GetTorrentSeeds()
    {
        var index = 0;
        foreach (var (name, document) in GetDocuments())
        {
            yield return new FuzzSeed(name, (byte[])[(byte)(index++ % 3 switch { 0 => 0, 1 => 0x23, _ => 0x85 }), .. document]);
        }
    }

    private static IEnumerable<FuzzSeed> GetWriterSeeds()
    {
        // d 3:cow 3:moo 4:spam l i42e 3:abc e e, then Complete
        yield return new FuzzSeed("dictionary", (byte[])[6, 7, 3, .. "cow"u8, 1, 3, .. "moo"u8, 8, 4, .. "spam"u8, 4, 0, 42, 0, 0, 0, 0, 0, 0, 0, 3, 3, .. "abc"u8, 5, 9, 10]);
        yield return new FuzzSeed("integer", (byte[])[0, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x7F, 10]);
        yield return new FuzzSeed("string", (byte[])[2, 5, .. "hello"u8, 10]);
        yield return new FuzzSeed("big-integer", (byte[])[4, 11, 12, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x7F, 11, 1, 0x80, 5, 10]);

        // Out-of-order calls: a value where a key is expected, a key in a list, mismatched ends, a second root
        yield return new FuzzSeed("invalid-calls", (byte[])[6, 0, 1, 0, 0, 0, 0, 0, 0, 0, 5, 7, 1, (byte)'a', 5, 9, 9, 4, 10, 7, 1, (byte)'b', 5, 0, 2, 0, 0, 0, 0, 0, 0, 0]);
        yield return new FuzzSeed("duplicate-key", (byte[])[6, 7, 1, (byte)'a', 0, 1, 0, 0, 0, 0, 0, 0, 0, 7, 1, (byte)'a', 0, 2, 0, 0, 0, 0, 0, 0, 0, 9]);
        yield return new FuzzSeed("deep", (byte[])[.. Enumerable.Repeat((byte)4, 70), .. Enumerable.Repeat((byte)5, 70)]);
        yield return new FuzzSeed("utf8", (byte[])[3, 7, .. FuzzText.Encode("café\ud800")]);
    }

    private static IEnumerable<(string Name, byte[] Data)> GetDocuments()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in (string[])["BencodeDocumentTests.cs", "TorrentFileTests.cs"])
        {
            var source = FuzzResources.ReadText(typeof(BencodeFuzzTargets).Assembly, file);
            var index = 0;
            foreach (Match match in StringLiteralRegex().Matches(source))
            {
                var text = Regex.Unescape(match.Groups["value"].Value);
                if (text.Length > 0 && seen.Add(text))
                {
                    yield return (string.Create(CultureInfo.InvariantCulture, $"{Path.GetFileNameWithoutExtension(file)}-{index++}"), Encoding.UTF8.GetBytes(text));
                }
            }
        }

        yield return ("empty-containers", "lldeede"u8.ToArray());
        yield return ("integers", "li0ei-1ei9223372036854775807ei-9223372036854775808ee"u8.ToArray());
        yield return ("binary-string", [.. "3:"u8, 0x00, 0xFF, 0xC3]);
        yield return ("unsorted-keys", "d1:bi1e1:ai2e1:cle0:dee"u8.ToArray());
        yield return ("max-depth", Encoding.ASCII.GetBytes(new string('l', MaxDepth) + new string('e', MaxDepth)));
        yield return ("too-deep", Encoding.ASCII.GetBytes(new string('l', MaxDepth + 1) + new string('e', MaxDepth + 1)));
        yield return ("multi-file-torrent", "d8:announce14:https://t.test13:announce-listll14:https://t.testel15:https://t2.testee7:comment5:hello10:created by4:fuzz13:creation datei1700000000e8:encoding5:UTF-84:infod5:filesld6:lengthi1e4:pathl3:dir5:a.txteed6:lengthi0e6:md5sum32:000000000000000000000000000000004:pathl5:b.bineee4:name4:root12:piece lengthi16384e6:pieces40:01234567890123456789012345678901234567897:privatei1e6:source3:srce5:nodesll9:127.0.0.1i6881eee9:publisher3:pub13:publisher-url12:https://p.io9:httpseedsl12:https://h.ioe8:url-list12:https://w.ioe"u8.ToArray());
        yield return ("url-list-string", "d4:infod6:lengthi5e4:name1:a12:piece lengthi1e6:pieces20:01234567890123456789e8:url-list12:https://w.ioe"u8.ToArray());
        yield return ("empty-url-list", "d4:infod6:lengthi5e4:name1:a12:piece lengthi1e6:pieces20:01234567890123456789e8:url-list0:e"u8.ToArray());
        yield return ("ubuntu-torrent", GetSmallUbuntuTorrent());
        yield return ("hybrid-torrent", ReadResource("hybrid.torrent"));
        yield return ("v2-torrent", ReadResource("v2.torrent"));
        var gbk = CodePagesEncodingProvider.Instance.GetEncoding("GBK")!;
        yield return ("gbk-torrent", [.. "d7:comment4:"u8, .. gbk.GetBytes("中文"), .. "8:encoding3:GBK4:infod6:lengthi1e4:name8:"u8, .. gbk.GetBytes("文件.txt"), .. "12:piece lengthi16384e6:pieces20:01234567890123456789ee"u8]);
        yield return ("big-integers", Encoding.ASCII.GetBytes("li99999999999999999999ei-99999999999999999999ei" + new string('9', MaxIntegerDigits) + "ee"));
        yield return ("too-many-digits", Encoding.ASCII.GetBytes("i" + new string('9', MaxIntegerDigits + 1) + "e"));
    }

    private static byte[] ReadResource(string name)
    {
        using var stream = FuzzResources.Open(typeof(BencodeFuzzTargets).Assembly, name);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>The sample torrent of the unit tests, with its first two piece hashes only so that the seed stays small.</summary>
    private static byte[] GetSmallUbuntuTorrent()
    {
        using var stream = FuzzResources.Open(typeof(BencodeFuzzTargets).Assembly, "ubuntu.torrent");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var root = (BencodeDictionary)BencodeDocument.Parse(buffer.ToArray()).Root;
        var piecesKey = new BencodeString("pieces"u8.ToArray());
        var info = new BencodeDictionary(((BencodeDictionary)root[InfoKey]).Select(entry => entry.Key.Equals(piecesKey)
            ? new KeyValuePair<BencodeString, BencodeValue>(entry.Key, new BencodeString(((BencodeString)entry.Value).Value[..40]))
            : entry));
        return new BencodeDictionary(root.Select(entry => entry.Key.Equals(InfoKey) ? new KeyValuePair<BencodeString, BencodeValue>(entry.Key, info) : entry)).ToUtf8ByteArray(canonical: false);
    }

    [GeneratedRegex("""(?<![@$"\\])"(?<value>(?:[^"\\\r\n]|\\.)*)"(?!")""", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 10_000)]
    private static partial Regex StringLiteralRegex();

    /// <summary>What a sequence of valid <see cref="BencodeWriter"/> calls writes, and which call is valid next.</summary>
    private sealed class WriterModel
    {
        private readonly List<Container> _containers = [];
        private bool _hasRoot;

        public ArrayBufferWriter<byte> Output { get; } = new();

        public bool IsComplete { get; private set; }

        public bool HasDuplicateKey { get; private set; }

        public int MaxDepth { get; private set; }

        public bool CanWriteValue => !IsComplete && (_containers.Count is 0 ? !_hasRoot : !(_containers[^1].IsDictionary && _containers[^1].ExpectsKey));

        public bool CanWriteKey => _containers.Count > 0 && _containers[^1].IsDictionary && _containers[^1].ExpectsKey;

        public bool CanEnd(bool isDictionary) => _containers.Count > 0 && _containers[^1].IsDictionary == isDictionary && (!isDictionary || _containers[^1].ExpectsKey);

        public void Apply(int opcode, byte[] bytes)
        {
            Output.Write(bytes);
            switch (opcode)
            {
                case 0 or 1 or 2 or 3 or 11:
                    StartValue();
                    if (_containers.Count is 0)
                    {
                        IsComplete = true;
                    }

                    break;

                case 4 or 6:
                    StartValue();
                    _containers.Add(new Container { IsDictionary = opcode is 6, ExpectsKey = true });
                    MaxDepth = Math.Max(MaxDepth, _containers.Count);
                    break;

                case 5 or 9:
                    _containers.RemoveAt(_containers.Count - 1);
                    IsComplete = _containers.Count is 0;
                    break;

                case 7 or 8:
                    _containers[^1].ExpectsKey = false;
                    if (!_containers[^1].Keys.Add(Convert.ToHexString(bytes.AsSpan(bytes.AsSpan().IndexOf((byte)':') + 1))))
                    {
                        HasDuplicateKey = true;
                    }

                    break;
            }
        }

        private void StartValue()
        {
            _hasRoot = true;
            if (_containers.Count > 0 && _containers[^1].IsDictionary)
            {
                _containers[^1].ExpectsKey = true;
            }
        }

        private sealed class Container
        {
            public bool IsDictionary { get; init; }

            public bool ExpectsKey { get; set; }

            public HashSet<string> Keys { get; } = new(StringComparer.Ordinal);
        }
    }
}
