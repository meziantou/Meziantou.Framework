using Meziantou.Framework.MediaTags;

namespace Meziantou.Framework.MediaTags.Tests;

public sealed class MediaFileDetectionTests
{
    private static string GetTestFilePath(string fileName) => Path.Combine("TestFiles", fileName);

    [Fact]
    public void DetectFormat_Mp3()
    {
        var format = MediaFile.DetectFormat(GetTestFilePath("basic.mp3"));
        Assert.Equal(MediaFormat.Mp3, format);
    }

    [Fact]
    public void DetectFormat_Ogg()
    {
        var format = MediaFile.DetectFormat(GetTestFilePath("basic.ogg"));
        Assert.Equal(MediaFormat.OggVorbis, format);
    }

    [Fact]
    public void DetectFormat_Opus()
    {
        var format = MediaFile.DetectFormat(GetTestFilePath("basic.opus"));
        Assert.Equal(MediaFormat.OggOpus, format);
    }

    [Fact]
    public void DetectFormat_Flac()
    {
        var format = MediaFile.DetectFormat(GetTestFilePath("basic.flac"));
        Assert.Equal(MediaFormat.Flac, format);
    }

    [Fact]
    public void DetectFormat_M4a()
    {
        var format = MediaFile.DetectFormat(GetTestFilePath("basic.m4a"));
        Assert.Equal(MediaFormat.Mp4, format);
    }

    [Fact]
    public void DetectFormat_Wav()
    {
        var format = MediaFile.DetectFormat(GetTestFilePath("basic.wav"));
        Assert.Equal(MediaFormat.Wav, format);
    }

    [Fact]
    public void DetectFormat_Aiff()
    {
        var format = MediaFile.DetectFormat(GetTestFilePath("basic.aiff"));
        Assert.Equal(MediaFormat.Aiff, format);
    }

    [Fact]
    public void DetectFormat_FromStream_Mp3()
    {
        using var stream = File.OpenRead(GetTestFilePath("basic.mp3"));
        var format = MediaFile.DetectFormat(stream);
        Assert.Equal(MediaFormat.Mp3, format);
        Assert.Equal(0, stream.Position); // Position should be restored
    }

    [Fact]
    public void DetectFormat_UnknownFile_ReturnsNull()
    {
        using var stream = new MemoryStream([0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B]);
        var format = MediaFile.DetectFormat(stream);
        Assert.Null(format);
    }
}
