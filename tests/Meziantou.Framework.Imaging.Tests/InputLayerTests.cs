using System.IO.Compression;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.TestHarness.Streams;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>The bounded input layer: buffer growth and replay, the parser contract, limits, checksums and bounded decompression.</summary>
public sealed class InputLayerTests
{
    [Fact]
    public void BufferReplaysTheDetectionPrefixAndGrowsForContiguousRequests()
    {
        var data = Enumerable.Range(0, 1000).Select(value => (byte)value).ToArray();
        var context = new ImageCodecContext(ImageConfiguration.Default, "test", CancellationToken.None);
        using var stream = new TestInputStream(data) { Seekable = false, MaxBytesPerRead = 3 };
        using (var input = new ImageInputBuffer(stream, ownsStream: false, context, initialCapacity: 16))
        {
            input.Fill(8);
            Assert.True(input.BufferedLength >= 8);
            Assert.Equal(data[..8], input.Buffered.Span[..8].ToArray());

            // Consuming and asking for a larger contiguous block keeps the unconsumed bytes, then grows
            input.Consume(5);
            input.Fill(300);
            Assert.True(input.Capacity >= 300);
            Assert.Equal(data[5..305], input.Buffered.Span[..300].ToArray());
            Assert.Equal(5, input.Position);

            input.Consume(300);
            input.Fill(10_000);
            Assert.True(input.IsEndOfInput);
            Assert.Equal(data[305..], input.Buffered.ToArray());
            Assert.Equal(305, context.Tracker.EncodedBytes);
            Assert.True(context.Scope.GetLiveBytes(AllocationKind.DecoderState) > 0);
        }

        Assert.Equal(0, context.Scope.LiveBytes);
        Assert.False(stream.IsDisposed);
    }

    [Fact]
    public void BufferNeverReadsPastTheEncodedByteLimit()
    {
        var data = new byte[100];
        var context = new ImageCodecContext(new ImageConfiguration { Limits = new ImageResourceLimits { MaxEncodedBytes = 40 } }, "test", CancellationToken.None);
        using var stream = new TestInputStream(data);
        using var input = new ImageInputBuffer(stream, ownsStream: false, context);
        input.Fill(30);
        Assert.Equal(40, stream.BytesRead);
        Assert.True(input.IsReadLimitReached);
        Assert.False(input.IsEndOfInput);
    }

    [Fact]
    public void OwnedStreamsAreDisposed()
    {
        var context = new ImageCodecContext(ImageConfiguration.Default, "test", CancellationToken.None);
        var stream = new TestInputStream(new byte[4]);
        new ImageInputBuffer(stream, ownsStream: true, context).Dispose();
        Assert.True(stream.IsDisposed);
    }

    [Fact]
    public void ParsersMayYieldButMustMakeProgress()
    {
        var context = new ImageCodecContext(ImageConfiguration.Default, "test", CancellationToken.None);
        var data = new byte[100];
        using (var parser = new YieldingParser())
        {
            Assert.Equal(100, ImageInputPump.Run(data, parser, context));
        }

        using var stream = new MemoryStream(data);
        using var input = new ImageInputBuffer(stream, ownsStream: false, new ImageCodecContext(ImageConfiguration.Default, "test", CancellationToken.None));
        using (var parser = new YieldingParser())
        {
            Assert.Equal(100, ImageInputPump.Run(input, parser, context));
        }

        Assert.Throws<InvalidOperationException>(() => ImageInputPump.Run(data, new StuckParser(), context));
        Assert.Throws<InvalidOperationException>(() => ImageInputPump.Run(data, new GreedyParser(), context));
    }

    [Fact]
    public void LimitFailuresTakePrecedenceOverTruncation()
    {
        // A request beyond the limit fails with the limit, even when the input would also be too short
        var context = new ImageCodecContext(new ImageConfiguration { Limits = new ImageResourceLimits { MaxEncodedBytes = 10 } }, "test", CancellationToken.None);
        var exception = Assert.Throws<ImageResourceLimitException>(() => ImageInputPump.Run(new byte[5], new FixedRequestParser(20), context));
        Assert.Equal(ImageResourceLimitKind.EncodedBytes, exception.Kind);
        Assert.Equal(20, exception.Requested);

        var truncated = Assert.Throws<InvalidImageContentException>(() => ImageInputPump.Run(new byte[5], new FixedRequestParser(8), new ImageCodecContext(ImageConfiguration.Default, "test", CancellationToken.None)));
        Assert.Equal(ImageFormat.Png, truncated.Format);
    }

