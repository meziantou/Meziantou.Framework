using Meziantou.Framework.MediaTags;

namespace Meziantou.Framework.MediaTags.Tests;

public sealed class FlacTests
{
    private static string GetTestFilePath(string fileName) => Path.Combine("TestFiles", fileName);

    [Fact]
    public void ReadTags_BasicFlac()
    {
        var result = MediaFile.ReadTags(GetTestFilePath("basic.flac"));
        Assert.True(result.IsSuccess);

        var tags = result.Value;
        Assert.Equal(MediaFormat.Flac, tags.Format);
        Assert.Equal("Test Title", tags.Title);
        Assert.Equal("Test Artist", tags.Artist);
        Assert.Equal("Test Album", tags.Album);
        Assert.Equal(2024, tags.Year);
        Assert.Equal("Rock", tags.Genre);
        Assert.Equal(3, tags.TrackNumber);
    }

    [Fact]
    public void ReadTags_UnicodeFlac()
    {
        var result = MediaFile.ReadTags(GetTestFilePath("unicode.flac"));
        Assert.True(result.IsSuccess);

        var tags = result.Value;
        Assert.Equal("日本語テスト", tags.Title);
        Assert.Equal("Тест Артист", tags.Artist);
        Assert.Equal("Tëst Àlbüm", tags.Album);
    }

    [Fact]
    public void ReadTags_EmptyFlac()
    {
        var result = MediaFile.ReadTags(GetTestFilePath("empty.flac"));
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ReadTags_AllFieldsFlac()
    {
        var result = MediaFile.ReadTags(GetTestFilePath("all_fields.flac"));
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
    public void WriteTags_WithPicture_RoundTrip()
    {
        var tempFile = Path.GetTempFileName() + ".flac";
        try
        {
            File.Copy(GetTestFilePath("basic.flac"), tempFile, overwrite: true);

            var pictureData = File.ReadAllBytes(Path.Combine("TestFiles", "cover.png"));
            var newTags = new MediaTagInfo { Title = "Art Title" };
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

            Assert.Equal("Art Title", readResult.Value.Title);
            Assert.Single(readResult.Value.Pictures);
            Assert.Equal("image/png", readResult.Value.Pictures[0].MimeType);
            Assert.Equal(pictureData, readResult.Value.Pictures[0].Data);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ReadTags_LongValuesFlac()
    {
        var result = MediaFile.ReadTags(GetTestFilePath("long_values.flac"));
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.Title);
        Assert.True(result.Value.Title.Length > 100);
    }

    [Fact]
    public void WriteTags_RoundTrip()
    {
        var tempFile = Path.GetTempFileName() + ".flac";
        try
        {
            File.Copy(GetTestFilePath("basic.flac"), tempFile, overwrite: true);

            var newTags = new MediaTagInfo
            {
                Title = "New FLAC Title",
                Artist = "New FLAC Artist",
                Album = "New FLAC Album",
                Year = 2025,
                Genre = "Classical",
                TrackNumber = 1,
                TrackTotal = 10,
                DiscNumber = 1,
                DiscTotal = 2,
                Comment = "FLAC Comment",
                Lyrics = "FLAC Lyrics",
                Isrc = "USRC17607839",
            };

            var writeResult = MediaFile.WriteTags(tempFile, newTags);
            Assert.True(writeResult.IsSuccess);

            var readResult = MediaFile.ReadTags(tempFile);
            Assert.True(readResult.IsSuccess);

            var tags = readResult.Value;
            Assert.Equal("New FLAC Title", tags.Title);
            Assert.Equal("New FLAC Artist", tags.Artist);
            Assert.Equal("New FLAC Album", tags.Album);
            Assert.Equal(2025, tags.Year);
            Assert.Equal("Classical", tags.Genre);
            Assert.Equal(1, tags.TrackNumber);
            Assert.Equal(10, tags.TrackTotal);
            Assert.Equal(1, tags.DiscNumber);
            Assert.Equal(2, tags.DiscTotal);
            Assert.Equal("FLAC Comment", tags.Comment);
            Assert.Equal("FLAC Lyrics", tags.Lyrics);
            Assert.Equal("USRC17607839", tags.Isrc);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void WriteTags_PreservesAudioData()
    {
        var tempFile = Path.GetTempFileName() + ".flac";
        try
        {
            File.Copy(GetTestFilePath("basic.flac"), tempFile, overwrite: true);

            var tags = new MediaTagInfo { Title = "Modified" };
            MediaFile.WriteTags(tempFile, tags);

            // The file should still be a valid FLAC (starts with fLaC)
            using var stream = File.OpenRead(tempFile);
            var magic = new byte[4];
            stream.ReadAtLeast(magic, 4);
            Assert.Equal((byte)'f', magic[0]);
            Assert.Equal((byte)'L', magic[1]);
            Assert.Equal((byte)'a', magic[2]);
            Assert.Equal((byte)'C', magic[3]);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ReadTags_InvalidFile_ReturnsError()
    {
        using var stream = new MemoryStream([0x00, 0x01, 0x02, 0x03]);
        var result = MediaFile.ReadTags(stream, MediaFormat.Flac);
        Assert.False(result.IsSuccess);
        Assert.Equal(MediaTagError.UnsupportedFormat, result.Error);
    }
}
