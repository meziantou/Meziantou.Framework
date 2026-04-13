using Meziantou.Framework.MediaTags;

namespace Meziantou.Framework.MediaTags.Tests;

public sealed class OggVorbisTests
{
    private static string GetTestFilePath(string fileName) => Path.Combine("TestFiles", fileName);

    [Fact]
    public void ReadTags_BasicOgg()
    {
        var result = MediaFile.ReadTags(GetTestFilePath("basic.ogg"));
        Assert.True(result.IsSuccess);

        var tags = result.Value;
        Assert.Equal(MediaFormat.OggVorbis, tags.Format);
        Assert.Equal("Test Title", tags.Title);
        Assert.Equal("Test Artist", tags.Artist);
        Assert.Equal("Test Album", tags.Album);
        Assert.Equal(2024, tags.Year);
        Assert.Equal("Rock", tags.Genre);
        Assert.Equal(3, tags.TrackNumber);
    }

    [Fact]
    public void ReadTags_UnicodeOgg()
    {
        var result = MediaFile.ReadTags(GetTestFilePath("unicode.ogg"));
        Assert.True(result.IsSuccess);

        var tags = result.Value;
        Assert.Equal("日本語テスト", tags.Title);
        Assert.Equal("Тест Артист", tags.Artist);
        Assert.Equal("Tëst Àlbüm", tags.Album);
    }

    [Fact]
    public void ReadTags_EmptyOgg()
    {
        var result = MediaFile.ReadTags(GetTestFilePath("empty.ogg"));
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ReadTags_AllFieldsOgg()
    {
        var result = MediaFile.ReadTags(GetTestFilePath("all_fields.ogg"));
        Assert.True(result.IsSuccess);

        var tags = result.Value;
        Assert.Equal("All Fields Title", tags.Title);
        Assert.Equal("All Fields Artist", tags.Artist);
        Assert.Equal("All Fields Album", tags.Album);
        Assert.Equal("All Fields Album Artist", tags.AlbumArtist);
        Assert.Equal("Electronic", tags.Genre);
        Assert.Equal(2023, tags.Year);
        Assert.Equal(5, tags.TrackNumber);
        Assert.Equal(12, tags.TrackTotal);
        Assert.Equal(2, tags.DiscNumber);
        Assert.Equal(3, tags.DiscTotal);
        Assert.Equal("All Fields Composer", tags.Composer);
        Assert.Equal("All Fields Comment", tags.Comment);
        Assert.Equal("2023 Test", tags.Copyright);
    }

    [Fact]
    public void WriteTags_RoundTrip()
    {
        var tempFile = Path.GetTempFileName() + ".ogg";
        try
        {
            File.Copy(GetTestFilePath("basic.ogg"), tempFile, overwrite: true);

            var newTags = new MediaTagInfo
            {
                Title = "New OGG Title",
                Artist = "New OGG Artist",
                Year = 2025,
                TrackNumber = 4,
                Lyrics = "New OGG Lyrics",
                Isrc = "USRC17607839",
            };

            var writeResult = MediaFile.WriteTags(tempFile, newTags);
            Assert.True(writeResult.IsSuccess);

            var readResult = MediaFile.ReadTags(tempFile);
            Assert.True(readResult.IsSuccess);

            Assert.Equal("New OGG Title", readResult.Value.Title);
            Assert.Equal("New OGG Artist", readResult.Value.Artist);
            Assert.Equal(2025, readResult.Value.Year);
            Assert.Equal(4, readResult.Value.TrackNumber);
            Assert.Equal("New OGG Lyrics", readResult.Value.Lyrics);
            Assert.Equal("USRC17607839", readResult.Value.Isrc);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void WriteTags_Idempotent()
    {
        var tempFile = Path.GetTempFileName() + ".ogg";
        try
        {
            File.Copy(GetTestFilePath("basic.ogg"), tempFile, overwrite: true);

            var tags = new MediaTagInfo { Title = "Idempotent" };

            // Write once
            MediaFile.WriteTags(tempFile, tags);
            var firstRead = MediaFile.ReadTags(tempFile);

            // Write again
            MediaFile.WriteTags(tempFile, tags);
            var secondRead = MediaFile.ReadTags(tempFile);

            Assert.Equal(firstRead.Value.Title, secondRead.Value.Title);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
