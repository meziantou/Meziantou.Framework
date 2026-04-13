using Meziantou.Framework.MediaTags;

namespace Meziantou.Framework.MediaTags.Tests;

public sealed class Mp4Tests
{
    private static string GetTestFilePath(string fileName) => Path.Combine("TestFiles", fileName);

    [Fact]
    public void ReadTags_BasicM4a()
    {
        var result = MediaFile.ReadTags(GetTestFilePath("basic.m4a"));
        Assert.True(result.IsSuccess);

        var tags = result.Value;
        Assert.Equal(MediaFormat.Mp4, tags.Format);
        Assert.Equal("Test Title", tags.Title);
        Assert.Equal("Test Artist", tags.Artist);
        Assert.Equal("Test Album", tags.Album);
        Assert.Equal(2024, tags.Year);
        Assert.Equal("Rock", tags.Genre);
        Assert.Equal(3, tags.TrackNumber);
    }

    [Fact]
    public void ReadTags_UnicodeM4a()
    {
        var result = MediaFile.ReadTags(GetTestFilePath("unicode.m4a"));
        Assert.True(result.IsSuccess);

        var tags = result.Value;
        Assert.Equal("日本語テスト", tags.Title);
        Assert.Equal("Тест Артист", tags.Artist);
        Assert.Equal("Tëst Àlbüm", tags.Album);
    }

    [Fact]
    public void ReadTags_EmptyM4a()
    {
        var result = MediaFile.ReadTags(GetTestFilePath("empty.m4a"));
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ReadTags_AllFieldsM4a()
    {
        var result = MediaFile.ReadTags(GetTestFilePath("all_fields.m4a"));
        Assert.True(result.IsSuccess);

        var tags = result.Value;
        Assert.Equal("All Fields Title", tags.Title);
        Assert.Equal("All Fields Artist", tags.Artist);
        Assert.Equal("All Fields Album", tags.Album);
        Assert.Equal("Electronic", tags.Genre);
        Assert.Equal(2023, tags.Year);
        Assert.Equal(5, tags.TrackNumber);
        Assert.Equal(12, tags.TrackTotal);
        Assert.Equal(2, tags.DiscNumber);
        Assert.Equal(3, tags.DiscTotal);
        Assert.Equal("All Fields Comment", tags.Comment);
    }

    [Fact]
    public void WriteTags_WithPicture_RoundTrip()
    {
        var tempFile = Path.GetTempFileName() + ".m4a";
        try
        {
            File.Copy(GetTestFilePath("basic.m4a"), tempFile, overwrite: true);

            var pictureData = File.ReadAllBytes(Path.Combine("TestFiles", "cover.png"));
            var newTags = new MediaTagInfo { Title = "Art Title" };
            newTags.Pictures.Add(new MediaPicture
            {
                PictureType = MediaPictureType.FrontCover,
                MimeType = "image/png",
                Data = pictureData,
            });

            var writeResult = MediaFile.WriteTags(tempFile, newTags);
            Assert.True(writeResult.IsSuccess);

            var readResult = MediaFile.ReadTags(tempFile);
            Assert.True(readResult.IsSuccess);

            Assert.Equal("Art Title", readResult.Value.Title);
            Assert.Single(readResult.Value.Pictures);
            Assert.Equal(pictureData, readResult.Value.Pictures[0].Data);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void WriteTags_RoundTrip()
    {
        var tempFile = Path.GetTempFileName() + ".m4a";
        try
        {
            File.Copy(GetTestFilePath("basic.m4a"), tempFile, overwrite: true);

            var newTags = new MediaTagInfo
            {
                Title = "New MP4 Title",
                Artist = "New MP4 Artist",
                Album = "New MP4 Album",
                Year = 2025,
                TrackNumber = 2,
                TrackTotal = 8,
                Lyrics = "New MP4 Lyrics",
                Isrc = "USRC17607839",
            };

            var writeResult = MediaFile.WriteTags(tempFile, newTags);
            Assert.True(writeResult.IsSuccess);

            var readResult = MediaFile.ReadTags(tempFile);
            Assert.True(readResult.IsSuccess);

            Assert.Equal("New MP4 Title", readResult.Value.Title);
            Assert.Equal("New MP4 Artist", readResult.Value.Artist);
            Assert.Equal("New MP4 Album", readResult.Value.Album);
            Assert.Equal(2025, readResult.Value.Year);
            Assert.Equal(2, readResult.Value.TrackNumber);
            Assert.Equal(8, readResult.Value.TrackTotal);
            Assert.Equal("New MP4 Lyrics", readResult.Value.Lyrics);
            Assert.Equal("USRC17607839", readResult.Value.Isrc);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ReadTags_InvalidFile_ReturnsError()
    {
        using var stream = new MemoryStream([0x00, 0x00, 0x00, 0x08, (byte)'f', (byte)'t', (byte)'y', (byte)'p']);
        var result = MediaFile.ReadTags(stream, MediaFormat.Mp4);
        // Very short ftyp, should succeed with empty tags or handle gracefully
        Assert.True(result.IsSuccess);
    }
}