    [Fact]
    public void DetectionNeedsTheFullPrefixWithinTheLimit()
    {
        var png = Codecs.SyntheticImages.Png(1, 1);
        var options = new ImageIdentifyOptions { Configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxEncodedBytes = 7 } } };
        Assert.Equal(ImageResourceLimitKind.EncodedBytes, Assert.Throws<ImageResourceLimitException>(() => Image.Identify(png, options)).Kind);
        using var stream = new MemoryStream(png);
        Assert.Equal(ImageResourceLimitKind.EncodedBytes, Assert.Throws<ImageResourceLimitException>(() => Image.Identify(stream, options)).Kind);
    }

    [Theory]
    [InlineData("", 0x00000000u)]
    [InlineData("123456789", 0xCBF43926u)]
    [InlineData("IEND", 0xAE426082u)]
    public void Crc32MatchesPublishedVectors(string text, uint expected) => Assert.Equal(expected, Crc32.Compute(Encoding.ASCII.GetBytes(text)));

    [Theory]
    [InlineData("", 0x00000001u)]
    [InlineData("Wikipedia", 0x11E60398u)]
    public void Adler32MatchesPublishedVectors(string text, uint expected) => Assert.Equal(expected, BoundedInflater.ComputeAdler32(Encoding.ASCII.GetBytes(text)));

    [Fact]
    public void BoundedInflaterValidatesCompletenessAndCharges()
    {
        var payload = Enumerable.Range(0, 20_000).Select(value => (byte)(value % 7)).ToArray();
        var compressed = Codecs.SyntheticImages.Zlib(payload);
        var tracker = new InputResourceTracker(ImageResourceLimits.Default);
        Assert.Equal(payload, BoundedInflater.TryInflateZlib(compressed, tracker));
        Assert.Equal(payload.Length, tracker.MetadataBytes);

        // The BCL reports truncation as a clean end: the Adler-32 trailer is verified
        Assert.Null(BoundedInflater.TryInflateZlib(compressed.AsSpan(0, compressed.Length - 2), new InputResourceTracker(ImageResourceLimits.Default)));
        Assert.Null(BoundedInflater.TryInflateZlib(compressed.AsSpan(0, compressed.Length / 2), new InputResourceTracker(ImageResourceLimits.Default)));
        Assert.Null(BoundedInflater.TryInflateZlib([0x78, 0x9C, 0xFF, 0xFF, 0xFF, 0xFF, 0, 0], new InputResourceTracker(ImageResourceLimits.Default)));

        var limited = new InputResourceTracker(new ImageResourceLimits { MaxMetadataBytes = 10_000 });
        var exception = Assert.Throws<ImageResourceLimitException>(() => BoundedInflater.TryInflateZlib(compressed, limited));
        Assert.Equal(ImageResourceLimitKind.MetadataBytes, exception.Kind);
        Assert.True(limited.MetadataBytes <= 10_000);
    }

    [Fact]
    public void ZlibStreamResumesAfterAStarvedFeed()
    {
        // Documents the BCL behavior codec authors rely on to inflate IDAT/fdAT payloads pushed chunk by chunk
        var payload = Enumerable.Range(0, 50_000).Select(value => (byte)(value * 31 % 251)).ToArray();
        var compressed = Codecs.SyntheticImages.Zlib(payload);
        var feed = new FeedStream();
        using var zlib = new ZLibStream(feed, CompressionMode.Decompress);
        var output = new MemoryStream();
        var buffer = new byte[1024];
        var offset = 0;
        while (true)
        {
            var read = zlib.Read(buffer);
            if (read > 0)
            {
                output.Write(buffer, 0, read);
                continue;
            }

            if (offset == compressed.Length)
                break;

            var count = Math.Min(97, compressed.Length - offset);
            feed.Push(compressed.AsSpan(offset, count));
            offset += count;
        }

        Assert.Equal(payload, output.ToArray());
        Assert.True(feed.StarvedReads > 1);
    }

    private sealed class YieldingParser() : ImageParser<int>(ImageFormat.Png)
    {
        private int _total;

        public override ParseStatus Parse(ReadOnlySpan<byte> buffer, bool isEndOfInput, out int consumed)
        {
            // Bounded work per call: consume at most 7 bytes, then yield
            consumed = Math.Min(7, buffer.Length);
            _total += consumed;
            if (_total == 100)
                return ParseStatus.Complete;

            return ParseStatus.NeedMoreData(1);
        }

        public override int GetResult() => _total;
    }

    private sealed class StuckParser() : ImageParser<int>(ImageFormat.Png)
    {
        public override ParseStatus Parse(ReadOnlySpan<byte> buffer, bool isEndOfInput, out int consumed)
        {
            consumed = 0;
            return ParseStatus.NeedMoreData(1);
        }

        public override int GetResult() => 0;
    }

    private sealed class GreedyParser() : ImageParser<int>(ImageFormat.Png)
    {
        public override ParseStatus Parse(ReadOnlySpan<byte> buffer, bool isEndOfInput, out int consumed)
        {
            consumed = buffer.Length + 1;
            return ParseStatus.Complete;
        }

        public override int GetResult() => 0;
    }

    private sealed class FixedRequestParser(int required) : ImageParser<int>(ImageFormat.Png)
    {
        public override ParseStatus Parse(ReadOnlySpan<byte> buffer, bool isEndOfInput, out int consumed)
        {
            consumed = 0;
            return buffer.Length >= required ? ParseStatus.Complete : ParseStatus.NeedMoreData(required);
        }

        public override int GetResult() => required;
    }

    private sealed class FeedStream : Stream
    {
        private readonly Queue<byte> _bytes = new();

        public int StarvedReads { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public void Push(ReadOnlySpan<byte> data)
        {
            foreach (var value in data)
            {
                _bytes.Enqueue(value);
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = 0;
            while (read < count && _bytes.Count > 0)
            {
                buffer[offset + read++] = _bytes.Dequeue();
            }

            if (read == 0)
            {
                StarvedReads++;
            }

            return read;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
