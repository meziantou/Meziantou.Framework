using Meziantou.Framework.MediaTags.Formats.Ogg;

namespace Meziantou.Framework.MediaTags.Tests;

public sealed class OggOpusTests
{
    private static string GetTestFilePath(string fileName) => Path.Combine("TestFiles", fileName);

    [Fact]
    public void ReadTags_BasicOpus()
    {
        var result = MediaFile.ReadTags(GetTestFilePath("basic.opus"));
        Assert.True(result.IsSuccess);

        var tags = result.Value;
        Assert.Equal(MediaFormat.OggOpus, tags.Format);
        Assert.Equal("Test Title", tags.Title);
        Assert.Equal("Test Artist", tags.Artist);
        Assert.Equal("Test Album", tags.Album);
        Assert.Equal(2024, tags.Year);
        Assert.NotNull(tags.Duration);
        Assert.InRange(tags.Duration.Value.TotalSeconds, 0.95, 1.1);
    }

    [Fact]
    public void WriteTags_RoundTrip()
    {
        var tempFile = Path.GetTempFileName() + ".opus";
        try
        {
            File.Copy(GetTestFilePath("basic.opus"), tempFile, overwrite: true);

            var newTags = new MediaTagInfo
            {
                Title = "New Opus Title",
                Artist = "New Opus Artist",
                Lyrics = "New Opus Lyrics",
                Isrc = "USRC17607839",
            };

            var writeResult = MediaFile.WriteTags(tempFile, newTags);
            Assert.True(writeResult.IsSuccess);

            var readResult = MediaFile.ReadTags(tempFile);
            Assert.True(readResult.IsSuccess);

            Assert.Equal("New Opus Title", readResult.Value.Title);
            Assert.Equal("New Opus Artist", readResult.Value.Artist);
            Assert.Equal("New Opus Lyrics", readResult.Value.Lyrics);
            Assert.Equal("USRC17607839", readResult.Value.Isrc);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void WriteTags_LargeCommentPacket_RoundTrip()
    {
        var tempFile = Path.GetTempFileName() + ".opus";
        try
        {
            File.Copy(GetTestFilePath("basic.opus"), tempFile, overwrite: true);

            var largeLyrics = new string('a', 80000);
            var newTags = new MediaTagInfo
            {
                Title = "Large Opus Title",
                Lyrics = largeLyrics,
            };

            var writeResult = MediaFile.WriteTags(tempFile, newTags);
            Assert.True(writeResult.IsSuccess);

            var readResult = MediaFile.ReadTags(tempFile);
            Assert.True(readResult.IsSuccess);

            Assert.Equal("Large Opus Title", readResult.Value.Title);
            Assert.Equal(largeLyrics, readResult.Value.Lyrics);
            Assert.True(ContainsContinuedPage(tempFile));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    private static bool ContainsContinuedPage(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        while (true)
        {
            var page = OggPage.Read(stream);
            if (page is null)
                return false;

            if ((page.HeaderType & OggPage.HeaderTypeContinued) != 0)
                return true;
        }
    }

    [Fact]
    public void ReadTags_AllFieldsOpus()
    {
        var result = MediaFile.ReadTags(GetTestFilePath("all_fields.opus"));
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
    }

    [Fact]
    public void ReadTags_UnicodeOpus()
    {
        var result = MediaFile.ReadTags(GetTestFilePath("unicode.opus"));
        Assert.True(result.IsSuccess);
        Assert.Equal("Ünïcödé Títlé", result.Value.Title);
        Assert.Equal("Àrtïst 日本語", result.Value.Artist);
    }

    [Fact]
    public void ReadTags_EmptyOpus()
    {
        var result = MediaFile.ReadTags(GetTestFilePath("empty.opus"));
        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Title);
        Assert.Null(result.Value.Artist);
    }

    [Fact]
    public void WriteTags_CommentPacketHasNoFramingBit()
    {
        // An OpusTags packet ends at its last comment; a Vorbis framing bit here would be read as tag data.
        var tempFile = Path.GetTempFileName() + ".opus";
        try
        {
            File.Copy(GetTestFilePath("basic.opus"), tempFile, overwrite: true);

            Assert.True(MediaFile.WriteTags(tempFile, new MediaTagInfo { Title = "Opus Title" }).IsSuccess);

            var packet = OggPageInspector.FindPacket(File.ReadAllBytes(tempFile), "OpusTags"u8);
            Assert.NotNull(packet);
            Assert.NotEqual(0x01, packet[^1]);
            Assert.Equal("Opus Title", MediaFile.ReadTags(tempFile).Value.Title);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void WriteTags_EveryOutputPageHasTheChecksumItDeclares()
    {
        var tempFile = Path.GetTempFileName() + ".opus";
        try
        {
            File.Copy(GetTestFilePath("basic.opus"), tempFile, overwrite: true);

            Assert.True(MediaFile.WriteTags(tempFile, new MediaTagInfo { Title = "Checksummed" }).IsSuccess);

            var pages = OggPageInspector.ReadPages(File.ReadAllBytes(tempFile));
            Assert.NotEmpty(pages);
            foreach (var page in pages)
            {
                Assert.Equal(page.StoredChecksum, Meziantou.Framework.MediaTags.Internals.OggCrc32.Compute(page.BytesWithZeroedChecksum));
            }
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ReadTags_NotAnOggFile_ReturnsSuccessWithNoTags()
    {
        using var stream = new MemoryStream([0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08]);

        var result = MediaFile.ReadTags(stream, MediaFormat.OggOpus);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Title);
    }
}
