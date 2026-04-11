using System.Buffers.Binary;
using System.Text;
using Meziantou.Framework.MediaTags;
using Meziantou.Framework.MediaTags.Formats.VorbisComment;

namespace Meziantou.Framework.MediaTags.Tests;

public sealed class VorbisCommentTests
{
    [Fact]
    public void TryParse_BasicComments()
    {
        var data = BuildVorbisComment("TestVendor", [
            "TITLE=Test Title",
            "ARTIST=Test Artist",
            "ALBUM=Test Album",
            "DATE=2024",
            "TRACKNUMBER=3",
            "GENRE=Rock",
        ]);

        var tags = new MediaTagInfo();
        var result = VorbisCommentReader.TryParse(data, tags);
        Assert.True(result);

        Assert.Equal("Test Title", tags.Title);
        Assert.Equal("Test Artist", tags.Artist);
        Assert.Equal("Test Album", tags.Album);
        Assert.Equal(2024, tags.Year);
        Assert.Equal(3, tags.TrackNumber);
        Assert.Equal("Rock", tags.Genre);
    }

    [Fact]
    public void TryParse_CaseInsensitiveFieldNames()
    {
        var data = BuildVorbisComment("Vendor", [
            "title=Lower Case Title",
            "ARTIST=Upper Case Artist",
            "Album=Mixed Case Album",
        ]);

        var tags = new MediaTagInfo();
        VorbisCommentReader.TryParse(data, tags);

        Assert.Equal("Lower Case Title", tags.Title);
        Assert.Equal("Upper Case Artist", tags.Artist);
        Assert.Equal("Mixed Case Album", tags.Album);
    }

    [Fact]
    public void TryParse_CustomFields()
    {
        var data = BuildVorbisComment("Vendor", [
            "TITLE=Title",
            "CUSTOMFIELD=CustomValue",
        ]);

        var tags = new MediaTagInfo();
        VorbisCommentReader.TryParse(data, tags);

        Assert.Equal("Title", tags.Title);
        Assert.True(tags.CustomFields.ContainsKey("CUSTOMFIELD"));
        Assert.Equal("CustomValue", tags.CustomFields["CUSTOMFIELD"]);
    }

    [Fact]
    public void TryParse_EmptyData_ReturnsFalse()
    {
        var tags = new MediaTagInfo();
        Assert.False(VorbisCommentReader.TryParse([], tags));
    }

    [Fact]
    public void TryParse_TruncatedData_ReturnsFalse()
    {
        var tags = new MediaTagInfo();
        Assert.False(VorbisCommentReader.TryParse([0x01, 0x02], tags));
    }

    [Fact]
    public void RoundTrip_WriteThenRead()
    {
        var originalTags = new MediaTagInfo
        {
            Title = "Round Trip Title",
            Artist = "Round Trip Artist",
            Album = "Round Trip Album",
            Year = 2025,
            TrackNumber = 5,
            TrackTotal = 10,
            Genre = "Pop",
            Comment = "A comment",
        };

        var data = VorbisCommentWriter.Build(originalTags);
        var readTags = new MediaTagInfo();
        var result = VorbisCommentReader.TryParse(data, readTags);
        Assert.True(result);

        Assert.Equal("Round Trip Title", readTags.Title);
        Assert.Equal("Round Trip Artist", readTags.Artist);
        Assert.Equal("Round Trip Album", readTags.Album);
        Assert.Equal(2025, readTags.Year);
        Assert.Equal(5, readTags.TrackNumber);
        Assert.Equal(10, readTags.TrackTotal);
        Assert.Equal("Pop", readTags.Genre);
        Assert.Equal("A comment", readTags.Comment);
    }

    [Fact]
    public void TryParse_UnicodeValues()
    {
        var data = BuildVorbisComment("Vendor", [
            "TITLE=日本語テスト",
            "ARTIST=Тест",
        ]);

        var tags = new MediaTagInfo();
        VorbisCommentReader.TryParse(data, tags);

        Assert.Equal("日本語テスト", tags.Title);
        Assert.Equal("Тест", tags.Artist);
    }

    private static byte[] BuildVorbisComment(string vendor, string[] comments)
    {
        var vendorBytes = Encoding.UTF8.GetBytes(vendor);
        var size = 4 + vendorBytes.Length + 4;
        foreach (var comment in comments)
        {
            size += 4 + Encoding.UTF8.GetByteCount(comment);
        }

        var result = new byte[size];
        var offset = 0;

        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(offset), (uint)vendorBytes.Length);
        offset += 4;
        vendorBytes.CopyTo(result, offset);
        offset += vendorBytes.Length;

        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(offset), (uint)comments.Length);
        offset += 4;

        foreach (var comment in comments)
        {
            var bytes = Encoding.UTF8.GetBytes(comment);
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(offset), (uint)bytes.Length);
            offset += 4;
            bytes.CopyTo(result, offset);
            offset += bytes.Length;
        }

        return result;
    }
}
