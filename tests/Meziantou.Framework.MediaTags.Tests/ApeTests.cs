using System.Buffers.Binary;

namespace Meziantou.Framework.MediaTags.Tests;

public sealed class ApeTests
{
    private static string GetTestFilePath(string fileName) => Path.Combine("TestFiles", fileName);

    private static byte[] ReadTestFile(string fileName) => File.ReadAllBytes(GetTestFilePath(fileName));

    private static MediaTagInfo Read(byte[] file, MediaFormat? format = null)
    {
        var result = MediaFile.ReadTags(new MemoryStream(file, writable: false), format);
        Assert.True(result.IsSuccess, result.ErrorMessage);
        return result.Value;
    }

    private static byte[] Write(byte[] file, MediaTagInfo tags, MediaFormat format)
    {
        using var output = new MemoryStream();
        var result = MediaFile.WriteTags(new MemoryStream(file, writable: false), output, tags, format);
        Assert.True(result.IsSuccess, result.ErrorMessage);
        return output.ToArray();
    }

    [Theory]
    [InlineData("basic.ape", MediaFormat.MonkeysAudio)]
    [InlineData("empty.ape", MediaFormat.MonkeysAudio)]
    [InlineData("basic.wv", MediaFormat.WavPack)]
    [InlineData("basic.mpc", MediaFormat.Musepack)]
    [InlineData("sv7.mpc", MediaFormat.Musepack)]
    public void DetectFormat(string fileName, MediaFormat expected)
    {
        Assert.Equal(expected, MediaFile.DetectFormat(GetTestFilePath(fileName)));
    }

    [Theory]
    [InlineData("basic.ape", 1.5)]
    [InlineData("sv7.mpc", 3.0041)]
    public void ReadTags_HandcraftedFiles(string fileName, double durationSeconds)
    {
        var result = MediaFile.ReadTags(GetTestFilePath(fileName));
        Assert.True(result.IsSuccess);

        var tags = result.Value;
        Assert.Equal("Test Title", tags.Title);
        Assert.Equal("Test Artist", tags.Artist);
        Assert.Equal("Test Album", tags.Album);
        Assert.Equal("Test Album Artist", tags.AlbumArtist);
        Assert.Equal(2024, tags.Year);
        Assert.Equal(3, tags.TrackNumber);
        Assert.Equal(12, tags.TrackTotal);
        Assert.Equal(1, tags.DiscNumber);
        Assert.Equal(2, tags.DiscTotal);
        Assert.Equal("Rock", tags.Genre);
        Assert.Equal("Test Comment", tags.Comment);
        Assert.Equal(-6.5, tags.ReplayGain?.TrackGain);
        Assert.Equal("f1c4a1d2-9d4b-4e0a-8f3e-1c2b3a4d5e6f", tags.MusicBrainzTrackId);
        Assert.Equal("Custom Value", tags.CustomFields["CUSTOM_FIELD"]);

        var picture = Assert.Single(tags.Pictures);
        Assert.Equal(MediaPictureType.FrontCover, picture.PictureType);
        Assert.Equal("image/png", picture.MimeType);
        Assert.Equal("cover.png", picture.Description);
        Assert.Equal(ReadTestFile("cover.png"), picture.Data);

        var nativeField = Assert.Single(tags.NativeFields!);
        Assert.Equal(MediaTagType.Ape, nativeField.TagType);
        Assert.Equal("Private Data", nativeField.Id);
        Assert.Equal([0, 1, 2, 3], nativeField.Data);
        Assert.Equal(2, nativeField.Flags);

        Assert.NotNull(tags.Duration);
        Assert.Equal(durationSeconds, tags.Duration.Value.TotalSeconds, 0.001);
    }

    [Theory]
    [InlineData("basic.wv", MediaFormat.WavPack, 1.0)]
    [InlineData("basic.mpc", MediaFormat.Musepack, 1.0)]
    public void ReadTags_EncodedFiles(string fileName, MediaFormat format, double durationSeconds)
    {
        var tags = Read(ReadTestFile(fileName));

        Assert.Equal(format, tags.Format);
        Assert.Equal("Test Title", tags.Title);
        Assert.Equal("Test Artist", tags.Artist);
        Assert.Equal("Test Album", tags.Album);
        Assert.Equal(2024, tags.Year);
        Assert.Equal(3, tags.TrackNumber);
        Assert.Equal("Rock", tags.Genre);
        Assert.NotNull(tags.Duration);
        Assert.Equal(durationSeconds, tags.Duration.Value.TotalSeconds, 0.05);
    }

    [Fact]
    public void ReadTags_UnicodeWavPack()
    {
        var tags = Read(ReadTestFile("unicode.wv"));

        Assert.Equal("日本語テスト", tags.Title);
        Assert.Equal("Тест Артист", tags.Artist);
        Assert.Equal("Tëst Àlbüm", tags.Album);
    }

