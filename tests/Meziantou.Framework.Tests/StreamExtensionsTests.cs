namespace Meziantou.Framework.Tests;

public sealed class StreamExtensionsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadToEndTests(bool canSeek)
    {
        using var stream = new MemoryStream();
        Enumerable.Range(0, 5).ForEach(i => stream.WriteByte((byte)i));
        stream.Seek(0, SeekOrigin.Begin);
        using var byteByByteStream = new CustomStream(stream, canSeek);

        var result = byteByByteStream.ReadToEnd();
        Assert.Equal([0, 1, 2, 3, 4], result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadToEndAsyncTests(bool canSeek)
    {
        using var stream = new MemoryStream();
        Enumerable.Range(0, 5).ForEach(i => stream.WriteByte((byte)i));
        stream.Seek(0, SeekOrigin.Begin);
        await using var byteByByteStream = new CustomStream(stream, canSeek);

        var result = await byteByByteStream.ReadToEndAsync();
        Assert.Equal([0, 1, 2, 3, 4], result);
    }

    [Fact]
    public void ReadToEnd_PositionPastEnd_ReturnsEmptyArray()
    {
        using var stream = new MemoryStream(new byte[10]) { Position = 100 };

        Assert.Empty(stream.ReadToEnd());
    }

    [Fact]
    public async Task ReadToEndAsync_PositionPastEnd_ReturnsEmptyArray()
    {
        using var stream = new MemoryStream(new byte[10]) { Position = 100 };

        Assert.Empty(await stream.ReadToEndAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(5)]
    public void ReadToEnd_StreamLongerThanLength_ReadsUntilTheEnd(int reportedLength)
    {
        using var stream = new UnderreportedLengthStream([0, 1, 2, 3, 4], reportedLength);

        Assert.Equal([0, 1, 2, 3, 4], stream.ReadToEnd());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(5)]
    public async Task ReadToEndAsync_StreamLongerThanLength_ReadsUntilTheEnd(int reportedLength)
    {
        using var stream = new UnderreportedLengthStream([0, 1, 2, 3, 4], reportedLength);

        Assert.Equal([0, 1, 2, 3, 4], await stream.ReadToEndAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TryReadAllTests(bool canSeek)
    {
        using var stream = new MemoryStream();
        Enumerable.Range(0, 5).ForEach(i => stream.WriteByte((byte)i));
        stream.Seek(0, SeekOrigin.Begin);
        using var byteByByteStream = new CustomStream(stream, canSeek);

        var buffer = new byte[5];
        byteByByteStream.TryReadAll(buffer, 0, 5);
        Assert.Equal([0, 1, 2, 3, 4], buffer);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TryReadAllAsyncTests(bool canSeek)
    {
        using var stream = new MemoryStream();
        Enumerable.Range(0, 5).ForEach(i => stream.WriteByte((byte)i));
        stream.Seek(0, SeekOrigin.Begin);
        await using var byteByByteStream = new CustomStream(stream, canSeek);

        var buffer = new byte[5];
        await byteByByteStream.TryReadAllAsync(buffer, 0, 5);
        Assert.Equal([0, 1, 2, 3, 4], buffer);
    }

    [Fact]
    public async Task ToMemoryStreamAsyncTest()
    {
        using var stream = new MemoryStream();
        Enumerable.Range(0, 5).ForEach(i => stream.WriteByte((byte)i));
        stream.Seek(1, SeekOrigin.Begin);

        await using var copy = await stream.ToMemoryStreamAsync();
        Assert.Equal([1, 2, 3, 4], copy.ToArray());
    }

    private sealed class CustomStream(Stream stream, bool canSeek) : Stream
    {
        public override bool CanRead => stream.CanRead;
        public override bool CanSeek => canSeek && stream.CanSeek;
        public override bool CanWrite => throw new NotSupportedException();
        public override long Length => stream.Length;
        public override long Position { get => stream.Position; set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
        {
            return stream.Read(buffer, offset, 1);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return stream.ReadAsync(buffer, offset, 1, cancellationToken);
        }

        public override int Read(Span<byte> buffer)
        {
            return stream.Read(buffer[0..1]);
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return stream.ReadAsync(buffer[0..1], cancellationToken);
        }

        public override long Seek(long offset, SeekOrigin origin) => stream.Seek(offset, origin);

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    // Simulates a stream that grows while it is read, such as a log file being appended
    private sealed class UnderreportedLengthStream(byte[] buffer, long reportedLength) : MemoryStream(buffer)
    {
        public override long Length => reportedLength;
    }

    [Fact]
    public async Task Documentation_ReadAll()
    {
        #region StreamExtensions_ReadAll
        using var stream = new MemoryStream([1, 2, 3, 4, 5]);

        // Fill the buffer, unless the end of the stream is reached first
        var header = new byte[2];
        Assert.Equal(2, stream.TryReadAll(header));

        // Read the rest of the stream
        var rest = await stream.ReadToEndAsync();
        Assert.Equal([3, 4, 5], rest);
        #endregion
    }
}
