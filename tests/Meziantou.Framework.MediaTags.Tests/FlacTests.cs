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
        Assert.HasCountGreaterThan(100, result.Value.Title);
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
            stream.ReadExactly(magic);
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
    public void ReadTags_Duration_IsPopulated()
    {
        var result = MediaFile.ReadTags(GetTestFilePath("basic.flac"));
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.Duration);
        Assert.True(result.Value.Duration!.Value.TotalSeconds is > 0.9 and < 1.1);
    }

    [Fact]
    public void ReadTags_WithLeadingId3Tag_UsesFlacReader()
    {
        var tempFile = Path.GetTempFileName() + ".flac";
        try
        {
            using (var output = File.Create(tempFile))
            {
                output.Write("ID3"u8);
                output.Write([0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]);

                using var input = File.OpenRead(GetTestFilePath("basic.flac"));
                input.CopyTo(output);
            }

            var result = MediaFile.ReadTags(tempFile);
            Assert.True(result.IsSuccess);
            Assert.Equal(MediaFormat.Flac, result.Value.Format);
            Assert.NotNull(result.Value.Duration);
            Assert.True(result.Value.Duration!.Value.TotalSeconds is > 0.9 and < 1.1);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ReadTags_WithLeadingId3TagAndFooter_UsesFlacReader()
    {
        var tempFile = Path.GetTempFileName() + ".flac";
        try
        {
            using (var output = File.Create(tempFile))
            {
                output.Write("ID3"u8);
                output.Write([0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]);

                output.Write([(byte)'3', (byte)'D', (byte)'I', 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]);

                using var input = File.OpenRead(GetTestFilePath("basic.flac"));
                input.CopyTo(output);
            }

            var result = MediaFile.ReadTags(tempFile);
            Assert.True(result.IsSuccess);
            Assert.Equal(MediaFormat.Flac, result.Value.Format);
            Assert.NotNull(result.Value.Duration);
            Assert.True(result.Value.Duration!.Value.TotalSeconds is > 0.9 and < 1.1);
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

    [Fact]
    public void WriteTags_FileWithLeadingId3Tag_KeepsTheAudioStream()
    {
        var tempFile = Path.GetTempFileName() + ".flac";
        try
        {
            var original = File.ReadAllBytes(GetTestFilePath("basic.flac"));
            File.WriteAllBytes(tempFile, [.. CreateId3v2Tag(paddingSize: 100), .. original]);

            Assert.Equal(MediaFormat.Flac, MediaFile.DetectFormat(tempFile));

            var writeResult = MediaFile.WriteTags(tempFile, new MediaTagInfo { Title = "New Title" });
            Assert.True(writeResult.IsSuccess);

            var written = File.ReadAllBytes(tempFile);

            // The ID3v2 tag is preserved and the FLAC signature still follows it
            const int TagLength = 110;
            Assert.Equal("ID3"u8.ToArray(), written.AsSpan(0, 3).ToArray());
            Assert.Equal("fLaC"u8.ToArray(), written.AsSpan(TagLength, 4).ToArray());

            // The audio frames are byte-for-byte what they were
            var originalAudio = original.AsSpan(FindAudioStart(original, streamStart: 0)).ToArray();
            var writtenAudio = written.AsSpan(FindAudioStart(written, streamStart: TagLength)).ToArray();
            Assert.Equal(originalAudio, writtenAudio);

            var readResult = MediaFile.ReadTags(tempFile);
            Assert.True(readResult.IsSuccess);
            Assert.Equal("New Title", readResult.Value.Title);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void WriteTags_NotAFlacFile_ReturnsError()
    {
        using var input = new MemoryStream([0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07]);
        using var output = new MemoryStream();

        var result = MediaFile.WriteTags(input, output, new MediaTagInfo { Title = "Title" }, MediaFormat.Flac);

        Assert.False(result.IsSuccess);
        Assert.Equal(MediaTagError.CorruptFile, result.Error);
        Assert.Equal(0, output.Length);
    }

    private static byte[] CreateId3v2Tag(int paddingSize)
    {
        var tag = new byte[10 + paddingSize];
        tag[0] = (byte)'I';
        tag[1] = (byte)'D';
        tag[2] = (byte)'3';
        tag[3] = 4;
        tag[9] = (byte)paddingSize; // Synchsafe size, small enough to fit in the last byte
        return tag;
    }

    private static int FindAudioStart(byte[] flac, int streamStart)
    {
        // Walk the metadata blocks from just after the "fLaC" signature to the first audio frame
        var offset = streamStart + 4;
        while (true)
        {
            var isLast = (flac[offset] & 0x80) != 0;
            var blockSize = (flac[offset + 1] << 16) | (flac[offset + 2] << 8) | flac[offset + 3];
            offset += 4 + blockSize;
            if (isLast)
                return offset;
        }
    }

    [Fact]
    public void WriteTags_PictureTooLargeForAMetadataBlock_IsRefused()
    {
        // A metadata block header has 24 bits for the size. Truncating it produces a file whose block chain
        // walks into the picture bytes, and every decoder then reads metadata as audio.
        var tempFile = Path.GetTempFileName() + ".flac";
        try
        {
            File.Copy(GetTestFilePath("basic.flac"), tempFile, overwrite: true);
            var original = File.ReadAllBytes(tempFile);

            var tags = new MediaTagInfo { Title = "Title" };
            tags.Pictures.Add(new MediaPicture
            {
                PictureType = MediaPictureType.FrontCover,
                MimeType = "image/jpeg",
                Data = new byte[0x100_0000], // 16 MiB, one byte past what the header can express
            });

            var result = MediaFile.WriteTags(tempFile, tags);

            Assert.False(result.IsSuccess);
            Assert.Equal(MediaTagError.InvalidTagData, result.Error);
            Assert.Equal(original, File.ReadAllBytes(tempFile));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ReadTags_WithArt_MatchesTheCoverFile()
    {
        var result = MediaFile.ReadTags(GetTestFilePath("with_art.flac"));
        Assert.True(result.IsSuccess);

        var picture = Assert.Single(result.Value.Pictures);
        Assert.Equal("image/png", picture.MimeType);
        Assert.Equal(File.ReadAllBytes(GetTestFilePath("cover.png")), picture.Data);
    }

    [Fact]
    public void WriteTags_ReplayGainAndMusicBrainzAndCustomFields_RoundTrip()
    {
        var tempFile = Path.GetTempFileName() + ".flac";
        try
        {
            File.Copy(GetTestFilePath("basic.flac"), tempFile, overwrite: true);

            var tags = new MediaTagInfo
            {
                Title = "Title",
                ReplayGain = new ReplayGainInfo { TrackGain = -3.21, TrackPeak = 0.5, AlbumGain = -2.5, AlbumPeak = 0.75 },
                MusicBrainzTrackId = "track-id",
                MusicBrainzArtistId = "artist-id",
                MusicBrainzAlbumId = "album-id",
                MusicBrainzReleaseGroupId = "release-group-id",
            };
            tags.CustomFields["MY FIELD"] = "my value";

            Assert.True(MediaFile.WriteTags(tempFile, tags).IsSuccess);

            var read = MediaFile.ReadTags(tempFile).Value;
            Assert.Equal(-3.21, read.ReplayGain?.TrackGain);
            Assert.Equal(0.5, read.ReplayGain?.TrackPeak);
            Assert.Equal(-2.5, read.ReplayGain?.AlbumGain);
            Assert.Equal(0.75, read.ReplayGain?.AlbumPeak);
            Assert.Equal("track-id", read.MusicBrainzTrackId);
            Assert.Equal("artist-id", read.MusicBrainzArtistId);
            Assert.Equal("album-id", read.MusicBrainzAlbumId);
            Assert.Equal("release-group-id", read.MusicBrainzReleaseGroupId);
            Assert.Equal("my value", read.CustomFields["MY FIELD"]);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void WriteTags_MetadataBlockRunningPastTheEndOfTheFile_IsRefused()
    {
        var tempFile = Path.GetTempFileName() + ".flac";
        try
        {
            var bytes = File.ReadAllBytes(GetTestFilePath("basic.flac"));

            // Overstate the size of the first metadata block
            bytes[5] = 0x7F;
            bytes[6] = 0xFF;
            bytes[7] = 0xFF;
            File.WriteAllBytes(tempFile, bytes);

            var result = MediaFile.WriteTags(tempFile, new MediaTagInfo { Title = "Title" });

            Assert.False(result.IsSuccess);
            Assert.Equal(MediaTagError.CorruptFile, result.Error);
            Assert.Equal(bytes, File.ReadAllBytes(tempFile));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
