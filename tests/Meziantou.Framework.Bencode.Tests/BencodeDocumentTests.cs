using System.Buffers;
using System.IO.Pipelines;
using System.Numerics;

namespace Meziantou.Framework.Bencode.Tests;

public sealed class BencodeDocumentTests
{
    [Fact]
    public async Task ReadmeExample_ParseAndWrite()
    {
        #region BencodeDocument_ParseAndWrite
        var data = "d3:cow3:moo4:spam4:eggse"u8.ToArray();
        var document = BencodeDocument.Parse(data);

        // Parse from a stream, such as File.OpenRead("input.bencode")
        await using var input = new MemoryStream(data);
        var pipeReader = PipeReader.Create(input);
        var streamedDocument = await BencodeDocument.ParseAsync(pipeReader);
        await pipeReader.CompleteAsync();
        Assert.IsType<BencodeDictionary>(streamedDocument.Root);

        var dictionary = (BencodeDictionary)document.Root;
        var cow = (BencodeString)dictionary[new BencodeString("cow"u8.ToArray())];
        Assert.Equal("moo", cow.ToUtf8String());

        var encoded = dictionary.ToUtf8ByteArray();
        Assert.Equal("d3:cow3:moo4:spam4:eggse"u8.ToArray(), encoded);

        // Write to a stream, such as File.Create("output.bencode")
        await using var stream = new MemoryStream();
        await dictionary.WriteToAsync(stream);
        #endregion

        Assert.Equal(data, stream.ToArray());
    }

    [Fact]
    public void ReadmeExample_Writer()
    {
        #region BencodeWriter_Write
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new BencodeWriter(buffer);

        writer.WriteStartDictionary();
        writer.WriteUtf8Key("cow");
        writer.WriteUtf8String("moo");
        writer.WriteEndDictionary();
        writer.Complete();

        var data = buffer.WrittenSpan.ToArray();
        Assert.Equal("d3:cow3:mooe"u8.ToArray(), data);
        #endregion
    }

    [Fact]
    public void Documentation_ReadValues()
    {
        #region BencodeDocument_ReadValues
        var document = BencodeDocument.Parse("d4:name5:Alice3:agei30e4:tagsl3:dev3:opsee"u8);
        var root = (BencodeDictionary)document.Root;

        var name = (BencodeString)root[new BencodeString("name"u8.ToArray())];
        Assert.Equal("Alice", name.ToUtf8String());

        Assert.True(root.TryGetValue(new BencodeString("age"u8.ToArray()), out var age));
        Assert.Equal(30, ((BencodeInteger)age).Value);

        var tags = (BencodeList)root[new BencodeString("tags"u8.ToArray())];
        Assert.Equal(["dev", "ops"], tags.Cast<BencodeString>().Select(tag => tag.ToUtf8String()));

        // The entries keep the order of the data
        Assert.Equal(["string Alice", "integer 30", "list of 2"], root.Values.Select(Describe));

        static string Describe(BencodeValue value) => value switch
        {
            BencodeInteger integer => $"integer {integer.Value}",
            BencodeString text => $"string {text}",
            BencodeList list => $"list of {list.Count}",
            BencodeDictionary dictionary => $"dictionary of {dictionary.Count}",
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };
        #endregion
    }

    [Fact]
    public void Documentation_BuildValues()
    {
        #region BencodeValue_Build
        var dictionary = new BencodeDictionary
        {
            { new BencodeString("zebra"u8.ToArray()), new BencodeInteger(1) },
            { new BencodeString("apple"u8.ToArray()), new BencodeList([new BencodeString("x"u8.ToArray()), new BencodeInteger(-2)]) },
        };

        // By default, the keys are sorted by their bytes (canonical bencode)
        Assert.Equal("d5:applel1:xi-2ee5:zebrai1ee"u8.ToArray(), dictionary.ToUtf8ByteArray());

        // canonical: false keeps the insertion order
        Assert.Equal("d5:zebrai1e5:applel1:xi-2eee"u8.ToArray(), dictionary.ToUtf8ByteArray(canonical: false));
        #endregion
    }

    [Fact]
    public void Documentation_BinaryStrings()
    {
        #region BencodeString_BinaryData
        var value = new BencodeString(new byte[] { 0xFF, 0x00, 0x10 });

        // The bytes are always available
        Assert.Equal([0xFF, 0x00, 0x10], value.Value.ToArray());

        // ToString is for diagnostics: it shows bytes that are not UTF-8 as hexadecimal
        Assert.Equal("0xFF0010", value.ToString());

        // ToUtf8String throws when the bytes are not valid UTF-8
        Assert.Throws<DecoderFallbackException>(() => value.ToUtf8String());
        #endregion
    }

