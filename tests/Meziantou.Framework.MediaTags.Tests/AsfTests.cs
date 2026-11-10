using System.Buffers.Binary;

namespace Meziantou.Framework.MediaTags.Tests;

public sealed class AsfTests
{
    private static string GetTestFilePath(string fileName) => Path.Combine("TestFiles", fileName);

    private static byte[] ReadTestFile(string fileName) => File.ReadAllBytes(GetTestFilePath(fileName));

    private static MediaTagInfo Read(byte[] file)
    {
        var result = MediaFile.ReadTags(new MemoryStream(file, writable: false), MediaFormat.Asf);
        Assert.True(result.IsSuccess, result.ErrorMessage);
        return result.Value;
    }

    private static byte[] Write(byte[] file, MediaTagInfo tags)
    {
        using var output = new MemoryStream();
        var result = MediaFile.WriteTags(new MemoryStream(file, writable: false), output, tags, MediaFormat.Asf);
        Assert.True(result.IsSuccess, result.ErrorMessage);
        return output.ToArray();
    }

    [Theory]
    [InlineData("basic.wma")]
    [InlineData("basic.wmv")]
    public void DetectFormat(string fileName)
    {
        Assert.Equal(MediaFormat.Asf, MediaFile.DetectFormat(GetTestFilePath(fileName)));
    }

    [Fact]
    public void ReadTags_BasicWma()
    {
        var result = MediaFile.ReadTags(GetTestFilePath("basic.wma"));
        Assert.True(result.IsSuccess);

        var tags = result.Value;
        Assert.Equal(MediaFormat.Asf, tags.Format);
        Assert.Equal("Test Title", tags.Title);
        Assert.Equal("Test Artist", tags.Artist);
        Assert.Equal("Test Album", tags.Album);
        Assert.Equal(2024, tags.Year);
        Assert.Equal(3, tags.TrackNumber);
        Assert.Equal("Rock", tags.Genre);
        Assert.NotNull(tags.Duration);
        Assert.Equal(1.0, tags.Duration.Value.TotalSeconds, 0.1);
        Assert.Null(tags.VideoWidth);
    }

    [Fact]
    public void ReadTags_AllFieldsWma()
    {
        var tags = Read(ReadTestFile("all_fields.wma"));

        Assert.Equal("All Fields Title", tags.Title);
        Assert.Equal("All Fields Artist", tags.Artist);
        Assert.Equal("All Fields Album", tags.Album);
        Assert.Equal("All Fields Album Artist", tags.AlbumArtist);
        Assert.Equal("Electronic", tags.Genre);
        Assert.Equal(2023, tags.Year);
        Assert.Equal(5, tags.TrackNumber);
        Assert.Equal(1, tags.DiscNumber);
        Assert.Equal(2, tags.DiscTotal);
        Assert.Equal("Test Composer", tags.Composer);
        Assert.Equal("Test Conductor", tags.Conductor);
        Assert.Equal("2023 Test", tags.Copyright);
        Assert.Equal("Test Comment", tags.Comment);
        Assert.Equal("USRC17607839", tags.Isrc);
        Assert.Equal(128, tags.Bpm);
        Assert.Equal("Line 1", tags.Lyrics);
        Assert.Equal(-6.5, tags.ReplayGain?.TrackGain);
        Assert.Equal("f1c4a1d2-9d4b-4e0a-8f3e-1c2b3a4d5e6f", tags.MusicBrainzTrackId);
        Assert.Equal("Custom Value", tags.CustomFields["CUSTOM_FIELD"]);
    }

    [Fact]
    public void ReadTags_UnicodeWma()
    {
        var tags = Read(ReadTestFile("unicode.wma"));

        Assert.Equal("日本語テスト", tags.Title);
        Assert.Equal("Тест Артист", tags.Artist);
        Assert.Equal("Tëst Àlbüm", tags.Album);
    }

    [Fact]
    public void ReadTags_Wmv_ReadsTheVideoSize()
    {
        var tags = Read(ReadTestFile("basic.wmv"));

        Assert.Equal("Video Title", tags.Title);
        Assert.Equal("Video Artist", tags.Artist);
        Assert.Equal(64, tags.VideoWidth);
        Assert.Equal(48, tags.VideoHeight);
    }

