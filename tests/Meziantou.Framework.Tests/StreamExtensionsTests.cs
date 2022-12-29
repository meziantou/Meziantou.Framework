using FluentAssertions;
using Xunit;

namespace Meziantou.Framework.Tests;

public sealed class StreamExtensionsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [SuppressMessage("Style", "IDE0230:Use UTF-8 string literal", Justification = "")]
    public void ReadToEndTests(bool canSeek)
    {
        using var stream = new MemoryStream();
        Enumerable.Range(0, 5).ForEach(i => stream.WriteByte((byte)i));
        stream.Seek(0, SeekOrigin.Begin);
        using var byteByByteStream = new CustomStream(stream, canSeek);

        var result = byteByByteStream.ReadToEnd();

        result.Should().Equal(new byte[] { 0, 1, 2, 3, 4 });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [SuppressMessage("Style", "IDE0230:Use UTF-8 string literal", Justification = "")]
    public async Task ReadToEndAsyncTests(bool canSeek)
    {
        using var stream = new MemoryStream();
        Enumerable.Range(0, 5).ForEach(i => stream.WriteByte((byte)i));
        stream.Seek(0, SeekOrigin.Begin);
        using var byteByByteStream = new CustomStream(stream, canSeek);

        var result = await byteByByteStream.ReadToEndAsync();

        result.Should().Equal(new byte[] { 0, 1, 2, 3, 4 });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [SuppressMessage("Style", "IDE0230:Use UTF-8 string literal", Justification = "")]
    public void TryReadAllTests(bool canSeek)
    {
        using var stream = new MemoryStream();
        Enumerable.Range(0, 5).ForEach(i => stream.WriteByte((byte)i));
        stream.Seek(0, SeekOrigin.Begin);
        using var byteByByteStream = new CustomStream(stream, canSeek);

        var buffer = new byte[5];
        byteByByteStream.TryReadAll(buffer, 0, 5);

        buffer.Should().Equal(new byte[] { 0, 1, 2, 3, 4 });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [SuppressMessage("Style", "IDE0230:Use UTF-8 string literal", Justification = "")]
    public async Task TryReadAllAsyncTests(bool canSeek)
    {
        using var stream = new MemoryStream();
        Enumerable.Range(0, 5).ForEach(i => stream.WriteByte((byte)i));
        stream.Seek(0, SeekOrigin.Begin);
        using var byteByByteStream = new CustomStream(stream, canSeek);

        var buffer = new byte[5];
        await byteByByteStream.TryReadAllAsync(buffer, 0, 5);

        buffer.Should().Equal(new byte[] { 0, 1, 2, 3, 4 });
    }

    [Fact]
    public async Task ToMemoryStreamAsyncTest()
    {
        using var stream = new MemoryStream();
        Enumerable.Range(0, 5).ForEach(i => stream.WriteByte((byte)i));
        stream.Seek(1, SeekOrigin.Begin);

        using var copy = await stream.ToMemoryStreamAsync();

        copy.ToArray().Should().Equal(new byte[] { 1, 2, 3, 4 });
    }

    private sealed class CustomStream : Stream
    {
        [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Stream is owned by the caller")]
        private readonly Stream _stream;
        private readonly bool _canSeek;

        public CustomStream(Stream stream, bool canSeek)
        {
            _stream = stream;
            _canSeek = canSeek;
        }

        public override bool CanRead => _stream.CanRead;
        public override bool CanSeek => _canSeek && _stream.CanSeek;
        public override bool CanWrite => throw new NotSupportedException();
        public override long Length => _stream.Length;
        public override long Position { get => _stream.Position; set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
        {
            return _stream.Read(buffer, offset, 1);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return _stream.ReadAsync(buffer, offset, 1, cancellationToken);
        }

        public override int Read(Span<byte> buffer)
        {
            return _stream.Read(buffer[0..1]);
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return _stream.ReadAsync(buffer[0..1], cancellationToken);
        }

        public override long Seek(long offset, SeekOrigin origin) => _stream.Seek(offset, origin);

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