    [Fact]
    public void Documentation_InvalidData()
    {
        #region BencodeDocument_InvalidData
        Assert.Throws<FormatException>(() => BencodeDocument.Parse("d3:cow3:moo"u8));   // unterminated dictionary
        Assert.Throws<FormatException>(() => BencodeDocument.Parse("i03e"u8));          // leading zero
        Assert.Throws<FormatException>(() => BencodeDocument.Parse("i1ei2e"u8));        // two root values
        Assert.Throws<FormatException>(() => BencodeDocument.Parse("d1:ai1e1:ai2ee"u8)); // duplicate key
        #endregion
    }

    [Fact]
    public void Parse_Dictionary()
    {
        var data = Encoding.ASCII.GetBytes("d3:cow3:moo4:spam4:eggse");

        var document = BencodeDocument.Parse(data);

        var dictionary = Assert.IsType<BencodeDictionary>(document.Root);
        Assert.Equal("moo", Assert.IsType<BencodeString>(dictionary[Utf8Key("cow")]).ToUtf8String());
        Assert.Equal("eggs", Assert.IsType<BencodeString>(dictionary[Utf8Key("spam")]).ToUtf8String());
    }

    [Fact]
    public void Parse_Dictionary_WithNonUtf8Key()
    {
        var data = new byte[] { (byte)'d', (byte)'1', (byte)':', 0xFF, (byte)'3', (byte)':', (byte)'a', (byte)'b', (byte)'c', (byte)'e' };

        var document = BencodeDocument.Parse(data);

        var dictionary = Assert.IsType<BencodeDictionary>(document.Root);
        Assert.True(dictionary.TryGetValue(new BencodeString(new byte[] { 0xFF }), out var value));
        Assert.Equal("abc", Assert.IsType<BencodeString>(value).ToUtf8String());
    }

    [Fact]
    public async Task ParseAsync_FromStream()
    {
        await using var stream = new MemoryStream(Encoding.ASCII.GetBytes("li1e3:abce"));

        var document = await BencodeDocument.ParseAsync(stream);

        var list = Assert.IsType<BencodeList>(document.Root);
        Assert.Equal(2, list.Count);
        Assert.Equal(1, Assert.IsType<BencodeInteger>(list[0]).Value);
        Assert.Equal("abc", Assert.IsType<BencodeString>(list[1]).ToUtf8String());
    }

    [Fact]
    public async Task ParseAsync_FromPipeReader()
    {
        var pipe = new Pipe();
        var parseTask = BencodeDocument.ParseAsync(pipe.Reader).AsTask();

        await pipe.Writer.WriteAsync("li1e3:a"u8.ToArray());
        await pipe.Writer.WriteAsync("bce"u8.ToArray());
        await pipe.Writer.CompleteAsync();

        var document = await parseTask;

        var list = Assert.IsType<BencodeList>(document.Root);
        Assert.Equal(2, list.Count);
        Assert.Equal(1, Assert.IsType<BencodeInteger>(list[0]).Value);
        Assert.Equal("abc", Assert.IsType<BencodeString>(list[1]).ToUtf8String());

        await pipe.Reader.CompleteAsync();
    }

    [Fact]
    public async Task ParseAsync_FromPipeReader_WithTrailingData_Throws()
    {
        var pipe = new Pipe();
        await pipe.Writer.WriteAsync("i1ee"u8.ToArray());
        await pipe.Writer.CompleteAsync();

        await Assert.ThrowsAsync<FormatException>(() => BencodeDocument.ParseAsync(pipe.Reader).AsTask());

        await pipe.Reader.CompleteAsync();
    }

    [Fact]
    public async Task WriteToAsync_Stream()
    {
        var document = BencodeDocument.Parse(Encoding.ASCII.GetBytes("d1:ai1ee"));

        await using var stream = new MemoryStream();
        await document.WriteToAsync(stream);
        var content = Encoding.ASCII.GetString(stream.ToArray());

        Assert.Equal("d1:ai1ee", content);
    }

    [Fact]
    public void ToArray_CanonicalDictionaryOrdering()
    {
        var value = new BencodeDictionary
        {
            { Utf8Key("b"), new BencodeInteger(1) },
            { Utf8Key("a"), new BencodeInteger(2) },
        };

        var document = new BencodeDocument(value);
        var content = Encoding.ASCII.GetString(document.ToArray());

        Assert.Equal("d1:ai2e1:bi1ee", content);
    }

