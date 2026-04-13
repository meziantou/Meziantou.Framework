using Meziantou.Framework.MediaTags;

namespace Meziantou.Framework.MediaTags.Tests;

public sealed class OggOpusTests
{
    private static string GetTestFilePath(string fileName) => Path.Combine("TestFiles", fileName);

    [Fact]
    public void ReadTags_BasicOpus()
    {
        var result = MediaFile.ReadTags(GetTestFilePath("basic.opus"));
        Assert.True(result.IsSuccess);

        var tags = result.Value;
        Assert.Equal(MediaFormat.OggOpus, tags.Format);
        Assert.Equal("Test Title", tags.Title);
        Assert.Equal("Test Artist", tags.Artist);
        Assert.Equal("Test Album", tags.Album);
        Assert.Equal(2024, tags.Year);
    }

    [Fact]
    public void WriteTags_RoundTrip()
    {
        var tempFile = Path.GetTempFileName() + ".opus";
        try
        {
            File.Copy(GetTestFilePath("basic.opus"), tempFile, overwrite: true);

            var newTags = new MediaTagInfo
            {
                Title = "New Opus Title",
                Artist = "New Opus Artist",
                Lyrics = "New Opus Lyrics",
                Isrc = "USRC17607839",
            };

            var writeResult = MediaFile.WriteTags(tempFile, newTags);
            Assert.True(writeResult.IsSuccess);

            var readResult = MediaFile.ReadTags(tempFile);
            Assert.True(readResult.IsSuccess);

            Assert.Equal("New Opus Title", readResult.Value.Title);
            Assert.Equal("New Opus Artist", readResult.Value.Artist);
            Assert.Equal("New Opus Lyrics", readResult.Value.Lyrics);
            Assert.Equal("USRC17607839", readResult.Value.Isrc);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
