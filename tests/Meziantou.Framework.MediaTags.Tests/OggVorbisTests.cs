using Meziantou.Framework.MediaTags.Formats.Ogg;

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
            Assert.True(MediaFile.WriteTags(tempFile, tags).IsSuccess);
            var firstRead = MediaFile.ReadTags(tempFile);

            // Write again
            Assert.True(MediaFile.WriteTags(tempFile, tags).IsSuccess);
            var secondRead = MediaFile.ReadTags(tempFile);

            // Comparing the two reads alone would pass if the writer dropped the title entirely
            Assert.Equal("Idempotent", firstRead.Value.Title);
            Assert.Equal("Idempotent", secondRead.Value.Title);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void WriteTags_LargeCommentPacket_RoundTrip()
    {
        var tempFile = Path.GetTempFileName() + ".ogg";
        try
        {
            File.Copy(GetTestFilePath("basic.ogg"), tempFile, overwrite: true);

            var largeLyrics = new string('a', 80000);
            var tags = new MediaTagInfo
            {
                Title = "Large OGG Title",
                Lyrics = largeLyrics,
            };

            var writeResult = MediaFile.WriteTags(tempFile, tags);
            Assert.True(writeResult.IsSuccess);

            var readResult = MediaFile.ReadTags(tempFile);
            Assert.True(readResult.IsSuccess);

            Assert.Equal("Large OGG Title", readResult.Value.Title);
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
    public void WriteTags_CommentPacketEndsWithTheFramingBit()
    {
        // libvorbis rejects the whole comment header when the framing bit is missing, so a file written
        // without it is refused by every libvorbis-based player even though this library reads it back.
        var tempFile = Path.GetTempFileName() + ".ogg";
        try
        {
            File.Copy(GetTestFilePath("basic.ogg"), tempFile, overwrite: true);

            Assert.True(MediaFile.WriteTags(tempFile, new MediaTagInfo { Title = "Framed" }).IsSuccess);

            byte[] vorbisCommentPrefix = [0x03, (byte)'v', (byte)'o', (byte)'r', (byte)'b', (byte)'i', (byte)'s'];
            var packet = OggPageInspector.FindPacket(File.ReadAllBytes(tempFile), vorbisCommentPrefix);
            Assert.NotNull(packet);
            Assert.Equal(0x01, packet[^1]);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void WriteTags_EveryOutputPageHasTheChecksumItDeclares()
    {
        var tempFile = Path.GetTempFileName() + ".ogg";
        try
        {
            File.Copy(GetTestFilePath("basic.ogg"), tempFile, overwrite: true);

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
    public void WriteTags_PageSequenceNumbersStayContiguous()
    {
        var tempFile = Path.GetTempFileName() + ".ogg";
        try
        {
            File.Copy(GetTestFilePath("basic.ogg"), tempFile, overwrite: true);

            Assert.True(MediaFile.WriteTags(tempFile, new MediaTagInfo { Title = "Sequenced" }).IsSuccess);

            var pages = OggPageInspector.ReadPages(File.ReadAllBytes(tempFile));
            for (var i = 0; i < pages.Count; i++)
            {
                Assert.Equal((uint)i, pages[i].SequenceNumber);
            }
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void WriteTags_MultiplexedStream_IsRefused()
    {
        // Rewriting one stream of a multiplexed file would renumber the pages of the other one and leave a
        // sequence hole in a stream this library was never asked to touch.
        var tempFile = Path.GetTempFileName() + ".ogg";
        try
        {
            var vorbis = File.ReadAllBytes(GetTestFilePath("basic.ogg"));
            var opus = File.ReadAllBytes(GetTestFilePath("basic.opus"));
            File.WriteAllBytes(tempFile, [.. vorbis, .. opus]);

            var result = MediaFile.WriteTags(tempFile, new MediaTagInfo { Title = "Title" });

            Assert.False(result.IsSuccess);
            Assert.Equal(MediaTagError.UnsupportedFormat, result.Error);
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

        var result = MediaFile.ReadTags(stream, MediaFormat.OggVorbis);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Title);
    }

    [Fact]
    public void ReadTags_TruncatedPage_DoesNotAllocateTheDeclaredSize()
    {
        // A page whose segment table declares far more data than the file holds must not be believed.
        var page = new byte[27 + 255];
        "OggS"u8.CopyTo(page);
        page[5] = 0x02; // begin of stream
        page[26] = 255; // 255 segments...
        for (var i = 0; i < 255; i++)
        {
            page[27 + i] = 255; // ...each declaring 255 bytes that are not there
        }

        using var stream = new MemoryStream(page);
        MediaFile.ReadTags(stream, MediaFormat.OggVorbis);
        stream.Position = 0;

        var before = GC.GetAllocatedBytesForCurrentThread();
        var result = MediaFile.ReadTags(stream, MediaFormat.OggVorbis);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(result.IsSuccess);
        Assert.True(allocated < 1024 * 1024, $"Reading a {stream.Length} byte file allocated {allocated} bytes.");
    }
}
