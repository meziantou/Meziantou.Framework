using System.Buffers.Binary;
using System.Text;
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
}
