using System.Buffers.Binary;

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

    [Fact]
    public void ReadTags_InfoEngineer_IsNotTheComposer()
    {
        var file = BuildWav(8, ("LIST", InfoList(("IENG", "Engineer"))));

        var tags = ReadWav(file);

        Assert.Null(tags.Composer);
        Assert.Equal("Engineer", tags.CustomFields["IENG"]);
    }

    [Fact]
    public void ReadTags_InfoTrackNumberWithATotal()
    {
        var tags = ReadWav(BuildWav(8, ("LIST", InfoList(("ITRK", "3/12")))));

        Assert.Equal(3, tags.TrackNumber);
        Assert.Equal(12, tags.TrackTotal);
    }

    [Fact]
    public void WriteTags_KeepsTheId3FramesItDoesNotRead()
    {
        var id3Tag = Mp3Id3v2Tests.BuildId3Tag(4, 0,
            ("TIT2", 0, Mp3Id3v2Tests.TextBody("Title")),
            ("TPUB", 0, Mp3Id3v2Tests.TextBody("Label")));
        var file = BuildWav(8, ("id3 ", id3Tag));

        var tags = ReadWav(file);
        tags.Title = "New title";
        using var input = new MemoryStream(file);
        using var output = new MemoryStream();
        Assert.True(MediaFile.WriteTags(input, output, tags, MediaFormat.Wav).IsSuccess);

        Assert.True(output.ToArray().AsSpan().IndexOf("Label"u8) >= 0);
        output.Position = 0;
        Assert.Equal("New title", MediaFile.ReadTags(output, MediaFormat.Wav).Value.Title);
    }

    [Fact]
    public void ReadAndWriteTags_DataChunkLargerThanTwoGigabytes()
    {
        // RIFF chunk sizes are unsigned. Read as signed, the data chunk of a WAV between 2 and 4 GB has a negative
        // size: reading stops before the tag that follows it, and writing is refused.
        const uint DataSize = 0x9000_0000;
        var id3Tag = Mp3Id3v2Tests.BuildId3Tag(4, 0, ("TIT2", 0, Mp3Id3v2Tests.TextBody("Big")));
        var header = BuildWav(0);
        var trailer = BuildChunk("id3 ", id3Tag);

        // Patch the RIFF size and the data chunk size of the header, which ends with the empty data chunk
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), (uint)(header.Length - 8 + DataSize + trailer.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(header.Length - 4), DataSize);

        using var input = new ZeroFilledStream(header, DataSize, trailer);
        var read = MediaFile.ReadTags(input, MediaFormat.Wav);
        Assert.True(read.IsSuccess, read.ErrorMessage);
        Assert.Equal("Big", read.Value.Title);
        Assert.InRange(read.Value.Duration!.Value.TotalSeconds, (DataSize / 88200d) - 1, (DataSize / 88200d) + 1);

        input.Position = 0;
        using var output = new CountingStream();
        var write = MediaFile.WriteTags(input, output, new MediaTagInfo { Title = "Title" }, MediaFormat.Wav);
        Assert.True(write.IsSuccess, write.ErrorMessage);
        Assert.True(output.Length > DataSize);
    }

    private static MediaTagInfo ReadWav(byte[] file)
    {
        using var stream = new MemoryStream(file);
        var result = MediaFile.ReadTags(stream, MediaFormat.Wav);
        Assert.True(result.IsSuccess, result.ErrorMessage);
        return result.Value;
    }

    /// <summary>Builds a 44.1 kHz 16-bit mono PCM WAV file whose data chunk comes last before <paramref name="chunks"/>.</summary>
    private static byte[] BuildWav(int dataSize, params (string Id, byte[] Data)[] chunks)
    {
        var format = new byte[16];
        BinaryPrimitives.WriteUInt16LittleEndian(format, 1); // PCM
        BinaryPrimitives.WriteUInt16LittleEndian(format.AsSpan(2), 1); // Mono
        BinaryPrimitives.WriteUInt32LittleEndian(format.AsSpan(4), 44100);
        BinaryPrimitives.WriteUInt32LittleEndian(format.AsSpan(8), 88200);
        BinaryPrimitives.WriteUInt16LittleEndian(format.AsSpan(12), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(format.AsSpan(14), 16);

        byte[] body = [.. "WAVE"u8, .. BuildChunk("fmt ", format), .. BuildChunk("data", new byte[dataSize]), .. chunks.SelectMany(chunk => BuildChunk(chunk.Id, chunk.Data))];
        var sizeField = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(sizeField, (uint)body.Length);
        return [.. "RIFF"u8, .. sizeField, .. body];
    }

    private static byte[] BuildChunk(string id, byte[] data)
    {
        var header = new byte[8];
        Encoding.ASCII.GetBytes(id, header);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), (uint)data.Length);
        return data.Length % 2 == 0 ? [.. header, .. data] : [.. header, .. data, 0];
    }

    private static byte[] InfoList(params (string Id, string Value)[] values)
        => [.. "INFO"u8, .. values.SelectMany(value => BuildChunk(value.Id, [.. Encoding.Latin1.GetBytes(value.Value), 0]))];

    /// <summary>A stream of a prefix, a run of zeros too large to hold in memory, and a suffix.</summary>
    private sealed class ZeroFilledStream(byte[] prefix, long zeroCount, byte[] suffix) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => prefix.Length + zeroCount + suffix.Length;
        public override long Position { get; set; }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            var total = 0;
            while (!buffer.IsEmpty && Position < Length)
            {
                int count;
                if (Position < prefix.Length)
                {
                    count = (int)Math.Min(buffer.Length, prefix.Length - Position);
                    prefix.AsSpan((int)Position, count).CopyTo(buffer);
                }
                else if (Position < prefix.Length + zeroCount)
                {
                    count = (int)Math.Min(buffer.Length, prefix.Length + zeroCount - Position);
                    buffer[..count].Clear();
                }
                else
                {
                    var suffixOffset = (int)(Position - prefix.Length - zeroCount);
                    count = Math.Min(buffer.Length, suffix.Length - suffixOffset);
                    suffix.AsSpan(suffixOffset, count).CopyTo(buffer);
                }

                buffer = buffer[count..];
                Position += count;
                total += count;
            }

            return total;
        }

        public override long Seek(long offset, SeekOrigin origin) => Position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => Position + offset,
            _ => Length + offset,
        };

        public override void Flush()
        {
        }

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>A write-only stream that discards what it is given and counts it.</summary>
    private sealed class CountingStream : Stream
    {
        private long _length;

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _length;

        public override long Position
        {
            get => _length;
            set => throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count) => _length += count;

        public override void Write(ReadOnlySpan<byte> buffer) => _length += buffer.Length;

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
