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
        Assert.Contains("日本語テスト", tags.Title);
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
        Assert.Equal(12, tags.TrackTotal);
        Assert.Equal("All Fields Album Artist", tags.AlbumArtist);
        Assert.Equal(2, tags.DiscNumber);
        Assert.Equal(3, tags.DiscTotal);
        Assert.Equal("All Fields Composer", tags.Composer);
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
        Assert.HasCountGreaterThan(100, tags.Title); // Should be very long
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

    [Fact]
    public void ReadTags_Mp3Frames_ComputesDuration()
    {
        const int FrameCount = 100;
        using var stream = new MemoryStream(CreateSyntheticMp3(FrameCount, includeId3v2Tag: true));

        var result = MediaFile.ReadTags(stream, MediaFormat.Mp3);
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.Duration);

        var expectedSeconds = FrameCount * (1152d / 44_100);
        Assert.InRange(result.Value.Duration.Value.TotalSeconds, expectedSeconds - 0.01, expectedSeconds + 0.01);
    }

    [Fact]
    public void ReadTags_Id3v2Tlen_ComputesDuration()
    {
        using var stream = new MemoryStream(CreateId3v24WithTextFrame("TLEN", "123456"));

        var result = MediaFile.ReadTags(stream, MediaFormat.Mp3);
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.Duration);
        Assert.Equal(TimeSpan.FromMilliseconds(123456), result.Value.Duration.Value);
    }

    private static byte[] CreateSyntheticMp3(int frameCount, bool includeId3v2Tag)
    {
        const int Mpeg1Layer3FrameLength = 417;
        var id3v2HeaderLength = includeId3v2Tag ? 10 : 0;
        var data = new byte[id3v2HeaderLength + (frameCount * Mpeg1Layer3FrameLength)];

        if (includeId3v2Tag)
        {
            data[0] = (byte)'I';
            data[1] = (byte)'D';
            data[2] = (byte)'3';
            data[3] = 4; // ID3v2.4
            data[4] = 0; // Revision
            data[5] = 0; // Flags
            // Tag size is 0, so bytes 6..9 are already 0.
        }

        var offset = id3v2HeaderLength;
        for (var i = 0; i < frameCount; i++)
        {
            data[offset + 0] = 0xFF;
            data[offset + 1] = 0xFB;
            data[offset + 2] = 0x90;
            data[offset + 3] = 0x00;
            offset += Mpeg1Layer3FrameLength;
        }

        return data;
    }

    private static byte[] CreateId3v24WithTextFrame(string frameId, string value)
    {
        var textBytes = System.Text.Encoding.ASCII.GetBytes(value);
        var frameDataLength = 1 + textBytes.Length;
        var frameLength = 10 + frameDataLength;
        var tagLength = 10 + frameLength;
        var result = new byte[tagLength];

        // Tag header
        result[0] = (byte)'I';
        result[1] = (byte)'D';
        result[2] = (byte)'3';
        result[3] = 4; // Version 2.4
        result[4] = 0;
        result[5] = 0;
        WriteSynchsafeInteger(result.AsSpan(6, 4), frameLength);

        // Frame header
        result[10] = (byte)frameId[0];
        result[11] = (byte)frameId[1];
        result[12] = (byte)frameId[2];
        result[13] = (byte)frameId[3];
        WriteSynchsafeInteger(result.AsSpan(14, 4), frameDataLength);
        result[18] = 0; // Frame flags
        result[19] = 0;

        // Frame payload
        result[20] = 0; // ISO-8859-1 encoding
        textBytes.CopyTo(result.AsSpan(21));

        return result;
    }

    private static void WriteSynchsafeInteger(Span<byte> destination, int value)
    {
        destination[0] = (byte)((value >> 21) & 0x7F);
        destination[1] = (byte)((value >> 14) & 0x7F);
        destination[2] = (byte)((value >> 7) & 0x7F);
        destination[3] = (byte)(value & 0x7F);
    }

    [Fact]
    public void ReadTags_TagSizeBeyondEndOfStream_DoesNotAllocateTheDeclaredSize()
    {
        // An ID3v2 header and nothing else, declaring the largest tag a synchsafe integer can express (256 MB)
        var header = new byte[10];
        header[0] = (byte)'I';
        header[1] = (byte)'D';
        header[2] = (byte)'3';
        header[3] = 4;
        WriteSynchsafeInteger(header.AsSpan(6, 4), 0x0FFF_FFFF);

        using var stream = new MemoryStream(header);

        // Read once so the measured read is not paying for JIT and first-use initialization
        MediaFile.ReadTags(stream, MediaFormat.Mp3);

        // Thread-scoped, because GC.GetTotalAllocatedBytes also counts what the other threads of the process allocate
        var before = GC.GetAllocatedBytesForCurrentThread();
        var result = MediaFile.ReadTags(stream, MediaFormat.Mp3);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Title);
        Assert.True(allocated < 1024 * 1024, $"Reading a {stream.Length} byte file allocated {allocated} bytes.");
    }

    [Fact]
    public void ReadTags_TagSizeMatchingTheStream_IsStillParsed()
    {
        var mp3 = CreateId3v24WithTextFrame("TIT2", "Bounded Title");

        using var stream = new MemoryStream(mp3);
        var result = MediaFile.ReadTags(stream, MediaFormat.Mp3);

        Assert.True(result.IsSuccess);
        Assert.Equal("Bounded Title", result.Value.Title);
    }

    [Fact]
    public void ReadTags_Utf16TextWithoutABom_KeepsTheFirstCharacter()
    {
        // The spec requires a BOM for encoding 0x01, but taggers do omit it. Skipping two bytes regardless
        // eats the first character of every title, artist and album in such a file.
        byte[] utf16LittleEndian = [0x48, 0x00, 0x65, 0x00, 0x6C, 0x00, 0x6C, 0x00, 0x6F, 0x00];
        var frame = new byte[1 + utf16LittleEndian.Length];
        frame[0] = 0x01;
        utf16LittleEndian.CopyTo(frame, 1);

        using var stream = new MemoryStream(BuildId3v24Tag(("TIT2", frame)));
        var result = MediaFile.ReadTags(stream, MediaFormat.Mp3);

        Assert.True(result.IsSuccess);
        Assert.Equal("Hello", result.Value.Title);
    }

    [Fact]
    public void ReadTags_Utf16TextWithABom_IsUnchanged()
    {
        byte[] withBom = [0xFF, 0xFE, 0x48, 0x00, 0x69, 0x00];
        var frame = new byte[1 + withBom.Length];
        frame[0] = 0x01;
        withBom.CopyTo(frame, 1);

        using var stream = new MemoryStream(BuildId3v24Tag(("TIT2", frame)));
        var result = MediaFile.ReadTags(stream, MediaFormat.Mp3);

        Assert.True(result.IsSuccess);
        Assert.Equal("Hi", result.Value.Title);
    }

    [Fact]
    public void ReadTags_Id3v22PictureFrame_ReadsThePicture()
    {
        // A v2.2 PIC frame stores a fixed 3-character image format, not the null-terminated MIME type of APIC.
        byte[] pictureData = [1, 2, 3, 4, 5, 6, 7, 8];
        var frame = new List<byte> { 0x00 };
        frame.AddRange("PNG"u8.ToArray());
        frame.Add((byte)MediaPictureType.FrontCover);
        frame.AddRange("desc"u8.ToArray());
        frame.Add(0x00);
        frame.AddRange(pictureData);

        using var stream = new MemoryStream(BuildId3v22Tag(("PIC", frame.ToArray())));
        var result = MediaFile.ReadTags(stream, MediaFormat.Mp3);

        Assert.True(result.IsSuccess);
        var picture = Assert.Single(result.Value.Pictures);
        Assert.Equal("image/png", picture.MimeType);
        Assert.Equal("desc", picture.Description);
        Assert.Equal(MediaPictureType.FrontCover, picture.PictureType);
        Assert.Equal(pictureData, picture.Data);
    }

    [Fact]
    public void WriteTags_TotalsWithoutNumbers_AreKept()
    {
        // The number and the total share one frame; writing the frame only when the number is set loses a
        // total that was supplied on its own.
        var tempFile = Path.GetTempFileName() + ".mp3";
        try
        {
            File.Copy(GetTestFilePath("basic.mp3"), tempFile, overwrite: true);

            Assert.True(MediaFile.WriteTags(tempFile, new MediaTagInfo { Title = "Title", TrackTotal = 12, DiscTotal = 3 }).IsSuccess);

            var tags = MediaFile.ReadTags(tempFile).Value;
            Assert.Equal(12, tags.TrackTotal);
            Assert.Equal(3, tags.DiscTotal);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void WriteTags_Duration_IsStoredInTheTag()
    {
        // Id3v2Reader parses TLEN, so not writing it drops an explicit duration on a read-modify-write.
        var tempFile = Path.GetTempFileName() + ".mp3";
        try
        {
            File.Copy(GetTestFilePath("basic.mp3"), tempFile, overwrite: true);

            var duration = TimeSpan.FromMilliseconds(123456);
            Assert.True(MediaFile.WriteTags(tempFile, new MediaTagInfo { Title = "Title", Duration = duration }).IsSuccess);

            Assert.Equal(duration, MediaFile.ReadTags(tempFile).Value.Duration);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void WriteTags_ReplayGainAndMusicBrainzAndCustomFields_RoundTrip()
    {
        var tempFile = Path.GetTempFileName() + ".mp3";
        try
        {
            File.Copy(GetTestFilePath("basic.mp3"), tempFile, overwrite: true);

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
    public void ReadTags_ReplayGainWithACommaDecimalSeparator_IsParsed()
    {
        // Taggers running under a European locale write the value with a comma.
        var description = "REPLAYGAIN_TRACK_GAIN"u8.ToArray();
        var value = "-3,21 dB"u8.ToArray();
        var frame = new byte[1 + description.Length + 1 + value.Length];
        frame[0] = 0x03; // UTF-8
        description.CopyTo(frame, 1);
        frame[1 + description.Length] = 0;
        value.CopyTo(frame, 1 + description.Length + 1);

        using var stream = new MemoryStream(BuildId3v24Tag(("TXXX", frame)));
        var result = MediaFile.ReadTags(stream, MediaFormat.Mp3);

        Assert.True(result.IsSuccess);
        Assert.Equal(-3.21, result.Value.ReplayGain?.TrackGain);
    }

    [Fact]
    public void ReadTags_WithArt_MatchesTheCoverFile()
    {
        var result = MediaFile.ReadTags(GetTestFilePath("with_art.mp3"));
        Assert.True(result.IsSuccess);

        var picture = Assert.Single(result.Value.Pictures);
        Assert.Equal("image/png", picture.MimeType);
        Assert.Equal(File.ReadAllBytes(GetTestFilePath("cover.png")), picture.Data);
    }

    private static byte[] BuildId3v24Tag(params (string FrameId, byte[] Body)[] frames)
    {
        var frameBytes = new List<byte>();
        foreach (var (frameId, body) in frames)
        {
            frameBytes.AddRange(Encoding.ASCII.GetBytes(frameId));
            frameBytes.AddRange(EncodeSynchsafe(body.Length));
            frameBytes.AddRange([0, 0]);
            frameBytes.AddRange(body);
        }

        var tag = new List<byte> { (byte)'I', (byte)'D', (byte)'3', 4, 0, 0 };
        tag.AddRange(EncodeSynchsafe(frameBytes.Count));
        tag.AddRange(frameBytes);
        tag.AddRange([0xFF, 0xFB, 0x90, 0x00]); // A frame header so the file looks like MP3 audio
        return tag.ToArray();
    }

    private static byte[] BuildId3v22Tag(params (string FrameId, byte[] Body)[] frames)
    {
        var frameBytes = new List<byte>();
        foreach (var (frameId, body) in frames)
        {
            frameBytes.AddRange(Encoding.ASCII.GetBytes(frameId));
            frameBytes.AddRange([(byte)((body.Length >> 16) & 0xFF), (byte)((body.Length >> 8) & 0xFF), (byte)(body.Length & 0xFF)]);
            frameBytes.AddRange(body);
        }

        var tag = new List<byte> { (byte)'I', (byte)'D', (byte)'3', 2, 0, 0 };
        tag.AddRange(EncodeSynchsafe(frameBytes.Count));
        tag.AddRange(frameBytes);
        tag.AddRange([0xFF, 0xFB, 0x90, 0x00]);
        return tag.ToArray();
    }

    private static byte[] EncodeSynchsafe(int value)
        => [(byte)((value >> 21) & 0x7F), (byte)((value >> 14) & 0x7F), (byte)((value >> 7) & 0x7F), (byte)(value & 0x7F)];
}