    [Theory]
    [InlineData("empty.ape")]
    [InlineData("empty.wv")]
    [InlineData("empty.mpc")]
    public void ReadTags_EmptyFiles(string fileName)
    {
        var tags = Read(ReadTestFile(fileName));

        Assert.Null(tags.Title);
        Assert.Empty(tags.CustomFields);
        Assert.Empty(tags.NativeFields!);
        Assert.NotNull(tags.Duration);
    }

    [Theory]
    [InlineData("basic.ape", MediaFormat.MonkeysAudio)]
    [InlineData("basic.wv", MediaFormat.WavPack)]
    [InlineData("basic.mpc", MediaFormat.Musepack)]
    [InlineData("sv7.mpc", MediaFormat.Musepack)]
    public void WriteTags_RoundTrip(string fileName, MediaFormat format)
    {
        var tags = new MediaTagInfo
        {
            Title = "New Title",
            Artist = "New Artist",
            Album = "New Album",
            AlbumArtist = "New Album Artist",
            Genre = "Jazz",
            Year = 1999,
            TrackTotal = 10,
            DiscNumber = 2,
            Comment = "New Comment",
            Lyrics = "Line 1\nLine 2",
            Isrc = "USRC17607839",
            Composer = "New Composer",
            Conductor = "New Conductor",
            Copyright = "1999 New",
            Bpm = 98,
            IsCompilation = false,
            ReplayGain = new ReplayGainInfo { AlbumGain = -3.21, AlbumPeak = 0.5 },
            MusicBrainzAlbumId = "0a1b2c3d-4e5f-4061-8293-a4b5c6d7e8f9",
        };
        tags.CustomFields["MOOD"] = "Calm";
        tags.Pictures.Add(new MediaPicture { PictureType = MediaPictureType.BackCover, Description = "back.png", Data = ReadTestFile("cover.png") });

        var written = Write(ReadTestFile(fileName), tags, format);
        var readBack = Read(written);

        Assert.Equal(format, readBack.Format);
        Assert.Equal("New Title", readBack.Title);
        Assert.Equal("New Artist", readBack.Artist);
        Assert.Equal("New Album", readBack.Album);
        Assert.Equal("New Album Artist", readBack.AlbumArtist);
        Assert.Equal("Jazz", readBack.Genre);
        Assert.Equal(1999, readBack.Year);
        Assert.Null(readBack.TrackNumber);
        Assert.Equal(10, readBack.TrackTotal);
        Assert.Equal(2, readBack.DiscNumber);
        Assert.Null(readBack.DiscTotal);
        Assert.Equal("New Comment", readBack.Comment);
        Assert.Equal("Line 1\nLine 2", readBack.Lyrics);
        Assert.Equal("USRC17607839", readBack.Isrc);
        Assert.Equal("New Composer", readBack.Composer);
        Assert.Equal("New Conductor", readBack.Conductor);
        Assert.Equal("1999 New", readBack.Copyright);
        Assert.Equal(98, readBack.Bpm);
        Assert.False(readBack.IsCompilation);
        Assert.Equal(-3.21, readBack.ReplayGain?.AlbumGain);
        Assert.Equal(0.5, readBack.ReplayGain?.AlbumPeak);
        Assert.Equal("0a1b2c3d-4e5f-4061-8293-a4b5c6d7e8f9", readBack.MusicBrainzAlbumId);
        Assert.Equal("Calm", readBack.CustomFields["MOOD"]);

        var picture = Assert.Single(readBack.Pictures);
        Assert.Equal(MediaPictureType.BackCover, picture.PictureType);
        Assert.Equal("back.png", picture.Description);
        Assert.Equal("image/png", picture.MimeType);

        // The duration comes from the audio, which the write does not change
        Assert.Equal(Read(ReadTestFile(fileName)).Duration, readBack.Duration);
    }

    [Fact]
    public void NativeFields_Example()
    {
        #region MediaFile_NativeFields
        var tags = MediaFile.ReadTags("TestFiles/basic.ape").Value;

        // The items of the APE tag that no property represents
        var privateData = tags.NativeFields!.Single(field => field.Id == "Private Data");
        Assert.Equal(MediaTagType.Ape, privateData.TagType);

        // Replace it with an external link
        tags.NativeFields!.Remove(privateData);
        tags.NativeFields.Add(new MediaNativeField { TagType = MediaTagType.Ape, Id = "Homepage", Data = "https://example.com"u8.ToArray(), Flags = 4 });

        using var input = File.OpenRead("TestFiles/basic.ape");
        using var output = new MemoryStream();
        var result = MediaFile.WriteTags(input, output, tags, MediaFormat.MonkeysAudio);
        Assert.True(result.IsSuccess, result.ErrorMessage);

        output.Position = 0;
        var written = MediaFile.ReadTags(output).Value;
        Assert.Equal("Homepage", Assert.Single(written.NativeFields!).Id);
        #endregion
    }