    [Theory]
    [InlineData("basic.wma")]
    [InlineData("empty.wma")]
    [InlineData("basic.wmv")]
    public void WriteTags_RoundTrip(string fileName)
    {
        var tags = new MediaTagInfo
        {
            Title = "New Title",
            Artist = "New Artist",
            Album = "New Album",
            AlbumArtist = "New Album Artist",
            Genre = "Jazz",
            Year = 1999,
            TrackNumber = 4,
            TrackTotal = 10,
            DiscTotal = 3,
            Comment = "New Comment",
            Lyrics = "Line 1\nLine 2",
            Isrc = "USRC17607839",
            Composer = "New Composer",
            Conductor = "New Conductor",
            Copyright = "1999 New",
            Bpm = 98,
            IsCompilation = true,
            ReplayGain = new ReplayGainInfo { TrackGain = -3.21, TrackPeak = 0.5 },
            MusicBrainzArtistId = "a1b2c3d4-e5f6-4a7b-8c9d-0e1f2a3b4c5d",
        };
        tags.CustomFields["MOOD"] = "Calm";
        tags.Pictures.Add(new MediaPicture { PictureType = MediaPictureType.FrontCover, MimeType = "image/png", Description = "Front", Data = ReadTestFile("cover.png") });

        var original = ReadTestFile(fileName);
        var written = Write(original, tags);
        var readBack = Read(written);

        Assert.Equal("New Title", readBack.Title);
        Assert.Equal("New Artist", readBack.Artist);
        Assert.Equal("New Album", readBack.Album);
        Assert.Equal("New Album Artist", readBack.AlbumArtist);
        Assert.Equal("Jazz", readBack.Genre);
        Assert.Equal(1999, readBack.Year);
        Assert.Equal(4, readBack.TrackNumber);
        Assert.Equal(10, readBack.TrackTotal);
        Assert.Null(readBack.DiscNumber);
        Assert.Equal(3, readBack.DiscTotal);
        Assert.Equal("New Comment", readBack.Comment);
        Assert.Equal("Line 1\nLine 2", readBack.Lyrics);
        Assert.Equal("USRC17607839", readBack.Isrc);
        Assert.Equal("New Composer", readBack.Composer);
        Assert.Equal("New Conductor", readBack.Conductor);
        Assert.Equal("1999 New", readBack.Copyright);
        Assert.Equal(98, readBack.Bpm);
        Assert.True(readBack.IsCompilation);
        Assert.Equal(-3.21, readBack.ReplayGain?.TrackGain);
        Assert.Equal(0.5, readBack.ReplayGain?.TrackPeak);
        Assert.Equal("a1b2c3d4-e5f6-4a7b-8c9d-0e1f2a3b4c5d", readBack.MusicBrainzArtistId);
        Assert.Equal("Calm", readBack.CustomFields["MOOD"]);

        var picture = Assert.Single(readBack.Pictures);
        Assert.Equal(MediaPictureType.FrontCover, picture.PictureType);
        Assert.Equal("image/png", picture.MimeType);
        Assert.Equal("Front", picture.Description);
        Assert.Equal(ReadTestFile("cover.png"), picture.Data);

        var originalTags = Read(original);
        Assert.Equal(originalTags.Duration, readBack.Duration);
        Assert.Equal(originalTags.VideoWidth, readBack.VideoWidth);

        // The file properties object stores the size of the file, at offset 40 of the object
        var filePropertiesOffset = written.AsSpan().IndexOf(new Guid("8CABDCA1-A947-11CF-8EE4-00C00C205365").ToByteArray());
        Assert.Equal((ulong)written.Length, BinaryPrimitives.ReadUInt64LittleEndian(written.AsSpan(filePropertiesOffset + 40)));

        // The data and index objects are copied unchanged
        var headerSize = (int)BinaryPrimitives.ReadUInt64LittleEndian(original.AsSpan(16));
        var writtenHeaderSize = (int)BinaryPrimitives.ReadUInt64LittleEndian(written.AsSpan(16));
        Assert.Equal(original.AsSpan(headerSize).ToArray(), written.AsSpan(writtenHeaderSize).ToArray());
    }

