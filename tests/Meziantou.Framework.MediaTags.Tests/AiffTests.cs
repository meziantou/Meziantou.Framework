using System.Buffers.Binary;
using System.Text;

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
    public void WriteTags_FileWithExistingIsrcChunk_WritesTheNewIsrc()
    {
        var tempFile = Path.GetTempFileName() + ".aiff";
        try
        {
            File.WriteAllBytes(tempFile, CreateAiffWithIsrcChunk("OLDISRC00000"));
            Assert.Equal("OLDISRC00000", MediaFile.ReadTags(tempFile).Value.Isrc);

            var writeResult = MediaFile.WriteTags(tempFile, new MediaTagInfo { Title = "Title", Isrc = "NEWISRC99999" });
            Assert.True(writeResult.IsSuccess);

            var readResult = MediaFile.ReadTags(tempFile);
            Assert.True(readResult.IsSuccess);
            Assert.Equal("NEWISRC99999", readResult.Value.Isrc);
            Assert.Equal("Title", readResult.Value.Title);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    private static byte[] CreateAiffWithIsrcChunk(string isrc)
    {
        var body = new MemoryStream();
        WriteChunk(body, "COMM", new byte[18]);
        WriteChunk(body, "ISRC", Encoding.ASCII.GetBytes(isrc));
        WriteChunk(body, "SSND", new byte[16]);

        var result = new MemoryStream();
        result.Write("FORM"u8);
        var formSize = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(formSize, (int)body.Length + 4);
        result.Write(formSize);
        result.Write("AIFF"u8);
        body.Position = 0;
        body.CopyTo(result);
        return result.ToArray();

        static void WriteChunk(Stream stream, string id, byte[] data)
        {
            var header = new byte[8];
            Encoding.ASCII.GetBytes(id, header);
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), data.Length);
            stream.Write(header);
            stream.Write(data);
            if (data.Length % 2 != 0)
                stream.WriteByte(0);
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
            stream.ReadExactly(header);
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
