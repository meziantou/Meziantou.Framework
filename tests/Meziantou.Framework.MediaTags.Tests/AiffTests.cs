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

    [Fact]
    public async Task ReadTags_NegativeChunkSize_DoesNotLoopForever()
    {
        // A chunk size of -8 makes the next chunk position land back on the chunk header itself
        using var stream = new MemoryStream(CreateAiff(("SSND", -8, [1, 2, 3, 4])));

        var read = Task.Run(() => MediaFile.ReadTags(stream, MediaFormat.Aiff));
        var completed = await Task.WhenAny(read, Task.Delay(TimeSpan.FromSeconds(30))) == read;

        Assert.True(completed, "ReadTags did not return; the chunk walk is stuck on the same chunk header.");
    }

    [Fact]
    public void ReadTags_ChunkSizeBeyondEndOfFile_DoesNotAllocateTheDeclaredSize()
    {
        // A 1 GB NAME chunk declared by a file only a few bytes long
        using var stream = new MemoryStream(CreateAiff(("NAME", 0x4000_0000, [1, 2, 3, 4])));

        // Read once so the measured read is not paying for JIT and first-use initialization
        MediaFile.ReadTags(stream, MediaFormat.Aiff);

        var before = GC.GetTotalAllocatedBytes(precise: true);
        var result = MediaFile.ReadTags(stream, MediaFormat.Aiff);
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - before;

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Title);
        Assert.True(allocated < 1024 * 1024, $"Reading a {stream.Length} byte file allocated {allocated} bytes.");
    }

    [Fact]
    public async Task WriteTags_NegativeChunkSize_DoesNotLoopForever()
    {
        var tempFile = Path.GetTempFileName() + ".aiff";
        try
        {
            File.WriteAllBytes(tempFile, CreateAiff(("SSND", -8, [1, 2, 3, 4])));

            var write = Task.Run(() => MediaFile.WriteTags(tempFile, new MediaTagInfo { Title = "Title" }));
            var completed = await Task.WhenAny(write, Task.Delay(TimeSpan.FromSeconds(30))) == write;

            Assert.True(completed, "WriteTags did not return; the chunk walk is stuck on the same chunk header.");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    private static byte[] CreateAiff(params (string Id, int DeclaredSize, byte[] Data)[] chunks)
    {
        var body = new MemoryStream();
        foreach (var (id, declaredSize, data) in chunks)
        {
            var header = new byte[8];
            Encoding.ASCII.GetBytes(id, header);
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), declaredSize);
            body.Write(header);
            body.Write(data);
        }

        var result = new MemoryStream();
        result.Write("FORM"u8);
        var formSize = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(formSize, (int)body.Length + 4);
        result.Write(formSize);
        result.Write("AIFF"u8);
        body.Position = 0;
        body.CopyTo(result);
        return result.ToArray();
    }
}
