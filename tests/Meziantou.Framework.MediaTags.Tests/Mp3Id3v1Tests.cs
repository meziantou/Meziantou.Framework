using Meziantou.Framework.MediaTags;
using Meziantou.Framework.MediaTags.Formats.Id3v1;

namespace Meziantou.Framework.MediaTags.Tests;

public sealed class Mp3Id3v1Tests
{
    private static string GetTestFilePath(string fileName) => Path.Combine("TestFiles", fileName);

    [Fact]
    public void ReadTags_Id3v1Only()
    {
        var result = MediaFile.ReadTags(GetTestFilePath("id3v1_only.mp3"));
        Assert.True(result.IsSuccess);

        var tags = result.Value;
        Assert.Equal("ID3v1 Title", tags.Title);
        Assert.Equal("ID3v1 Artist", tags.Artist);
        Assert.Equal("ID3v1 Album", tags.Album);
        Assert.Equal(2024, tags.Year);
        Assert.Equal("Pop", tags.Genre);
        Assert.Equal(7, tags.TrackNumber);
    }

    [Fact]
    public void ParseTag_HandCrafted_BasicFields()
    {
        // Build a 128-byte ID3v1 tag by hand
        var tag = new byte[128];
        tag[0] = (byte)'T';
        tag[1] = (byte)'A';
        tag[2] = (byte)'G';

        // Title (30 bytes at offset 3)
        System.Text.Encoding.ASCII.GetBytes("Hello World").CopyTo(tag.AsSpan(3));
        // Artist (30 bytes at offset 33)
        System.Text.Encoding.ASCII.GetBytes("Artist Name").CopyTo(tag.AsSpan(33));
        // Album (30 bytes at offset 63)
        System.Text.Encoding.ASCII.GetBytes("Album Name").CopyTo(tag.AsSpan(63));
        // Year (4 bytes at offset 93)
        System.Text.Encoding.ASCII.GetBytes("2025").CopyTo(tag.AsSpan(93));
        // Comment (28 bytes at offset 97) + zero byte + track
        System.Text.Encoding.ASCII.GetBytes("A comment").CopyTo(tag.AsSpan(97));
        tag[125] = 0;
        tag[126] = 5; // Track 5
        // Genre
        tag[127] = 17; // Rock

        var tags = new MediaTagInfo();
        var success = Id3v1Reader.TryParseTag(tag, tags);
        Assert.True(success);
        Assert.Equal("Hello World", tags.Title);
        Assert.Equal("Artist Name", tags.Artist);
        Assert.Equal("Album Name", tags.Album);
        Assert.Equal(2025, tags.Year);
        Assert.Equal("A comment", tags.Comment);
        Assert.Equal(5, tags.TrackNumber);
        Assert.Equal("Rock", tags.Genre);
    }

    [Fact]
    public void ParseTag_NoTagMagic_ReturnsFalse()
    {
        var data = new byte[128];
        data[0] = (byte)'X';
        data[1] = (byte)'Y';
        data[2] = (byte)'Z';

        var tags = new MediaTagInfo();
        Assert.False(Id3v1Reader.TryParseTag(data, tags));
    }

    [Fact]
    public void ParseTag_TooShort_ReturnsFalse()
    {
        var tags = new MediaTagInfo();
        Assert.False(Id3v1Reader.TryParseTag(new byte[10], tags));
    }
}