    [Fact]
    public void WriteTags_LargeValues()
    {
        // Longer than the 16-bit lengths of the content description and extended content description objects
        var tags = new MediaTagInfo { Title = new string('t', 40_000), Lyrics = new string('l', 40_000) };
        tags.Pictures.Add(new MediaPicture { PictureType = MediaPictureType.BackCover, MimeType = "image/jpeg", Data = new byte[100_000] });

        var readBack = Read(Write(ReadTestFile("basic.wma"), tags));

        Assert.Equal(tags.Title, readBack.Title);
        Assert.Equal(tags.Lyrics, readBack.Lyrics);
        Assert.HasCount(100_000, Assert.Single(readBack.Pictures).Data);
    }

    [Fact]
    public void WriteTags_NativeFields_RoundTrip()
    {
        var tags = new MediaTagInfo
        {
            Title = "Title",
            NativeFields =
            [
                new MediaNativeField { TagType = MediaTagType.Asf, Id = "WM/EncodingTime", Data = BitConverter.GetBytes(132_000_000_000_000_000L), Flags = 4 },
                new MediaNativeField { TagType = MediaTagType.Asf, Id = "WM/MediaIsLive", Data = [1, 0, 0, 0], Flags = 2 },
                new MediaNativeField { TagType = MediaTagType.Asf, Id = "WM/WMCollectionID", Data = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef").ToByteArray(), Flags = 6 },
                new MediaNativeField { TagType = MediaTagType.Asf, Id = "Binary", Data = [1, 2, 3], Flags = 1 },
            ],
        };

        var written = Write(ReadTestFile("basic.wma"), tags);
        var readBack = Read(written);

        Assert.Equal("Title", readBack.Title);
        Assert.Equal(tags.NativeFields.Select(field => (field.Id, field.Flags, Convert.ToHexString(field.Data))), readBack.NativeFields!.Select(field => (field.Id, field.Flags, Convert.ToHexString(field.Data))));

        // Writing a new MediaTagInfo keeps them
        var rewritten = Read(Write(written, new MediaTagInfo { Artist = "Artist" }));
        Assert.Null(rewritten.Title);
        Assert.HasCount(4, rewritten.NativeFields!);

        // Removing the tags removes them
        using var output = new MemoryStream();
        Assert.True(MediaFile.RemoveTags(new MemoryStream(written, writable: false), output, MediaFormat.Asf).IsSuccess);
        Assert.Empty(Read(output.ToArray()).NativeFields!);
    }

    [Theory]
    [InlineData("Text", 0, 3)]
    [InlineData("WM/Picture", 1, 3)]
    [InlineData("WM/TrackNumber", 3, 4)]
    [InlineData("Number", 3, 3)]
    [InlineData("Bool", 2, 2)]
    [InlineData("Unknown", 7, 3)]
    public void WriteTags_InvalidNativeField_Fails(string name, int dataType, int length)
    {
        var tags = new MediaTagInfo { NativeFields = [new MediaNativeField { TagType = MediaTagType.Asf, Id = name, Data = new byte[length], Flags = dataType }] };

        var result = MediaFile.WriteTags(new MemoryStream(ReadTestFile("basic.wma"), writable: false), new MemoryStream(), tags, MediaFormat.Asf);

        Assert.False(result.IsSuccess);
        Assert.Equal(MediaTagError.InvalidTagData, result.Error);
    }

    [Fact]
    public void RemoveTags_RemovesEveryAttribute()
    {
        var original = ReadTestFile("all_fields.wma");
        using var output = new MemoryStream();
        Assert.True(MediaFile.RemoveTags(new MemoryStream(original, writable: false), output, MediaFormat.Asf).IsSuccess);

        var tags = Read(output.ToArray());
        Assert.Null(tags.Title);
        Assert.Null(tags.Album);
        Assert.Empty(tags.CustomFields);
        Assert.Empty(tags.NativeFields!);
        Assert.Equal(Read(original).Duration, tags.Duration);
    }

    [Fact]
    public void WriteTags_KeepsTheAttributesOfAStream()
    {
        // A metadata library object with a stream-level attribute and a file-level one
        var streamRecord = CreateMetadataRecord(language: 0, stream: 1, "WM/StreamName", "Audio");
        var fileRecord = CreateMetadataRecord(language: 0, stream: 0, "WM/AlbumTitle", "Library Album");
        var file = AddToHeaderExtension(ReadTestFile("basic.wma"), CreateObject(new Guid("44231C94-9498-49D1-A141-1D134E457054"), [2, 0, .. streamRecord, .. fileRecord]));
        Assert.Equal("Library Album", Read(file).Album);

        var written = Write(file, new MediaTagInfo { Title = "Title" });
        Assert.True(written.AsSpan().IndexOf(streamRecord) >= 0);
        Assert.Null(Read(written).Album);

        using var output = new MemoryStream();
        Assert.True(MediaFile.RemoveTags(new MemoryStream(written, writable: false), output, MediaFormat.Asf).IsSuccess);
        Assert.True(output.ToArray().AsSpan().IndexOf(streamRecord) >= 0);
    }

    [Fact]
    public void ReadTags_MalformedAttributes_CanBeWrittenBack()
    {
        // A 32-bit integer stored in 2 bytes, and an unknown data type
        var file = AddToHeaderExtension(ReadTestFile("basic.wma"), CreateObject(new Guid("44231C94-9498-49D1-A141-1D134E457054"),
            [2, 0, .. CreateMetadataRecord(0, 0, "Short", [1, 2], dataType: 3), .. CreateMetadataRecord(0, 0, "Unknown", [1, 2, 3], dataType: 99)]));

        var tags = Read(file);
        Assert.Empty(tags.NativeFields!);
        Assert.Equal("Test Title", Read(Write(file, tags)).Title);
    }

    private static byte[] CreateMetadataRecord(ushort language, ushort stream, string name, string value)
        => CreateMetadataRecord(language, stream, name, Encoding.Unicode.GetBytes(value + "\0"), dataType: 0);

    private static byte[] CreateMetadataRecord(ushort language, ushort stream, string name, byte[] valueBytes, ushort dataType)
    {
        var nameBytes = Encoding.Unicode.GetBytes(name + "\0");
        var record = new byte[12 + nameBytes.Length + valueBytes.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(record, language);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(2), stream);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(4), (ushort)nameBytes.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(6), dataType);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(8), (uint)valueBytes.Length);
        nameBytes.CopyTo(record, 12);
        valueBytes.CopyTo(record, 12 + nameBytes.Length);
        return record;
    }

    private static byte[] CreateObject(Guid id, byte[] data)
    {
        var result = new byte[24 + data.Length];
        id.TryWriteBytes(result);
        BinaryPrimitives.WriteUInt64LittleEndian(result.AsSpan(16), (ulong)result.Length);
        data.CopyTo(result, 24);
        return result;
    }

    /// <summary>Appends an object to the header extension object, and updates the sizes of the header extension and the header.</summary>
    private static byte[] AddToHeaderExtension(byte[] file, byte[] extensionObject)
    {
        var headerExtension = file.AsSpan().IndexOf(new Guid("5FBF03B5-A92E-11CF-8EE3-00C00C205365").ToByteArray());
        var headerExtensionSize = (int)BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(headerExtension + 16));
        var insertAt = headerExtension + headerExtensionSize;
        var result = (byte[])[.. file.AsSpan(0, insertAt), .. extensionObject, .. file.AsSpan(insertAt)];
        BinaryPrimitives.WriteUInt64LittleEndian(result.AsSpan(16), BinaryPrimitives.ReadUInt64LittleEndian(result.AsSpan(16)) + (ulong)extensionObject.Length);
        BinaryPrimitives.WriteUInt64LittleEndian(result.AsSpan(headerExtension + 16), (ulong)(headerExtensionSize + extensionObject.Length));
        var dataSize = headerExtension + 24 + 18;
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(dataSize), BinaryPrimitives.ReadUInt32LittleEndian(result.AsSpan(dataSize)) + (uint)extensionObject.Length);
        return result;
    }

    [Fact]
    public void ReadTags_NotAnAsfFile_Fails()
    {
        var result = MediaFile.ReadTags(new MemoryStream(ReadTestFile("basic.mp3")), MediaFormat.Asf);

        Assert.False(result.IsSuccess);
        Assert.Equal(MediaTagError.CorruptFile, result.Error);
    }
}