    [Fact]
    public void WriteTags_KeepsNativeFieldsOfTheFile()
    {
        var written = Write(ReadTestFile("basic.ape"), new MediaTagInfo { Title = "Only Title" }, MediaFormat.MonkeysAudio);
        var readBack = Read(written);

        Assert.Equal("Only Title", readBack.Title);
        Assert.Null(readBack.Artist);
        Assert.Empty(readBack.Pictures);
        var nativeField = Assert.Single(readBack.NativeFields!);
        Assert.Equal("Private Data", nativeField.Id);
    }

    [Fact]
    public void WriteTags_ReplacesNativeFields()
    {
        var tags = Read(ReadTestFile("basic.ape"));
        tags.NativeFields!.Clear();
        tags.NativeFields.Add(new MediaNativeField { TagType = MediaTagType.Ape, Id = "Link", Data = "https://example.com"u8.ToArray(), Flags = 4 | 1 });

        // A field of another tag type does not apply to this format
        tags.NativeFields.Add(new MediaNativeField { TagType = MediaTagType.Id3v2, Id = "PRIV", Data = [1] });

        var readBack = Read(Write(ReadTestFile("basic.ape"), tags, MediaFormat.MonkeysAudio));

        var nativeField = Assert.Single(readBack.NativeFields!);
        Assert.Equal("Link", nativeField.Id);
        Assert.Equal("https://example.com"u8.ToArray(), nativeField.Data);
        Assert.Equal(5, nativeField.Flags);
    }

    [Theory]
    [InlineData("Cover Art (Front)", 2)]
    [InlineData("Title", 0)]
    [InlineData("InvalidéKey", 2)]
    [InlineData("X", 2)]
    [InlineData("TAG", 2)]
    [InlineData("Flags", 8)]
    public void WriteTags_InvalidNativeField_Fails(string key, int flags)
    {
        var tags = new MediaTagInfo { NativeFields = [new MediaNativeField { TagType = MediaTagType.Ape, Id = key, Data = [1], Flags = flags }] };

        var result = MediaFile.WriteTags(new MemoryStream(ReadTestFile("empty.ape"), writable: false), new MemoryStream(), tags, MediaFormat.MonkeysAudio);

        Assert.False(result.IsSuccess);
        Assert.Equal(MediaTagError.InvalidTagData, result.Error);
    }

    [Fact]
    public void WriteTags_InvalidCustomFieldKey_Fails()
    {
        var tags = new MediaTagInfo();
        tags.CustomFields["Ünïcode"] = "value";

        var result = MediaFile.WriteTags(new MemoryStream(ReadTestFile("empty.wv"), writable: false), new MemoryStream(), tags, MediaFormat.WavPack);

        Assert.False(result.IsSuccess);
        Assert.Equal(MediaTagError.InvalidTagData, result.Error);
    }

    [Fact]
    public void WriteTags_TwoPicturesOfTheSameType_Fails()
    {
        var tags = new MediaTagInfo();
        tags.Pictures.Add(new MediaPicture { PictureType = MediaPictureType.FrontCover, Data = [1] });
        tags.Pictures.Add(new MediaPicture { PictureType = MediaPictureType.FrontCover, Data = [2] });

        var result = MediaFile.WriteTags(new MemoryStream(ReadTestFile("empty.wv"), writable: false), new MemoryStream(), tags, MediaFormat.WavPack);

        Assert.False(result.IsSuccess);
        Assert.Equal(MediaTagError.InvalidTagData, result.Error);
    }

    [Fact]
    public void RemoveTags_RemovesApeAndId3v1Tags()
    {
        // Two APE tags and an ID3v1 tag appended by different taggers
        var original = ReadTestFile("empty.ape");
        var file = Write(original, new MediaTagInfo { Title = "First" }, MediaFormat.MonkeysAudio);
        var id3v1 = new byte[128];
        "TAG"u8.CopyTo(id3v1);
        "Id3v1 Title"u8.CopyTo(id3v1.AsSpan(3));
        id3v1[127] = 255;
        var secondTag = Write(original, new MediaTagInfo { Artist = "Second" }, MediaFormat.MonkeysAudio).AsSpan(original.Length).ToArray();
        file = [.. file, .. id3v1, .. secondTag];

        var tags = Read(file);
        Assert.Equal("Second", tags.Artist);
        Assert.Equal("Id3v1 Title", tags.Title);

        using var output = new MemoryStream();
        Assert.True(MediaFile.RemoveTags(new MemoryStream(file, writable: false), output, MediaFormat.MonkeysAudio).IsSuccess);
        Assert.Equal(original, output.ToArray());
    }

