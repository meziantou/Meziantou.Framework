using System.Buffers;
using System.IO.Pipelines;

namespace Meziantou.Framework.Bencode.Tests;

public sealed class BencodeDocumentTests
{
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
    public void PublicApi_DoesNotExposeSyncStreamMethods()
    {
        Assert.Null(typeof(BencodeDocument).GetMethod(nameof(BencodeDocument.Parse), [typeof(Stream)]));
        Assert.Null(typeof(BencodeDocument).GetMethod("WriteTo", [typeof(Stream)]));
        Assert.Null(typeof(BencodeValue).GetMethod(nameof(BencodeValueExtensions.ToUtf8ByteArray), [typeof(bool)]));
        Assert.Null(typeof(BencodeValue).GetMethod(nameof(BencodeValueExtensions.WriteToAsync), [typeof(Stream), typeof(bool), typeof(CancellationToken)]));
    }

    private static BencodeString Utf8Key(string value) => new(Encoding.UTF8.GetBytes(value));
}
