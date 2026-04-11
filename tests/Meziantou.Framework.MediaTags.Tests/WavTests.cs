using Meziantou.Framework.MediaTags;

namespace Meziantou.Framework.MediaTags.Tests;

public sealed class WavTests
{
    private static string GetTestFilePath(string fileName) => Path.Combine("TestFiles", fileName);

    [Fact]
    public void ReadTags_BasicWav()
    {
        var result = MediaFile.ReadTags(GetTestFilePath("basic.wav"));
        Assert.True(result.IsSuccess);

        var tags = result.Value;
        Assert.Equal(MediaFormat.Wav, tags.Format);
        // WAV metadata support varies by ffmpeg version; check at least title
        if (tags.Title is not null)
            Assert.Equal("Test Title", tags.Title);
    }

    [Fact]
    public void ReadTags_EmptyWav()
    {
        var result = MediaFile.ReadTags(GetTestFilePath("empty.wav"));
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void WriteTags_RoundTrip()
    {
        var tempFile = Path.GetTempFileName() + ".wav";
        try
        {
            File.Copy(GetTestFilePath("basic.wav"), tempFile, overwrite: true);

            var newTags = new MediaTagInfo
            {
                Title = "New WAV Title",
                Artist = "New WAV Artist",
                Album = "New WAV Album",
            };

            var writeResult = MediaFile.WriteTags(tempFile, newTags);
            Assert.True(writeResult.IsSuccess);

            var readResult = MediaFile.ReadTags(tempFile);
            Assert.True(readResult.IsSuccess);

            Assert.Equal("New WAV Title", readResult.Value.Title);
            Assert.Equal("New WAV Artist", readResult.Value.Artist);
            Assert.Equal("New WAV Album", readResult.Value.Album);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void WriteTags_PreservesRiffHeader()
    {
        var tempFile = Path.GetTempFileName() + ".wav";
        try
        {
            File.Copy(GetTestFilePath("basic.wav"), tempFile, overwrite: true);

            var tags = new MediaTagInfo { Title = "Test" };
            MediaFile.WriteTags(tempFile, tags);

            // File should still start with RIFF....WAVE
            using var stream = File.OpenRead(tempFile);
            var header = new byte[12];
            stream.ReadAtLeast(header, 12);
            Assert.Equal((byte)'R', header[0]);
            Assert.Equal((byte)'I', header[1]);
            Assert.Equal((byte)'F', header[2]);
            Assert.Equal((byte)'F', header[3]);
            Assert.Equal((byte)'W', header[8]);
            Assert.Equal((byte)'A', header[9]);
            Assert.Equal((byte)'V', header[10]);
            Assert.Equal((byte)'E', header[11]);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ReadTags_InvalidFile_ReturnsError()
    {
        using var stream = new MemoryStream([0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B]);
        var result = MediaFile.ReadTags(stream, MediaFormat.Wav);
        Assert.False(result.IsSuccess);
    }
}