    [Fact]
    public void ReadTags_Mp3WithApeTag_UsesItAfterId3v2()
    {
        var tags = Read(ReadTestFile("ape_only.mp3"));

        Assert.Equal(MediaFormat.Mp3, tags.Format);
        Assert.Equal("Ape Title", tags.Title);
        Assert.Equal("Ape Artist", tags.Artist);
        Assert.Equal("Ape Album", tags.Album);
        Assert.Equal(7, tags.TrackNumber);

        // From the ID3v1 tag, as the APE tag has no year
        Assert.Equal(2001, tags.Year);
    }

    [Fact]
    public void WriteTags_Mp3WithApeTag_ReplacesItWithId3v2()
    {
        var file = ReadTestFile("ape_only.mp3");
        var tags = Read(file);
        tags.Title = "Changed";

        var written = Write(file, tags, MediaFormat.Mp3);

        Assert.Equal(-1, written.AsSpan().IndexOf("APETAGEX"u8));
        var readBack = Read(written);
        Assert.Equal("Changed", readBack.Title);
        Assert.Equal("Ape Artist", readBack.Artist);
        Assert.Equal(7, readBack.TrackNumber);
        Assert.Equal(2001, readBack.Year);
    }

    [Fact]
    public void ReadTags_ItemsTheWriterRefuses_CanBeWrittenBack()
    {
        // An item with a forbidden key, and a binary item named like a property that an ID3v1 tag also sets
        var tag = BuildApeTag(("TAG", 0, "x"u8.ToArray()), ("Title", 2, [1, 2]));
        var id3v1 = new byte[128];
        "TAG"u8.CopyTo(id3v1);
        "Id3v1 Title"u8.CopyTo(id3v1.AsSpan(3));
        id3v1[127] = 255;
        var file = (byte[])[.. ReadTestFile("empty.wv"), .. tag, .. id3v1];

        // The property wins over the native field of the same name
        var tags = Read(file);
        Assert.Empty(tags.CustomFields);
        Assert.Equal("Id3v1 Title", tags.Title);
        Assert.Empty(tags.NativeFields!);

        var readBack = Read(Write(file, tags, MediaFormat.WavPack));
        Assert.Equal("Id3v1 Title", readBack.Title);
        Assert.Empty(readBack.NativeFields!);
    }

    private static byte[] BuildApeTag(params (string Key, int Flags, byte[] Value)[] items)
    {
        var body = items.SelectMany(item => (byte[])[.. BitConverter.GetBytes(item.Value.Length), .. BitConverter.GetBytes(item.Flags), .. Encoding.ASCII.GetBytes(item.Key), 0, .. item.Value]).ToArray();
        var footer = new byte[32];
        "APETAGEX"u8.CopyTo(footer);
        BinaryPrimitives.WriteInt32LittleEndian(footer.AsSpan(8), 2000);
        BinaryPrimitives.WriteInt32LittleEndian(footer.AsSpan(12), body.Length + 32);
        BinaryPrimitives.WriteInt32LittleEndian(footer.AsSpan(16), items.Length);
        return [.. body, .. footer];
    }

    [Fact]
    public void ReadTags_ApeVersion1Tag()
    {
        // An APEv1 tag has no header, and its items are text whatever their flags
        var item = (byte[])[.. BitConverter.GetBytes(5), .. BitConverter.GetBytes(2), .. "Title\0"u8, .. "Hello"u8];
        var footer = new byte[32];
        "APETAGEX"u8.CopyTo(footer);
        BinaryPrimitives.WriteInt32LittleEndian(footer.AsSpan(8), 1000);
        BinaryPrimitives.WriteInt32LittleEndian(footer.AsSpan(12), item.Length + 32);
        BinaryPrimitives.WriteInt32LittleEndian(footer.AsSpan(16), 1);
        var file = (byte[])[.. ReadTestFile("empty.wv"), .. item, .. footer];

        Assert.Equal("Hello", Read(file).Title);
    }

    [Fact]
    public void ReadTags_TagThatCoversTheSignature_IsIgnored()
    {
        // A footer whose size reaches the start of the file
        var file = ReadTestFile("empty.ape");
        var footer = new byte[32];
        "APETAGEX"u8.CopyTo(footer);
        BinaryPrimitives.WriteInt32LittleEndian(footer.AsSpan(8), 2000);
        BinaryPrimitives.WriteInt32LittleEndian(footer.AsSpan(12), file.Length + 32);
        file = [.. file, .. footer];

        var tags = Read(file);
        Assert.Null(tags.Title);

        var written = Write(file, new MediaTagInfo { Title = "Title" }, MediaFormat.MonkeysAudio);
        Assert.Equal(MediaFormat.MonkeysAudio, MediaFile.DetectFormat(new MemoryStream(written)));
        Assert.Equal("Title", Read(written).Title);
    }
}
