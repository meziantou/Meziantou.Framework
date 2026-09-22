using System.Buffers.Binary;

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
        Assert.NotNull(tags.Duration);
        Assert.InRange(tags.Duration.Value.TotalSeconds, 0.9, 1.2);
    }

    [Fact]
    public void ReadTags_M4aWithFlacExtension_UsesMagicBytesAndParsesDuration()
    {
        var tempFile = Path.GetTempFileName() + ".flac";
        try
        {
            File.Copy(GetTestFilePath("basic.m4a"), tempFile, overwrite: true);

            var result = MediaFile.ReadTags(tempFile);
            Assert.True(result.IsSuccess);

            var tags = result.Value;
            Assert.Equal(MediaFormat.Mp4, tags.Format);
            Assert.NotNull(tags.Duration);
            Assert.InRange(tags.Duration.Value.TotalSeconds, 0.9, 1.2);
        }
        finally
        {
            File.Delete(tempFile);
        }
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
        Assert.Equal("All Fields Album Artist", tags.AlbumArtist);
        Assert.Equal("All Fields Composer", tags.Composer);
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
    public void WriteTags_ReplayGain_RoundTrip_OnM4aWithFlacExtension()
    {
        var tempFile = Path.GetTempFileName() + ".flac";
        try
        {
            File.Copy(GetTestFilePath("basic.m4a"), tempFile, overwrite: true);

            var newTags = new MediaTagInfo
            {
                ReplayGain = new ReplayGainInfo
                {
                    TrackGain = -11.19,
                },
            };

            var writeResult = MediaFile.WriteTags(tempFile, newTags);
            Assert.True(writeResult.IsSuccess);

            var readResult = MediaFile.ReadTags(tempFile);
            Assert.True(readResult.IsSuccess);
            Assert.Equal(MediaFormat.Mp4, readResult.Value.Format);
            Assert.NotNull(readResult.Value.ReplayGain);
            Assert.Equal(-11.19, readResult.Value.ReplayGain.Value.TrackGain);
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

    [Fact]
    public void ReadTags_TruncatedAtom_DoesNotReadZeroPaddedData()
    {
        var valueBytes = Encoding.UTF8.GetBytes("A");
        var dataPayload = new byte[8 + valueBytes.Length];
        BinaryPrimitives.WriteUInt32BigEndian(dataPayload, 1);
        valueBytes.CopyTo(dataPayload, 8);

        var titleAtomType = Encoding.Latin1.GetString([0xA9, (byte)'n', (byte)'a', (byte)'m']);
        var titleAtom = CreateAtom(titleAtomType, CreateAtom("data", dataPayload));
        var ilstAtom = CreateAtom("ilst", titleAtom);
        var metaPayload = new byte[4 + ilstAtom.Length];
        ilstAtom.CopyTo(metaPayload, 4);
        var mp4 = CreateAtom("moov", CreateAtom("udta", CreateAtom("meta", metaPayload)));
        BinaryPrimitives.WriteUInt32BigEndian(mp4, (uint)(mp4.Length + 4));

        using var stream = new MemoryStream(mp4);
        var result = MediaFile.ReadTags(stream, MediaFormat.Mp4);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Title);
    }

    [Fact]
    public void ReadTags_FreeformReplayGain_NullTerminatedText()
    {
        using var stream = new MemoryStream(CreateMp4WithFreeformTags([
            ("com.apple.iTunes\0", "REPLAYGAIN_TRACK_GAIN\0", "-6.25 dB\0", 1u),
            ("com.apple.iTunes", "REPLAYGAIN_ALBUM_PEAK", "0.987654\0", 1u),
        ]));

        var result = MediaFile.ReadTags(stream, MediaFormat.Mp4);
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.ReplayGain);
        Assert.Equal(-6.25, result.Value.ReplayGain.Value.TrackGain);
        Assert.Equal(0.987654, result.Value.ReplayGain.Value.AlbumPeak);
    }

    [Fact]
    public void ReadTags_FreeformReplayGain_Utf16Text()
    {
        using var stream = new MemoryStream(CreateMp4WithFreeformTags([
            ("com.apple.iTunes", "REPLAYGAIN_TRACK_GAIN", "-7.50 dB", 2u),
            ("com.apple.iTunes", "REPLAYGAIN_TRACK_PEAK", "0.998877", 2u),
        ]));

        var result = MediaFile.ReadTags(stream, MediaFormat.Mp4);
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.ReplayGain);
        Assert.Equal(-7.5, result.Value.ReplayGain.Value.TrackGain);
        Assert.Equal(0.998877, result.Value.ReplayGain.Value.TrackPeak);
    }

    private static byte[] CreateMp4WithFreeformTags((string Mean, string Name, string Value, uint DataType)[] freeformTags)
    {
        using var ilstPayload = new MemoryStream();
        foreach (var (mean, name, value, dataType) in freeformTags)
        {
            ilstPayload.Write(CreateFreeformAtom(mean, name, value, dataType));
        }

        var ilstAtom = CreateAtom("ilst", ilstPayload.ToArray());
        var metaPayload = new byte[4 + ilstAtom.Length];
        ilstAtom.CopyTo(metaPayload, 4); // Full box version/flags
        var metaAtom = CreateAtom("meta", metaPayload);
        var udtaAtom = CreateAtom("udta", metaAtom);
        return CreateAtom("moov", udtaAtom);
    }

    private static byte[] CreateFreeformAtom(string mean, string name, string value, uint dataType)
    {
        var meanAtom = CreateTextAtom("mean", mean);
        var nameAtom = CreateTextAtom("name", name);

        var valueBytes = dataType == 2 ? Encoding.BigEndianUnicode.GetBytes(value) : Encoding.UTF8.GetBytes(value);
        var dataPayload = new byte[8 + valueBytes.Length];
        BinaryPrimitives.WriteUInt32BigEndian(dataPayload, dataType);
        valueBytes.CopyTo(dataPayload, 8);
        var dataAtom = CreateAtom("data", dataPayload);

        var freeformPayload = new byte[meanAtom.Length + nameAtom.Length + dataAtom.Length];
        meanAtom.CopyTo(freeformPayload, 0);
        nameAtom.CopyTo(freeformPayload, meanAtom.Length);
        dataAtom.CopyTo(freeformPayload, meanAtom.Length + nameAtom.Length);
        return CreateAtom("----", freeformPayload);
    }

    private static byte[] CreateTextAtom(string atomType, string value)
    {
        var valueBytes = Encoding.UTF8.GetBytes(value);
        var payload = new byte[4 + valueBytes.Length]; // Full box version/flags + UTF-8 data
        valueBytes.CopyTo(payload, 4);
        return CreateAtom(atomType, payload);
    }

    private static byte[] CreateAtom(string atomType, byte[] payload)
    {
        var atom = new byte[8 + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(atom, (uint)atom.Length);
        Encoding.Latin1.GetBytes(atomType, atom.AsSpan(4, 4));
        payload.CopyTo(atom, 8);
        return atom;
    }

    [Fact]
    public void ReadTags_DeeplyNestedAtoms_ReturnsErrorInsteadOfOverflowingTheStack()
    {
        // An atom size of 0 means "extends to the end of the file", so every 8 bytes adds a nesting level
        var file = new MemoryStream();
        file.Write(CreateAtom("ftyp", new byte[8]));
        for (var i = 0; i < 100_000; i++)
        {
            file.Write([0, 0, 0, 0]);
            file.Write("moov"u8);
        }

        using var stream = new MemoryStream(file.ToArray());
        var result = MediaFile.ReadTags(stream, MediaFormat.Mp4);

        Assert.False(result.IsSuccess);
        Assert.Equal(MediaTagError.CorruptFile, result.Error);
    }

    [Fact]
    public void ReadTags_NestingUpToTheSupportedDepth_IsStillParsed()
    {
        // moov > udta > meta > ilst > ©nam > data is the deepest path a real file uses
        var result = MediaFile.ReadTags(GetTestFilePath("all_fields.m4a"));

        Assert.True(result.IsSuccess);
        Assert.Equal("All Fields Title", result.Value.Title);
    }

    [Fact]
    public void WriteTags_WritesTheMetaHandlerBox()
    {
        var tempFile = Path.GetTempFileName() + ".m4a";
        try
        {
            File.Copy(GetTestFilePath("basic.m4a"), tempFile, overwrite: true);

            var writeResult = MediaFile.WriteTags(tempFile, new MediaTagInfo { Title = "Handler Title" });
            Assert.True(writeResult.IsSuccess);

            var written = File.ReadAllBytes(tempFile);
            var meta = IndexOfAtomType(written, "meta");
            Assert.True(meta >= 0, "No meta atom in the written file.");

            // meta payload: version/flags(4), then the handler box, then ilst
            var handler = meta + 8;
            Assert.Equal(33u, BinaryPrimitives.ReadUInt32BigEndian(written.AsSpan(handler, 4)));
            Assert.Equal("hdlr", Encoding.Latin1.GetString(written, handler + 4, 4));
            Assert.Equal("mdir", Encoding.Latin1.GetString(written, handler + 16, 4));
            Assert.Equal("ilst", Encoding.Latin1.GetString(written, handler + 33 + 4, 4));

            // The tags are still readable through the library itself
            var readResult = MediaFile.ReadTags(tempFile);
            Assert.True(readResult.IsSuccess);
            Assert.Equal("Handler Title", readResult.Value.Title);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    private static int IndexOfAtomType(byte[] data, string atomType)
    {
        var needle = Encoding.Latin1.GetBytes(atomType);
        for (var i = 4; i + 4 <= data.Length; i++)
        {
            if (data.AsSpan(i, 4).SequenceEqual(needle))
                return i;
        }

        return -1;
    }

    [Fact]
    public void WriteTags_CustomFieldsAndMusicBrainzAndConductor_RoundTrip()
    {
        // Mp4Reader fills these from freeform atoms, so a writer that does not emit them silently strips them
        // from every file that goes through the read-modify-write loop.
        var tempFile = Path.GetTempFileName() + ".m4a";
        try
        {
            File.Copy(GetTestFilePath("basic.m4a"), tempFile, overwrite: true);

            var tags = new MediaTagInfo
            {
                Title = "Title",
                Conductor = "The Conductor",
                MusicBrainzTrackId = "track-id",
                MusicBrainzArtistId = "artist-id",
                MusicBrainzAlbumId = "album-id",
                MusicBrainzReleaseGroupId = "release-group-id",
                ReplayGain = new ReplayGainInfo { TrackGain = -3.21, TrackPeak = 0.5 },
            };
            tags.CustomFields["MY FIELD"] = "my value";

            Assert.True(MediaFile.WriteTags(tempFile, tags).IsSuccess);

            var read = MediaFile.ReadTags(tempFile).Value;
            Assert.Equal("The Conductor", read.Conductor);
            Assert.Equal("track-id", read.MusicBrainzTrackId);
            Assert.Equal("artist-id", read.MusicBrainzArtistId);
            Assert.Equal("album-id", read.MusicBrainzAlbumId);
            Assert.Equal("release-group-id", read.MusicBrainzReleaseGroupId);
            Assert.Equal("my value", read.CustomFields["MY FIELD"]);
            Assert.Equal(-3.21, read.ReplayGain?.TrackGain);
            Assert.Equal(0.5, read.ReplayGain?.TrackPeak);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void WriteTags_TotalsWithoutNumbers_AreKept()
    {
        var tempFile = Path.GetTempFileName() + ".m4a";
        try
        {
            File.Copy(GetTestFilePath("basic.m4a"), tempFile, overwrite: true);

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

    [Theory]
    [InlineData(70000, 0)]
    [InlineData(0, 70000)]
    public void WriteTags_ValueTooLargeForAnMp4Atom_IsRefused(int bpm, int trackNumber)
    {
        // The MP4 atoms store these in 16 bits. An unchecked cast silently wraps 70000 to 4464.
        var tempFile = Path.GetTempFileName() + ".m4a";
        try
        {
            File.Copy(GetTestFilePath("basic.m4a"), tempFile, overwrite: true);
            var original = File.ReadAllBytes(tempFile);

            var tags = new MediaTagInfo { Title = "Title" };
            if (bpm > 0)
                tags.Bpm = bpm;
            else
                tags.TrackNumber = trackNumber;

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
    public void WriteTags_PreservesIlstItemsTheWriterDoesNotProduce()
    {
        // Sort names, the encoder tool and the gapless flag are the user's data and must survive a tag edit.
        var tempFile = Path.GetTempFileName() + ".m4a";
        try
        {
            File.Copy(GetTestFilePath("basic.m4a"), tempFile, overwrite: true);
            var encoderAtomBefore = Encoding.Latin1.GetString(File.ReadAllBytes(tempFile)).Contains("\u00A9too", StringComparison.Ordinal);
            Assert.True(encoderAtomBefore, "The fixture no longer carries an encoder atom; the test cannot prove anything.");

            Assert.True(MediaFile.WriteTags(tempFile, new MediaTagInfo { Title = "Title" }).IsSuccess);

            // The encoder atom must survive the tag edit
            var after = Encoding.Latin1.GetString(File.ReadAllBytes(tempFile));
            Assert.Contains("\u00A9too", after);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ReadTags_WithArt_MatchesTheCoverFile()
    {
        var result = MediaFile.ReadTags(GetTestFilePath("with_art.m4a"));
        Assert.True(result.IsSuccess);

        var picture = Assert.Single(result.Value.Pictures);
        Assert.Equal("image/png", picture.MimeType);
        Assert.Equal(File.ReadAllBytes(GetTestFilePath("cover.png")), picture.Data);
    }

    [Fact]
    public void WriteTags_MoovBeforeMdat_CorrectsTheChunkOffsets()
    {
        // Sample chunk offsets are absolute file offsets. Resizing moov moves mdat, so leaving stco alone
        // makes the audio of every faststart file decode from the wrong place while the tags still read back.
        var tempFile = Path.GetTempFileName() + ".m4a";
        try
        {
            var faststart = BuildFaststartFile(File.ReadAllBytes(GetTestFilePath("basic.m4a")), out var originalMdatPayload);
            File.WriteAllBytes(tempFile, faststart);

            Assert.Equal(originalMdatPayload, ReadFirstChunkOffset(File.ReadAllBytes(tempFile)));

            Assert.True(MediaFile.WriteTags(tempFile, new MediaTagInfo { Title = "A considerably longer title than the original" }).IsSuccess);

            var written = File.ReadAllBytes(tempFile);
            var newMdatPayload = FindTopLevelAtom(written, "mdat") + 8;
            Assert.NotEqual(originalMdatPayload, newMdatPayload);
            Assert.Equal(newMdatPayload, ReadFirstChunkOffset(written));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    /// <summary>Rewrites a file so that moov precedes mdat, as a streaming-optimised encoder produces.</summary>
    private static byte[] BuildFaststartFile(byte[] source, out long mdatPayloadPosition)
    {
        var moovPosition = FindTopLevelAtom(source, "moov");
        var mdatPosition = FindTopLevelAtom(source, "mdat");
        var moovSize = ReadAtomSize(source, moovPosition);
        var mdatSize = ReadAtomSize(source, mdatPosition);

        using var output = new MemoryStream();
        var position = 0;
        while (position + 8 <= source.Length)
        {
            var size = ReadAtomSize(source, position);
            var type = Encoding.Latin1.GetString(source.AsSpan(position + 4, 4));
            if (type is not ("moov" or "mdat"))
                output.Write(source, position, (int)size);

            position += (int)size;
        }

        output.Write(source, (int)moovPosition, (int)moovSize);
        var mdatStart = (int)output.Length;
        output.Write(source, (int)mdatPosition, (int)mdatSize);

        var result = output.ToArray();
        mdatPayloadPosition = mdatStart + 8;

        // Point the chunk offset table at the audio in its new place
        var stco = IndexOf(result, "stco"u8);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(stco + 12), (uint)mdatPayloadPosition);
        return result;
    }

    private static long ReadFirstChunkOffset(byte[] file)
    {
        var stco = IndexOf(file, "stco"u8);
        Assert.True(stco >= 0, "The file has no chunk offset table.");
        return System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(stco + 12));
    }

    private static long FindTopLevelAtom(byte[] file, string type)
    {
        var position = 0;
        while (position + 8 <= file.Length)
        {
            var size = ReadAtomSize(file, position);
            if (Encoding.Latin1.GetString(file.AsSpan(position + 4, 4)) == type)
                return position;

            position += (int)size;
        }

        return -1;
    }

    private static long ReadAtomSize(byte[] file, long position)
        => System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan((int)position));

    private static int IndexOf(byte[] haystack, ReadOnlySpan<byte> needle) => haystack.AsSpan().IndexOf(needle);

    [Fact]
    public void ReadTags_GnreAtom_IsTheGenre()
    {
        // iTunes stores its built-in genres as an ID3v1 genre index plus one, not as text.
        var file = CreateMp4WithIlstItems(CreateGnreAtom(18));

        Assert.Equal("Rock", ReadMp4(file).Genre);
    }

    [Fact]
    public void ReadTags_GenreTextAndGnreAtom_TheTextWins()
    {
        var file = CreateMp4WithIlstItems(CreateGnreAtom(18), CreateAtom("©gen", CreateAtom("data", [0, 0, 0, 1, 0, 0, 0, 0, .. "Custom"u8])));

        Assert.Equal("Custom", ReadMp4(file).Genre);
    }

    [Fact]
    public void WriteTags_Genre_ReplacesTheGnreAtom()
    {
        var file = CreateMp4WithIlstItems(CreateGnreAtom(18));

        var written = WriteMp4(file, new MediaTagInfo { Genre = "Jazz" });
        Assert.Equal(-1, IndexOfAtomType(written, "gnre"));
        Assert.Equal("Jazz", ReadMp4(written).Genre);

        Assert.Null(ReadMp4(WriteMp4(file, new MediaTagInfo())).Genre);
    }

    [Fact]
    public void WriteTags_CustomFieldOutsideTheItunesNamespace_KeepsItsNamespace()
    {
        var file = CreateMp4WithFreeformTags([("org.example.tool", "Name", "value", 1)]);
        var tags = ReadMp4(file);
        Assert.Equal("value", tags.CustomFields["org.example.tool:Name"]);
        tags.CustomFields["Key:With colon"] = "other";

        var written = WriteMp4(file, tags);

        Assert.True(IndexOf(written, "org.example.tool"u8) >= 0);
        Assert.Equal(-1, IndexOf(written, "org.example.tool:Name"u8));
        var reread = ReadMp4(written);
        Assert.Equal("value", reread.CustomFields["org.example.tool:Name"]);
        Assert.Equal("other", reread.CustomFields["Key:With colon"]);
    }

    [Fact]
    public void WriteTags_SeveralPictures_RoundTripInOneCovrAtom()
    {
        var tags = new MediaTagInfo();
        tags.Pictures.Add(new MediaPicture { MimeType = "image/png", Data = [1, 2, 3] });
        tags.Pictures.Add(new MediaPicture { MimeType = "image/gif", Data = [4, 5, 6] });
        tags.Pictures.Add(new MediaPicture { MimeType = "image/bmp", Data = [7, 8, 9] });

        var written = WriteMp4(File.ReadAllBytes(GetTestFilePath("basic.m4a")), tags);

        var covr = IndexOfAtomType(written, "covr");
        Assert.True(covr >= 0);
        Assert.Equal(-1, IndexOfAtomType(written[(covr + 4)..], "covr"));

        var pictures = ReadMp4(written).Pictures;
        Assert.Equal(new string?[] { "image/png", "image/gif", "image/bmp" }, pictures.Select(picture => picture.MimeType).ToArray());
        Assert.Equal(new byte[] { 4, 5, 6 }, pictures[1].Data);
    }

    [Fact]
    public void ReadTags_FileWithALeadingId3Tag()
    {
        byte[] bytes = [(byte)'I', (byte)'D', (byte)'3', 3, 0, 0, 0, 0, 0, 0, .. File.ReadAllBytes(GetTestFilePath("basic.m4a"))];
        using var stream = new MemoryStream(bytes);

        var result = MediaFile.ReadTags(stream);

        Assert.True(result.IsSuccess);
        Assert.Equal(MediaFormat.Mp4, result.Value.Format);
        Assert.Equal("Test Title", result.Value.Title);
    }

    private static MediaTagInfo ReadMp4(byte[] file)
    {
        using var stream = new MemoryStream(file);
        var result = MediaFile.ReadTags(stream, MediaFormat.Mp4);
        Assert.True(result.IsSuccess, result.ErrorMessage);
        return result.Value;
    }

    private static byte[] WriteMp4(byte[] file, MediaTagInfo tags)
    {
        using var input = new MemoryStream(file);
        using var output = new MemoryStream();
        var result = MediaFile.WriteTags(input, output, tags, MediaFormat.Mp4);
        Assert.True(result.IsSuccess, result.ErrorMessage);
        return output.ToArray();
    }

    private static byte[] CreateGnreAtom(ushort genreId) => CreateAtom("gnre", CreateAtom("data", [0, 0, 0, 0, 0, 0, 0, 0, (byte)(genreId >> 8), (byte)genreId]));

    private static byte[] CreateMp4WithIlstItems(params byte[][] items)
    {
        var ilstAtom = CreateAtom("ilst", items.SelectMany(item => item).ToArray());
        var metaPayload = new byte[4 + ilstAtom.Length];
        ilstAtom.CopyTo(metaPayload, 4); // Full box version/flags
        return CreateAtom("moov", CreateAtom("udta", CreateAtom("meta", metaPayload)));
    }
}
