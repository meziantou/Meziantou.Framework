using Meziantou.Framework.MediaTags;

namespace Meziantou.Framework.MediaTags.Tests;

public sealed class AiffTests
{
    private static string GetTestFilePath(string fileName) => Path.Combine("TestFiles", fileName);

    [Fact]
    public void ReadTags_BasicAiff()
    {
        var result = MediaFile.ReadTags(GetTestFilePath("basic.aiff"));
        Assert.True(result.IsSuccess);

        var tags = result.Value;
        Assert.Equal(MediaFormat.Aiff, tags.Format);
    }

    [Fact]
    public void ReadTags_EmptyAiff()
    {
        var result = MediaFile.ReadTags(GetTestFilePath("empty.aiff"));
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void WriteTags_RoundTrip()
    {
        var tempFile = Path.GetTempFileName() + ".aiff";
        try
        {
            File.Copy(GetTestFilePath("basic.aiff"), tempFile, overwrite: true);

            var newTags = new MediaTagInfo
            {
                Title = "New AIFF Title",
                Artist = "New AIFF Artist",
                Album = "New AIFF Album",
                Year = 2025,
            };

            var writeResult = MediaFile.WriteTags(tempFile, newTags);
            Assert.True(writeResult.IsSuccess);

            var readResult = MediaFile.ReadTags(tempFile);
            Assert.True(readResult.IsSuccess);

            Assert.Equal("New AIFF Title", readResult.Value.Title);
            Assert.Equal("New AIFF Artist", readResult.Value.Artist);
            Assert.Equal("New AIFF Album", readResult.Value.Album);
            Assert.Equal(2025, readResult.Value.Year);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void WriteTags_PreservesFormHeader()
    {
        var tempFile = Path.GetTempFileName() + ".aiff";
        try
        {
            File.Copy(GetTestFilePath("basic.aiff"), tempFile, overwrite: true);

            var tags = new MediaTagInfo { Title = "Test" };
            MediaFile.WriteTags(tempFile, tags);

            // File should start with FORM....AIFF
            using var stream = File.OpenRead(tempFile);
            var header = new byte[12];
            stream.ReadAtLeast(header, 12);
            Assert.Equal((byte)'F', header[0]);
            Assert.Equal((byte)'O', header[1]);
            Assert.Equal((byte)'R', header[2]);
            Assert.Equal((byte)'M', header[3]);
            Assert.Equal((byte)'A', header[8]);
            Assert.Equal((byte)'I', header[9]);
            Assert.Equal((byte)'F', header[10]);
            Assert.Equal((byte)'F', header[11]);
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
        var result = MediaFile.ReadTags(stream, MediaFormat.Aiff);
        Assert.False(result.IsSuccess);
    }
}
