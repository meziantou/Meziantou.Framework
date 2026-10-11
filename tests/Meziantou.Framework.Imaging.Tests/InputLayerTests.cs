using System.IO.Compression;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ARequestTheEndSatisfiesCompletesWhenTheInputEndsExactlyAtTheLimit(bool asynchronous)
    {
        var data = Enumerable.Range(0, 100).Select(value => (byte)value).ToArray();
        foreach (var limit in new long[] { 100, 101, long.MaxValue })
        {
            Assert.Equal((90, null), RunOnSpan(data, limit, () => new TailParser(prefix: 10)));

            var (result, exception, bytesRead) = await RunOnStreamAsync(data, limit, () => new TailParser(prefix: 10), asynchronous);
            Assert.Null(exception);
            Assert.Equal(90, result);
            Assert.Equal(100, bytesRead);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ARequestTheEndSatisfiesFailsWithTheLimitWhenTheInputContinuesPastIt(bool asynchronous)
    {
        // The limit falls on the first byte of the tail, inside it, and on its last byte
        var data = new byte[100];
        foreach (var limit in new long[] { 10, 60, 99 })
        {
            var (_, fromSpan) = RunOnSpan(data, limit, () => new TailParser(prefix: 10));
            Assert.NotNull(fromSpan);
            Assert.Equal(ImageResourceLimitKind.EncodedBytes, fromSpan.Kind);
            Assert.Equal(limit, fromSpan.Limit);
            Assert.Equal(limit + 1, fromSpan.Requested);

            var (_, fromStream, bytesRead) = await RunOnStreamAsync(data, limit, () => new TailParser(prefix: 10), asynchronous);
            Assert.NotNull(fromStream);
            Assert.Equal(fromSpan.Kind, fromStream.Kind);
            Assert.Equal(fromSpan.Limit, fromStream.Limit);
            Assert.Equal(fromSpan.Requested, fromStream.Requested);

            // One discarded byte tells the end of the input from a longer input: it is the only read past the limit
            Assert.Equal(limit + 1, bytesRead);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ARequestLargerThanTheLimitIsSatisfiedByTheEndOfTheInput(bool asynchronous)
    {
        var data = new byte[50];
        foreach (var limit in new long[] { 50, 60 })
        {
            Assert.Equal((50, null), RunOnSpan(data, limit, () => new EndParser(request: 1000)));

            var (result, exception, bytesRead) = await RunOnStreamAsync(data, limit, () => new EndParser(request: 1000), asynchronous);
            Assert.Null(exception);
            Assert.Equal(50, result);
            Assert.Equal(50, bytesRead);
        }

        var (_, fromSpan) = RunOnSpan(data, 49, () => new EndParser(request: 1000));
        var (_, fromStream, _) = await RunOnStreamAsync(data, 49, () => new EndParser(request: 1000), asynchronous);
        Assert.NotNull(fromSpan);
        Assert.Equal(1000, fromSpan.Requested);
        Assert.NotNull(fromStream);
        Assert.Equal(fromSpan.Requested, fromStream.Requested);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ARequestTheEndDoesNotSatisfyNeverReadsPastTheLimit(bool asynchronous)
    {
        var data = new byte[100];
        var (_, fromSpan) = RunOnSpan(data, 40, () => new ConsumingParser());
        var (_, fromStream, bytesRead) = await RunOnStreamAsync(data, 40, () => new ConsumingParser(), asynchronous);
        Assert.NotNull(fromSpan);
        Assert.Equal(41, fromSpan.Requested);
        Assert.NotNull(fromStream);
        Assert.Equal(fromSpan.Requested, fromStream.Requested);
        Assert.Equal(40, bytesRead);
    }

    [Fact]
    public async Task TheEndOfTheInputIsProbedOnce()
    {
        var context = CreateContext(40);
        using var stream = new TestInputStream(new byte[100]) { Seekable = false };
        using var input = new ImageInputBuffer(stream, ownsStream: false, context, initialCapacity: 16);
        input.Fill(40);
        Assert.True(input.IsReadLimitReached);
        Assert.False(input.ExceedsLimit);

        input.ProbeEndOfInput();
        Assert.True(input.ExceedsLimit);
        Assert.False(input.IsEndOfInput);
        Assert.Equal(40, input.BufferedLength);
        Assert.Equal(41, stream.BytesRead);

        input.ProbeEndOfInput();
        await input.ProbeEndOfInputAsync(XunitCancellationToken);
        Assert.Equal(41, stream.BytesRead);

        using var endingStream = new TestInputStream(new byte[40]) { Seekable = false };
        using var endingInput = new ImageInputBuffer(endingStream, ownsStream: false, CreateContext(40), initialCapacity: 16);
        endingInput.Fill(40);
        Assert.False(endingInput.IsEndOfInput);
        await endingInput.ProbeEndOfInputAsync(XunitCancellationToken);
        Assert.True(endingInput.IsEndOfInput);
        Assert.False(endingInput.ExceedsLimit);
        Assert.Equal(40, endingInput.BufferedLength);
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

    public static TheoryData<InputVariant> Variants => [.. InputVariants.All];

    [Theory]
    [MemberData(nameof(Variants))]
    public async Task AnInputShorterThanTheDetectionPrefixIsDetectedWithinItsOwnLength(InputVariant variant)
    {
        // A complete image of 8 bytes: the 18-byte detection prefix is the whole input, whose end a stream only reports
        // once it is read past
        var image = "P1\n1 1\n0"u8.ToArray();
        var unknown = "not an image"u8.ToArray();
        foreach (var (data, expected) in new[] { (image, "Pnm 1x1"), (unknown, nameof(UnknownImageFormatException)) })
        {
            foreach (var limit in new[] { data.Length + 1, data.Length })
            {
                var outcome = await DescribeAsync(variant, data, limit);
                Assert.StartsWith("identify: " + expected, outcome);
                Assert.Equal(await DescribeAsync(InputVariant.Span, data, limit), outcome);
            }

            // A limit that cuts the input is a limit failure, never a format detected (or rejected) from a part of the input
            foreach (var limit in new[] { data.Length - 1, 2, 1 })
            {
                var outcome = await DescribeAsync(variant, data, limit);
                Assert.StartsWith(string.Create(CultureInfo.InvariantCulture, $"identify: ImageResourceLimitException(EncodedBytes, limit {limit}, requested 18)"), outcome);
                Assert.Equal(await DescribeAsync(InputVariant.Span, data, limit), outcome);
            }
        }

        static Task<string> DescribeAsync(InputVariant variant, byte[] data, long limit)
            => InputVariants.DescribeOutcomeAsync(variant, data, ImageFormat.Pnm, new ImageConfiguration { Limits = new ImageResourceLimits { MaxEncodedBytes = limit } }, XunitCancellationToken);
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

    private static ImageCodecContext CreateContext(long maxEncodedBytes)
        => new(new ImageConfiguration { Limits = new ImageResourceLimits { MaxEncodedBytes = maxEncodedBytes } }, "test", CancellationToken.None);

    /// <summary>Runs a parser over a non-seekable stream that returns one byte per read, through a buffer that has to grow.</summary>
    private static async Task<(int Result, ImageResourceLimitException? Exception, long BytesRead)> RunOnStreamAsync(byte[] data, long maxEncodedBytes, Func<ImageParser<int>> createParser, bool asynchronous)
    {
        var context = CreateContext(maxEncodedBytes);
        using var stream = new TestInputStream(data) { Seekable = false, MaxBytesPerRead = 1, ForbidSynchronousReads = asynchronous, ForbidAsynchronousReads = !asynchronous };
        using var input = new ImageInputBuffer(stream, ownsStream: false, context, initialCapacity: 16);
        using var parser = createParser();
        try
        {
            var result = asynchronous ? await ImageInputPump.RunAsync(input, parser, context) : ImageInputPump.Run(input, parser, context);
            return (result, null, stream.BytesRead);
        }
        catch (ImageResourceLimitException exception)
        {
            return (0, exception, stream.BytesRead);
        }
    }

    private static (int Result, ImageResourceLimitException? Exception) RunOnSpan(byte[] data, long maxEncodedBytes, Func<ImageParser<int>> createParser)
    {
        using var parser = createParser();
        try
        {
            return (ImageInputPump.Run(data, parser, CreateContext(maxEncodedBytes)), null);
        }
        catch (ImageResourceLimitException exception)
        {
            return (0, exception);
        }
    }

    /// <summary>Consumes a prefix, then needs every remaining byte at once: a structure located from the end of the input.</summary>
    private sealed class TailParser(int prefix) : ImageParser<int>(ImageFormat.Png)
    {
        private int _prefixLeft = prefix;
        private int _tail;

        public override ParseStatus Parse(ReadOnlySpan<byte> buffer, bool isEndOfInput, out int consumed)
        {
            consumed = Math.Min(_prefixLeft, buffer.Length);
            _prefixLeft -= consumed;
            if (_prefixLeft > 0)
                return ParseStatus.NeedMoreData(1);

            var tail = buffer[consumed..];
            if (!isEndOfInput)
                return ParseStatus.NeedMoreDataOrEnd(tail.Length + 1);

            _tail = tail.Length;
            consumed = buffer.Length;
            return ParseStatus.Complete;
        }

        public override int GetResult() => _tail;
    }

    /// <summary>Needs the end of the input, and asks for it with a request the limit can never hold.</summary>
    private sealed class EndParser(int request) : ImageParser<int>(ImageFormat.Png)
    {
        private int _length;

        public override ParseStatus Parse(ReadOnlySpan<byte> buffer, bool isEndOfInput, out int consumed)
        {
            consumed = 0;
            if (!isEndOfInput)
                return ParseStatus.NeedMoreDataOrEnd(request);

            _length = buffer.Length;
            consumed = buffer.Length;
            return ParseStatus.Complete;
        }

        public override int GetResult() => _length;
    }

    /// <summary>Consumes everything and always needs one more byte: the end of the input is truncation.</summary>
    private sealed class ConsumingParser() : ImageParser<int>(ImageFormat.Png)
    {
        public override ParseStatus Parse(ReadOnlySpan<byte> buffer, bool isEndOfInput, out int consumed)
        {
            consumed = buffer.Length;
            return ParseStatus.NeedMoreData(1);
        }

        public override int GetResult() => 0;
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
