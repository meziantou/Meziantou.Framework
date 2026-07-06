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

        Assert.True(left.Equals(equal));
        Assert.True(left.Equals((object)equal));
        Assert.False(left.Equals(different));
        Assert.Equal(left.GetHashCode(), equal.GetHashCode());
        Assert.Equal("42", left.ToString());
        Assert.Equal("-1", different.ToString());
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