    [Fact]
    public void ReadmeSample_ParseThenEncodeADictionary()
    {
        var data = "d3:cow3:moo4:spam4:eggse"u8.ToArray();
        var document = BencodeDocument.Parse(data);

        var dictionary = (BencodeDictionary)document.Root;
        var cow = (BencodeString)dictionary[new BencodeString("cow"u8.ToArray())];
        Assert.Equal("moo", cow.ToUtf8String());

        var encoded = dictionary.ToUtf8ByteArray();
        Assert.Equal("d3:cow3:moo4:spam4:eggse", Encoding.ASCII.GetString(encoded));
    }

    [Fact]
    public void BencodeValueToArray_CanonicalFalse_PreservesInsertionOrder()
    {
        BencodeValue value = new BencodeDictionary
        {
            { Utf8Key("b"), new BencodeInteger(1) },
            { Utf8Key("a"), new BencodeInteger(2) },
        };

        var content = Encoding.ASCII.GetString(value.ToUtf8ByteArray(canonical: false));

        Assert.Equal("d1:bi1e1:ai2ee", content);
    }

    [Fact]
    public async Task BencodeValueWriteToAsync_Stream()
    {
        BencodeValue value = new BencodeList([new BencodeInteger(1), new BencodeString("abc"u8.ToArray())]);

        await using var stream = new MemoryStream();
        await value.WriteToAsync(stream);

        Assert.Equal("li1e3:abce", Encoding.ASCII.GetString(stream.ToArray()));
    }

    [Fact]
    public void Parse_DeeplyNestedLists_ThrowsInsteadOfOverflowingTheStack()
    {
        var data = new byte[200_000];
        Array.Fill(data, (byte)'l');

        var exception = Assert.Throws<FormatException>(() => BencodeDocument.Parse(data));
        Assert.Contains("nested too deeply", exception.Message);
    }

    [Fact]
    public async Task ParseAsync_DeeplyNestedLists_ThrowsInsteadOfOverflowingTheStack()
    {
        var data = new byte[200_000];
        Array.Fill(data, (byte)'l');

        var pipe = new Pipe();
        var parseTask = BencodeDocument.ParseAsync(pipe.Reader).AsTask();
        await pipe.Writer.WriteAsync(data);
        await pipe.Writer.CompleteAsync();

        var exception = await Assert.ThrowsAsync<FormatException>(() => parseTask);
        Assert.Contains("nested too deeply", exception.Message);
    }

    [Fact]
    public async Task ParseAsync_DeclaredStringLengthIsNotAllocatedBeforeTheDataArrives()
    {
        var pipe = new Pipe();
        await pipe.Writer.WriteAsync("2000000000:"u8.ToArray());
        await pipe.Writer.CompleteAsync();

        // Every byte is already buffered, so the parse runs synchronously on this thread and the counter is reliable.
        var before = GC.GetAllocatedBytesForCurrentThread();
        try
        {
            await BencodeDocument.ParseAsync(pipe.Reader);
            Assert.Fail("Expected a FormatException.");
        }
        catch (FormatException)
        {
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 1024 * 1024, $"An 11-byte document allocated {allocated} bytes.");

        await pipe.Reader.CompleteAsync();
    }

    [Theory]
    [InlineData(64, true)]
    [InlineData(65, false)]
    public void Parse_NestingDepthBoundary(int depth, bool expectedSuccess)
    {
        var data = Encoding.ASCII.GetBytes(new string('l', depth) + new string('e', depth));

        if (expectedSuccess)
        {
            Assert.IsType<BencodeList>(BencodeDocument.Parse(data).Root);
        }
        else
        {
            Assert.Throws<FormatException>(() => BencodeDocument.Parse(data));
        }
    }

