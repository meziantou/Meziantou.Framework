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
        Assert.NotNull(tags.Duration);
        Assert.InRange(tags.Duration.Value.TotalSeconds, 0.95, 1.05);
        // The fixture is committed, so these are not version dependent. Guarding the assertion would let a
        // reader that no longer understands a third-party LIST/INFO chunk pass.
        Assert.Equal("Test Title", tags.Title);
        Assert.Equal("Test Artist", tags.Artist);
        Assert.Equal("Test Album", tags.Album);
    }

    [Fact]
    public void ReadTags_EmptyWav()
    {
        var result = MediaFile.ReadTags(GetTestFilePath("empty.wav"));
        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Title);
        Assert.Null(result.Value.Artist);
        Assert.Null(result.Value.Album);
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
            stream.ReadExactly(header);
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

    [Fact]
    public void ReadTags_TruncatedInfoChunk_DoesNotReadZeroPaddedData()
    {
        using var stream = new MemoryStream([
            (byte)'R', (byte)'I', (byte)'F', (byte)'F', 25, 0, 0, 0, (byte)'W', (byte)'A', (byte)'V', (byte)'E',
            (byte)'L', (byte)'I', (byte)'S', (byte)'T', 16, 0, 0, 0, (byte)'I', (byte)'N', (byte)'F', (byte)'O',
            (byte)'I', (byte)'N', (byte)'A', (byte)'M', 4, 0, 0, 0, (byte)'A',
        ]);

        var result = MediaFile.ReadTags(stream, MediaFormat.Wav);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Title);
    }

    [Fact]
    public void ReadTags_DeeplyNestedListChunks_ReturnsErrorInsteadOfOverflowingTheStack()
    {
        // Each LIST chunk declares the rest of the file as its payload, so every 12 bytes adds a nesting level
        const int Levels = 100_000;
        var file = new MemoryStream();
        file.Write("RIFF"u8);
        file.Write([0, 0, 0, 0]);
        file.Write("WAVE"u8);
        for (var i = 0; i < Levels; i++)
        {
            file.Write("LIST"u8);
            var size = (Levels - i - 1) * 12 + 4;
            file.Write([(byte)size, (byte)(size >> 8), (byte)(size >> 16), (byte)(size >> 24)]);
            file.Write("INFO"u8);
        }

        using var stream = new MemoryStream(file.ToArray());
        var result = MediaFile.ReadTags(stream, MediaFormat.Wav);

        Assert.False(result.IsSuccess);
        Assert.Equal(MediaTagError.CorruptFile, result.Error);
    }

    [Fact]
    public void WriteTags_NotARiffFile_IsRefused()
    {
        using var input = new MemoryStream(Encoding.ASCII.GetBytes(new string('X', 4096)));
        using var output = new MemoryStream();

        var result = MediaFile.WriteTags(input, output, new MediaTagInfo { Title = "Title" }, MediaFormat.Wav);

        Assert.False(result.IsSuccess);
        Assert.Equal(MediaTagError.UnsupportedFormat, result.Error);
    }

    [Fact]
    public void WriteTags_ReplayGainAndCustomFields_RoundTrip()
    {
        var tempFile = Path.GetTempFileName() + ".wav";
        try
        {
            File.Copy(GetTestFilePath("basic.wav"), tempFile, overwrite: true);

            var tags = new MediaTagInfo
            {
                Title = "Title",
                ReplayGain = new ReplayGainInfo { TrackGain = -1.5, TrackPeak = 0.25 },
            };
            tags.CustomFields["MY FIELD"] = "my value";

            Assert.True(MediaFile.WriteTags(tempFile, tags).IsSuccess);

            var read = MediaFile.ReadTags(tempFile).Value;
            Assert.Equal(-1.5, read.ReplayGain?.TrackGain);
            Assert.Equal(0.25, read.ReplayGain?.TrackPeak);
            Assert.Equal("my value", read.CustomFields["MY FIELD"]);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
