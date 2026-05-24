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
    public void Parse_InvalidData_Throws()
    {
        var data = Encoding.ASCII.GetBytes("i-0e");

        Assert.Throws<FormatException>(() => BencodeDocument.Parse(data));
    }

    [Fact]
    public void PublicApi_DoesNotExposeSyncStreamMethods()
    {
        Assert.Null(typeof(BencodeDocument).GetMethod(nameof(BencodeDocument.Parse), [typeof(Stream)]));
        Assert.Null(typeof(BencodeDocument).GetMethod("WriteTo", [typeof(Stream)]));
    }
}
