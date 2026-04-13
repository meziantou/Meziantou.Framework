using Meziantou.Framework.MediaTags;

namespace Meziantou.Framework.MediaTags.Tests;

public sealed class Mp3Id3v2Tests
{
    private static string GetTestFilePath(string fileName) => Path.Combine("TestFiles", fileName);

    [Fact]
    public void ReadTags_BasicMp3()
    {
        var result = MediaFile.ReadTags(GetTestFilePath("basic.mp3"));
        Assert.True(result.IsSuccess);

        var tags = result.Value;
        Assert.Equal(MediaFormat.Mp3, tags.Format);
        Assert.Equal("Test Title", tags.Title);
        Assert.Equal("Test Artist", tags.Artist);
        Assert.Equal("Test Album", tags.Album);
        Assert.Equal(2024, tags.Year);
        Assert.Equal("Rock", tags.Genre);
        Assert.Equal(3, tags.TrackNumber);
    }

    [Fact]
    public void ReadTags_UnicodeMp3()
    {
        var result = MediaFile.ReadTags(GetTestFilePath("unicode.mp3"));
        Assert.True(result.IsSuccess);

        var tags = result.Value;
        Assert.Contains("日本語テスト", tags.Title, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadTags_EmptyMp3()
    {
        var result = MediaFile.ReadTags(GetTestFilePath("empty.mp3"));
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ReadTags_AllFieldsMp3()
    {
        var result = MediaFile.ReadTags(GetTestFilePath("all_fields.mp3"));
        Assert.True(result.IsSuccess);

        var tags = result.Value;
        Assert.Equal("All Fields Title", tags.Title);
        Assert.Equal("All Fields Artist", tags.Artist);
        Assert.Equal("All Fields Album", tags.Album);
        Assert.Equal("Electronic", tags.Genre);
        Assert.Equal(2023, tags.Year);
        Assert.Equal(5, tags.TrackNumber);
        Assert.Equal("All Fields Comment", tags.Comment);
    }

    [Fact]
    public void ReadTags_WithArt()
    {
        var result = MediaFile.ReadTags(GetTestFilePath("with_art.mp3"));
        Assert.True(result.IsSuccess);

        var tags = result.Value;
        Assert.Equal("Art Title", tags.Title);
        Assert.NotEmpty(tags.Pictures);
        Assert.NotEmpty(tags.Pictures[0].Data);
    }

    [Fact]
    public void ReadTags_LongValues()
    {
        var result = MediaFile.ReadTags(GetTestFilePath("long_values.mp3"));
        Assert.True(result.IsSuccess);

        var tags = result.Value;
        Assert.NotNull(tags.Title);
        Assert.True(tags.Title.Length > 100); // Should be very long
    }

    [Fact]
    public void WriteTags_RoundTrip()
    {
        var tempFile = Path.GetTempFileName() + ".mp3";
        try
        {
            File.Copy(GetTestFilePath("basic.mp3"), tempFile, overwrite: true);

            var newTags = new MediaTagInfo
            {
                Title = "New Title",
                Artist = "New Artist",
                Album = "New Album",
                Year = 2025,
                Genre = "Jazz",
                TrackNumber = 7,
                TrackTotal = 15,
                Comment = "New Comment",
                Lyrics = "New Lyrics",
                Isrc = "USRC17607839",
            };

            var writeResult = MediaFile.WriteTags(tempFile, newTags);
            Assert.True(writeResult.IsSuccess);

            var readResult = MediaFile.ReadTags(tempFile);
            Assert.True(readResult.IsSuccess);

            var tags = readResult.Value;
            Assert.Equal("New Title", tags.Title);
            Assert.Equal("New Artist", tags.Artist);
            Assert.Equal("New Album", tags.Album);
            Assert.Equal(2025, tags.Year);
            Assert.Equal("Jazz", tags.Genre);
            Assert.Equal(7, tags.TrackNumber);
            Assert.Equal(15, tags.TrackTotal);
            Assert.Equal("New Comment", tags.Comment);
            Assert.Equal("New Lyrics", tags.Lyrics);
            Assert.Equal("USRC17607839", tags.Isrc);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void WriteTags_Unicode_RoundTrip()
    {
        var tempFile = Path.GetTempFileName() + ".mp3";
        try
        {
            File.Copy(GetTestFilePath("basic.mp3"), tempFile, overwrite: true);

            var newTags = new MediaTagInfo
            {
                Title = "日本語テスト",
                Artist = "Тест Артист",
                Album = "Tëst Àlbüm",
            };

            var writeResult = MediaFile.WriteTags(tempFile, newTags);
            Assert.True(writeResult.IsSuccess);

            var readResult = MediaFile.ReadTags(tempFile);
            Assert.True(readResult.IsSuccess);

            Assert.Equal("日本語テスト", readResult.Value.Title);
            Assert.Equal("Тест Артист", readResult.Value.Artist);
            Assert.Equal("Tëst Àlbüm", readResult.Value.Album);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void WriteTags_WithPicture_RoundTrip()
    {
        var tempFile = Path.GetTempFileName() + ".mp3";
        try
        {
            File.Copy(GetTestFilePath("basic.mp3"), tempFile, overwrite: true);

            var pictureData = File.ReadAllBytes(Path.Combine("TestFiles", "cover.png"));
            var newTags = new MediaTagInfo
            {
                Title = "With Picture",
            };
            newTags.Pictures.Add(new MediaPicture
            {
                PictureType = MediaPictureType.FrontCover,
                MimeType = "image/png",
                Description = "Cover",
                Data = pictureData,
            });

            var writeResult = MediaFile.WriteTags(tempFile, newTags);
            Assert.True(writeResult.IsSuccess);

            var readResult = MediaFile.ReadTags(tempFile);
            Assert.True(readResult.IsSuccess);

            Assert.Equal("With Picture", readResult.Value.Title);
            Assert.Single(readResult.Value.Pictures);
            Assert.Equal(MediaPictureType.FrontCover, readResult.Value.Pictures[0].PictureType);
            Assert.Equal("image/png", readResult.Value.Pictures[0].MimeType);
            Assert.Equal(pictureData, readResult.Value.Pictures[0].Data);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ReadTags_FromStream()
    {
        using var stream = File.OpenRead(GetTestFilePath("basic.mp3"));
        var result = MediaFile.ReadTags(stream, MediaFormat.Mp3);
        Assert.True(result.IsSuccess);
        Assert.Equal("Test Title", result.Value.Title);
    }

    [Fact]
    public void ReadTags_CorruptData_ReturnsResult()
    {
        using var stream = new MemoryStream([0xFF, 0xFB, 0x00, 0x00]); // MP3 frame sync but truncated
        var result = MediaFile.ReadTags(stream, MediaFormat.Mp3);
        // Should succeed with empty tags (no valid ID3 found)
        Assert.True(result.IsSuccess);
    }
}
