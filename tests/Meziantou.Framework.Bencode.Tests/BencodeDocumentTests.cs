using System.Buffers;
using System.IO.Pipelines;
using System.Text;

namespace Meziantou.Framework.Bencode.Tests;

public sealed class BencodeDocumentTests
{
    [Fact]
    public void Parse_Dictionary()
    {
        var data = Encoding.ASCII.GetBytes("d3:cow3:moo4:spam4:eggse");

        var document = BencodeDocument.Parse(data);

        var dictionary = Assert.IsType<BencodeDictionary>(document.Root);
        Assert.Equal("moo", Assert.IsType<BencodeString>(dictionary["cow"]).ToUtf8String());
        Assert.Equal("eggs", Assert.IsType<BencodeString>(dictionary["spam"]).ToUtf8String());
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
            { "b", new BencodeInteger(1) },
            { "a", new BencodeInteger(2) },
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
            { "b", new BencodeInteger(1) },
            { "a", new BencodeInteger(2) },
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
    public void BencodeWriter_WriteDictionary()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new BencodeWriter(buffer);

        writer.WriteStartDictionary();
        writer.WriteKey("cow");
        writer.WriteString("moo");
        writer.WriteKey("spam");
        writer.WriteStartList();
        writer.WriteInteger(1);
        writer.WriteString("abc");
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
        writer.WriteKey("a");

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
}