    [Theory]
    [InlineData(64, true)]
    [InlineData(65, false)]
    public async Task ParseAsync_NestingDepthBoundary(int depth, bool expectedSuccess)
    {
        var data = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("d1:a", depth)) + "1:b" + new string('e', depth));

        await using var stream = new MemoryStream(data);
        if (expectedSuccess)
        {
            Assert.IsType<BencodeDictionary>((await BencodeDocument.ParseAsync(stream)).Root);
        }
        else
        {
            await Assert.ThrowsAsync<FormatException>(async () => await BencodeDocument.ParseAsync(stream));
        }
    }

    [Fact]
    public async Task ParseAsync_CancelPendingRead_StopsTheParse()
    {
        var pipe = new Pipe();
        var parseTask = BencodeDocument.ParseAsync(pipe.Reader).AsTask();
        await pipe.Writer.WriteAsync("l"u8.ToArray());

        pipe.Reader.CancelPendingRead();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => parseTask.WaitAsync(TimeSpan.FromSeconds(30)));

        await pipe.Writer.CompleteAsync();
        await pipe.Reader.CompleteAsync();
    }

    [Fact]
    public async Task ParseAsync_UnterminatedIntegerDigits_AreRejectedInsteadOfBuffered()
    {
        var pipe = new Pipe();
        var parseTask = BencodeDocument.ParseAsync(pipe.Reader).AsTask();

        // More digits than the maximum supported, so the decoder cannot be waiting for the terminator
        var digits = new byte[5000];
        Array.Fill(digits, (byte)'1');
        await pipe.Writer.WriteAsync("i"u8.ToArray());
        await pipe.Writer.WriteAsync(digits);

        await Assert.ThrowsAsync<FormatException>(() => parseTask.WaitAsync(TimeSpan.FromSeconds(30)));

        await pipe.Writer.CompleteAsync();
        await pipe.Reader.CompleteAsync();
    }

    [Fact]
    public async Task ParseAsync_CancellationToken_StopsTheParse()
    {
        using var cts = new CancellationTokenSource();
        var pipe = new Pipe();
        var parseTask = BencodeDocument.ParseAsync(pipe.Reader, cts.Token).AsTask();
        await pipe.Writer.WriteAsync("l"u8.ToArray());

        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => parseTask.WaitAsync(TimeSpan.FromSeconds(30)));

        await pipe.Writer.CompleteAsync();
        await pipe.Reader.CompleteAsync();
    }

    [Fact]
    public async Task ParseAsync_UnterminatedStringLengthDigits_AreRejectedInsteadOfBuffered()
    {
        var pipe = new Pipe();
        var parseTask = BencodeDocument.ParseAsync(pipe.Reader).AsTask();

        // More digits than the maximum supported, so the decoder cannot be waiting for the terminator
        var digits = new byte[5000];
        Array.Fill(digits, (byte)'1');
        await pipe.Writer.WriteAsync(digits);

        await Assert.ThrowsAsync<FormatException>(() => parseTask.WaitAsync(TimeSpan.FromSeconds(30)));

        await pipe.Writer.CompleteAsync();
        await pipe.Reader.CompleteAsync();
    }

    [Fact]
    public async Task ParseAsync_StringSpanningMultipleReads_IsReadCompletely()
    {
        var payload = new byte[300_000];
        for (var i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)(i % 256);
        }

        var pipe = new Pipe();
        var parseTask = BencodeDocument.ParseAsync(pipe.Reader).AsTask();

        await pipe.Writer.WriteAsync(Encoding.ASCII.GetBytes(payload.Length + ":"));
        for (var offset = 0; offset < payload.Length; offset += 10_000)
        {
            await pipe.Writer.WriteAsync(payload.AsMemory(offset, Math.Min(10_000, payload.Length - offset)));
        }

        await pipe.Writer.CompleteAsync();

        var value = Assert.IsType<BencodeString>((await parseTask).Root);
        Assert.True(value.Value.Span.SequenceEqual(payload));

        await pipe.Reader.CompleteAsync();
    }

    [Fact]
    public async Task ParseAsync_TruncatedStringData_Throws()
    {
        var pipe = new Pipe();
        await pipe.Writer.WriteAsync("10:abc"u8.ToArray());
        await pipe.Writer.CompleteAsync();

        await Assert.ThrowsAsync<FormatException>(async () => await BencodeDocument.ParseAsync(pipe.Reader));

        await pipe.Reader.CompleteAsync();
    }

    [Fact]
    public async Task ParseAsync_EmptyString_IsSupported()
    {
        var pipe = new Pipe();
        await pipe.Writer.WriteAsync("0:"u8.ToArray());
        await pipe.Writer.CompleteAsync();

        var value = Assert.IsType<BencodeString>((await BencodeDocument.ParseAsync(pipe.Reader)).Root);
        Assert.True(value.Value.IsEmpty);

        await pipe.Reader.CompleteAsync();
    }

    [Fact]
    public void Parse_InvalidData_Throws()
    {
        var data = Encoding.ASCII.GetBytes("i-0e");

        Assert.Throws<FormatException>(() => BencodeDocument.Parse(data));
    }

    [Fact]
    public void BencodeInteger_ImplementsValueEqualityAndToString()
    {
        var left = new BencodeInteger(42);
        var equal = new BencodeInteger(42);
        var different = new BencodeInteger(-1);

#pragma warning disable MFAS0033, MFAS0034 // Preserve the Equals overload validation
        Assert.True(left.Equals(equal));
        Assert.True(left.Equals((object)equal));
        Assert.False(left.Equals(different));
#pragma warning restore MFAS0033, MFAS0034
        Assert.Equal(left.GetHashCode(), equal.GetHashCode());
        Assert.Equal("42", left.ToString());
        Assert.Equal("-1", different.ToString());
    }

    [Fact]
    public void BencodeString_ToString_ValidUtf8_ReturnsTheText()
    {
        var value = new BencodeString("caf\u00e9"u8.ToArray());

        Assert.Equal("caf\u00e9", value.ToString());
    }

    [Fact]
    public void BencodeString_ToString_NonUtf8_ReturnsHexadecimalInsteadOfThrowing()
    {
        var value = new BencodeString(new byte[] { 0xFF, 0x00, 0x9A });

        Assert.Equal("0xFF009A", value.ToString());
    }

    [Fact]
    public void BencodeString_ToString_LongBinaryValue_IsTruncated()
    {
        var bytes = new byte[100];
        Array.Fill(bytes, (byte)0xFF);

        var text = new BencodeString(bytes).ToString();

        Assert.Equal("0x" + new string('F', 64) + "... (100 bytes)", text);
    }

    [Fact]
    public void BencodeString_ToUtf8String_NonUtf8_StillThrows()
    {
        var value = new BencodeString(new byte[] { 0xFF });

        Assert.Throws<DecoderFallbackException>(() => value.ToUtf8String());
    }

    [Fact]
    public void BencodeString_ToString_ParsedBinaryValue_DoesNotThrow()
    {
        var data = new List<byte>("d6:pieces20:"u8.ToArray());
        data.AddRange([0x00, 0x01, 0x02, 0xFF, 0xFE]);
        data.AddRange("0123456789abcde"u8.ToArray());
        data.Add((byte)'e');

        var dictionary = Assert.IsType<BencodeDictionary>(BencodeDocument.Parse(data.ToArray()).Root);
        var pieces = dictionary[new BencodeString("pieces"u8.ToArray())];

        Assert.Equal("0x000102FFFE303132333435363738396162636465", pieces.ToString());
    }

    [Fact]
    public void BencodeDictionary_TryGetValue_MissingKey_ReportsNoValue()
    {
        var dictionary = new BencodeDictionary
        {
            { Utf8Key("a"), new BencodeInteger(1) },
        };

        Assert.False(dictionary.TryGetValue(Utf8Key("b"), out var missing));
        Assert.Null(missing);

        Assert.True(dictionary.TryGetValue(Utf8Key("a"), out var found));
        Assert.Equal(BencodeValueKind.Integer, found.Kind);
    }

    [Fact]
    public void BencodeWriter_WriteDictionary()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new BencodeWriter(buffer);

        writer.WriteStartDictionary();
        writer.WriteUtf8Key("cow");
        writer.WriteUtf8String("moo");
        writer.WriteUtf8Key("spam");
        writer.WriteStartList();
        writer.WriteInteger(1);
        writer.WriteUtf8String("abc");
        writer.WriteEndList();
        writer.WriteEndDictionary();
        writer.Complete();

        Assert.Equal("d3:cow3:moo4:spamli1e3:abcee", Encoding.ASCII.GetString(buffer.WrittenSpan));
    }

    [Fact]
    public void BencodeDictionary_DuplicateBinaryKey_Throws()
    {
        var dictionary = new BencodeDictionary();
        dictionary.Add(new BencodeString(new byte[] { 0xFF }), new BencodeInteger(1));

        Assert.Throws<ArgumentException>(() => dictionary.Add(new BencodeString(new byte[] { 0xFF }), new BencodeInteger(2)));
    }

    [Fact]
    public void BencodeWriter_WriteDictionary_UsingSpanKeysAndValues()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new BencodeWriter(buffer);

        writer.WriteStartDictionary();
        writer.WriteKey("cow"u8);
        writer.WriteString("moo"u8);
        writer.WriteKey("spam"u8);
        writer.WriteStartList();
        writer.WriteInteger(1);
        writer.WriteString("abc"u8);
        writer.WriteEndList();
        writer.WriteEndDictionary();
        writer.Complete();

        Assert.Equal("d3:cow3:moo4:spamli1e3:abcee", Encoding.ASCII.GetString(buffer.WrittenSpan));
    }

    [Fact]
    public void BencodeWriter_WriteValueInDictionaryWithoutKey_Throws()
    {
        var writer = new BencodeWriter(new ArrayBufferWriter<byte>());
        writer.WriteStartDictionary();

        Assert.Throws<InvalidOperationException>(() => writer.WriteInteger(1));
    }

    [Fact]
    public void BencodeWriter_WriteEndDictionaryWhileExpectingValue_Throws()
    {
        var writer = new BencodeWriter(new ArrayBufferWriter<byte>());
        writer.WriteStartDictionary();
        writer.WriteUtf8Key("a");

        Assert.Throws<InvalidOperationException>(() => writer.WriteEndDictionary());
    }

    [Fact]
    public void BencodeWriter_WriteEndDictionaryWhileExpectingValue_LeavesTheDictionaryOpen()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new BencodeWriter(buffer);
        writer.WriteStartDictionary();
        writer.WriteUtf8Key("a");

        Assert.Throws<InvalidOperationException>(() => writer.WriteEndDictionary());

        writer.WriteInteger(1);
        writer.WriteEndDictionary();
        writer.Complete();

        Assert.Equal("d1:ai1ee", Encoding.ASCII.GetString(buffer.WrittenSpan));
    }

    [Fact]
    public void BencodeWriter_WriteEndListWhileInsideADictionary_LeavesTheDictionaryOpen()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new BencodeWriter(buffer);
        writer.WriteStartDictionary();
        writer.WriteUtf8Key("a");
        writer.WriteInteger(1);

        Assert.Throws<InvalidOperationException>(() => writer.WriteEndList());

        writer.WriteEndDictionary();
        writer.Complete();

        Assert.Equal("d1:ai1ee", Encoding.ASCII.GetString(buffer.WrittenSpan));
    }

    [Fact]
    public void BencodeWriter_WriteEndDictionaryWithoutAnOpenContainer_Throws()
    {
        var writer = new BencodeWriter(new ArrayBufferWriter<byte>());

        Assert.Throws<InvalidOperationException>(() => writer.WriteEndDictionary());
    }

    [Fact]
    public void BencodeWriter_WriteMultipleRootValues_Throws()
    {
        var writer = new BencodeWriter(new ArrayBufferWriter<byte>());
        writer.WriteInteger(1);

        Assert.Throws<InvalidOperationException>(() => writer.WriteInteger(2));
    }

    [Fact]
    public void BencodeWriter_CompleteWithOpenContainer_Throws()
    {
        var writer = new BencodeWriter(new ArrayBufferWriter<byte>());
        writer.WriteStartList();
        writer.WriteInteger(1);

        Assert.Throws<InvalidOperationException>(() => writer.Complete());
    }

    [Fact]
    public async Task Documentation_ReadTokens()
    {
        #region BencodeReader_ReadTokens
        var data = "d4:name5:Alice6:scoresli10ei20eee"u8.ToArray();

        // Read from a stream, such as File.OpenRead("input.bencode")
        await using var stream = new MemoryStream(data);
        await using var reader = new BencodeReader(stream);

        var tokens = new List<string>();
        while (await reader.ReadAsync())
        {
            tokens.Add(reader.TokenType switch
            {
                BencodeTokenType.Key => "key " + reader.GetUtf8String(),
                BencodeTokenType.String => "string " + reader.GetUtf8String(),
                BencodeTokenType.Integer => "integer " + reader.GetInt64(),
                _ => reader.TokenType.ToString(),
            });
        }

        Assert.Equal(["StartDictionary", "key name", "string Alice", "key scores", "StartList", "integer 10", "integer 20", "EndList", "EndDictionary"], tokens);
        #endregion
    }

    [Fact]
    public async Task Documentation_ReadSelectedValues()
    {
        #region BencodeReader_ReadValue
        var data = "d5:large12:skipped data4:metad7:versioni2eee"u8.ToArray();

        await using var stream = new MemoryStream(data);
        await using var reader = new BencodeReader(stream);

        BencodeValue? meta = null;
        await reader.ReadAsync(); // StartDictionary
        while (await reader.ReadAsync() && reader.TokenType is BencodeTokenType.Key)
        {
            var key = reader.GetUtf8String();
            await reader.ReadAsync(); // The first token of the value
            if (key is "meta")
            {
                // Read the whole value as a BencodeValue
                meta = await reader.ReadValueAsync();
            }
            else
            {
                // Skip the content of a list or a dictionary; a string or an integer is already read
                await reader.SkipAsync();
            }
        }

        var version = (BencodeInteger)((BencodeDictionary)meta!)[new BencodeString("version"u8.ToArray())];
        Assert.Equal(2, version.Value);
        #endregion
    }

    [Fact]
    public void Documentation_LargeIntegers()
    {
        #region BencodeInteger_BigInteger
        var document = BencodeDocument.Parse("li42ei123456789012345678901234567890ee"u8);
        var list = (BencodeList)document.Root;

        var small = (BencodeInteger)list[0];
        Assert.True(small.IsInt64);
        Assert.Equal(42, small.Value);

        // An integer that does not fit in a long
        var large = (BencodeInteger)list[1];
        Assert.False(large.IsInt64);
        Assert.Equal(BigInteger.Parse("123456789012345678901234567890", CultureInfo.InvariantCulture), large.BigIntegerValue);
        Assert.False(large.TryGetInt64(out _));

        // Write a large integer
        Assert.Equal("i-98765432109876543210e"u8.ToArray(), new BencodeInteger(BigInteger.Parse("-98765432109876543210", CultureInfo.InvariantCulture)).ToUtf8ByteArray());
        #endregion
    }

    [Theory]
    [InlineData("i9223372036854775807e", true)]
    [InlineData("i-9223372036854775808e", true)]
    [InlineData("i9223372036854775808e", false)]
    [InlineData("i-9223372036854775809e", false)]
    [InlineData("i99999999999999999999999999999e", false)]
    public async Task Parse_IntegerOutsideInt64_IsABigInteger(string text, bool isInt64)
    {
        var data = Encoding.ASCII.GetBytes(text);
        var expected = BigInteger.Parse(text[1..^1], CultureInfo.InvariantCulture);

        var integer = (BencodeInteger)BencodeDocument.Parse(data).Root;
        Assert.Equal(isInt64, integer.IsInt64);
        Assert.Equal(expected, integer.BigIntegerValue);
        Assert.Equal(data, integer.ToUtf8ByteArray());
        Assert.Equal(text[1..^1], integer.ToString());

        await using var stream = new MemoryStream(data);
        var streamed = (BencodeInteger)(await BencodeDocument.ParseAsync(stream)).Root;
        Assert.Equal(integer, streamed);
    }

    [Fact]
    public void BencodeInteger_OutsideInt64_ValueThrows()
    {
        var integer = new BencodeInteger(new BigInteger(long.MaxValue) + 1);

        Assert.Throws<InvalidOperationException>(() => integer.Value);
    }

    [Fact]
    public void BencodeInteger_EqualityDoesNotDependOnTheConstructor()
    {
        var fromLong = new BencodeInteger(42);
        var fromBigInteger = new BencodeInteger(new BigInteger(42));

        Assert.True(fromLong.IsInt64 && fromBigInteger.IsInt64);
        Assert.Equal(fromLong, fromBigInteger);
        Assert.Equal(fromLong.GetHashCode(), fromBigInteger.GetHashCode());
        Assert.Equal(new BencodeInteger(BigInteger.Pow(10, 30)), new BencodeInteger(BigInteger.Pow(10, 30)));
        Assert.NotEqual(new BencodeInteger(BigInteger.Pow(10, 30)), new BencodeInteger(BigInteger.Pow(10, 30) + 1));
    }

    [Theory]
    [InlineData(4096, true)]
    [InlineData(4097, false)]
    public async Task Parse_IntegerDigitLimit(int digitCount, bool expectedSuccess)
    {
        foreach (var sign in (string[])["", "-"])
        {
            var data = Encoding.ASCII.GetBytes("i" + sign + new string('9', digitCount) + "e");
            await using var stream = new MemoryStream(data);
            if (expectedSuccess)
            {
                Assert.Equal(data, BencodeDocument.Parse(data).Root.ToUtf8ByteArray());
                Assert.Equal(data, (await BencodeDocument.ParseAsync(stream)).Root.ToUtf8ByteArray());
            }
            else
            {
                Assert.Throws<FormatException>(() => BencodeDocument.Parse(data));
                await Assert.ThrowsAsync<FormatException>(async () => await BencodeDocument.ParseAsync(stream));
            }
        }
    }

    [Fact]
    public void BencodeWriter_WriteBigInteger()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new BencodeWriter(buffer);
        writer.WriteStartList();
        writer.WriteInteger(BigInteger.Pow(2, 100));
        writer.WriteInteger(BigInteger.MinusOne);
        writer.WriteEndList();
        writer.Complete();

        Assert.Equal("li1267650600228229401496703205376ei-1ee"u8.ToArray(), buffer.WrittenSpan.ToArray());
    }

    [Fact]
    public async Task BencodeReader_ReportsDepthAndBytesConsumed()
    {
        var pipe = new Pipe();
        await pipe.Writer.WriteAsync("ld1:ai1eee"u8.ToArray());
        await pipe.Writer.CompleteAsync();

        var reader = new BencodeReader(pipe.Reader);
        await using (reader)
        {
            var tokens = new List<(BencodeTokenType, int, long)>();
            while (await reader.ReadAsync())
            {
                tokens.Add((reader.TokenType, reader.CurrentDepth, reader.BytesConsumed));
            }

            Assert.Equal(
                [
                    (BencodeTokenType.StartList, 0, 1L),
                    (BencodeTokenType.StartDictionary, 1, 2L),
                    (BencodeTokenType.Key, 2, 5L),
                    (BencodeTokenType.Integer, 2, 8L),
                    (BencodeTokenType.EndDictionary, 1, 9L),
                    (BencodeTokenType.EndList, 0, 10L),
                ],
                tokens);
            Assert.Equal(BencodeTokenType.None, reader.TokenType);
            Assert.False(await reader.ReadAsync());
        }

        // The reader was created from a PipeReader, which it does not complete
        await pipe.Reader.CompleteAsync();
    }

    [Fact]
    public async Task BencodeReader_TrailingData_Throws()
    {
        await using var reader = new BencodeReader(new MemoryStream("i1ei2e"u8.ToArray()));

        Assert.True(await reader.ReadAsync());
        await Assert.ThrowsAsync<FormatException>(async () => await reader.ReadAsync());
    }

    [Theory]
    [InlineData("di1ei2ee")]
    [InlineData("dlei2ee")]
    [InlineData("d1:ae")]
    [InlineData("l")]
    [InlineData("i1")]
    [InlineData("x")]
    [InlineData("")]
    public async Task BencodeReader_InvalidData_Throws(string text)
    {
        await using var reader = new BencodeReader(new MemoryStream(Encoding.ASCII.GetBytes(text)));

        await Assert.ThrowsAsync<FormatException>(async () =>
        {
            while (await reader.ReadAsync())
            {
            }
        });
    }

    [Fact]
    public async Task BencodeReader_DuplicateKeys_AreReportedByReadValueAsync()
    {
        var data = "d1:ai1e1:ai2ee"u8.ToArray();

        await using (var reader = new BencodeReader(new MemoryStream(data)))
        {
            var keys = 0;
            while (await reader.ReadAsync())
            {
                if (reader.TokenType is BencodeTokenType.Key)
                {
                    keys++;
                }
            }

            Assert.Equal(2, keys);
        }

        await using (var reader = new BencodeReader(new MemoryStream(data)))
        {
            await reader.ReadAsync();
            await Assert.ThrowsAsync<FormatException>(async () => await reader.ReadValueAsync());
        }
    }

    [Fact]
    public async Task BencodeReader_GetValueOfTheWrongToken_Throws()
    {
        await using var reader = new BencodeReader(new MemoryStream("li99999999999999999999ee"u8.ToArray()));

        Assert.Throws<InvalidOperationException>(() => reader.GetBytes());
        await reader.ReadAsync();
        Assert.Throws<InvalidOperationException>(() => reader.GetBytes());
        Assert.Throws<InvalidOperationException>(() => reader.GetInt64());
        await reader.ReadAsync();
        Assert.Throws<OverflowException>(() => reader.GetInt64());
        Assert.False(reader.TryGetInt64(out _));
        Assert.Equal(BigInteger.Parse("99999999999999999999", CultureInfo.InvariantCulture), reader.GetBigInteger());
        await reader.ReadAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await reader.ReadValueAsync());
    }

    [Fact]
    public async Task BencodeReader_SkipAsync_SkipsNestedContainers()
    {
        await using var reader = new BencodeReader(new MemoryStream("ll1:ad1:bli1eeeei2ee"u8.ToArray()));

        await reader.ReadAsync(); // l
        await reader.ReadAsync(); // l
        await reader.SkipAsync();
        Assert.Equal(BencodeTokenType.EndList, reader.TokenType);
        Assert.Equal(1, reader.CurrentDepth);

        await reader.ReadAsync();
        Assert.Equal(2, reader.GetInt64());
        await reader.SkipAsync();
        Assert.Equal(BencodeTokenType.Integer, reader.TokenType);
    }

    [Fact]
    public async Task BencodeReader_LeaveOpen()
    {
        var stream = new MemoryStream("i1e"u8.ToArray());
        await using (var reader = new BencodeReader(stream, leaveOpen: true))
        {
            await reader.ReadAsync();
        }

        Assert.True(stream.CanRead);

        await using (var reader = new BencodeReader(stream))
        {
        }

        Assert.False(stream.CanRead);
    }

    [Fact]
    public void PublicApi_DoesNotExposeSyncStreamMethods()
    {
        Assert.Null(typeof(BencodeDocument).GetMethod(nameof(BencodeDocument.Parse), [typeof(Stream)]));
        Assert.Null(typeof(BencodeDocument).GetMethod("WriteTo", [typeof(Stream)]));
        Assert.Null(typeof(BencodeValue).GetMethod(nameof(BencodeValueExtensions.ToUtf8ByteArray), [typeof(bool)]));
        Assert.Null(typeof(BencodeValue).GetMethod(nameof(BencodeValueExtensions.WriteToAsync), [typeof(Stream), typeof(bool), typeof(CancellationToken)]));
    }

    private static BencodeString Utf8Key(string value) => new(Encoding.UTF8.GetBytes(value));
}
