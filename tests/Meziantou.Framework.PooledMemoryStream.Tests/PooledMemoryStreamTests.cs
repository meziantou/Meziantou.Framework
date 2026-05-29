using System.Buffers;
using System.Text.Json;

namespace Meziantou.Framework.Tests;

public sealed class PooledMemoryStreamTests
{
    // Deterministic pseudo-random sequence (xorshift32) so tests don't depend on System.Random.
    private static uint NextRandom(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return state;
    }

    private static int NextRandom(ref uint state, int maxExclusive) => (int)(NextRandom(ref state) % (uint)maxExclusive);

    private static byte[] CreateData(int length, int seed = 0)
    {
        var data = new byte[length];
        for (var i = 0; i < length; i++)
            data[i] = (byte)((i * 31) + (seed * 7) + 13);
        return data;
    }

    // Small tier sizes so tests exercise multiple segments without allocating much.
    private static PooledMemoryStreamOptions SmallTiers() => new()
    {
        BufferSizes = [16, 64, 256],
    };

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(63)]
    [InlineData(256)]
    [InlineData(1000)]
    [InlineData(5000)]
    public void WriteThenRead_RoundTrip(int length)
    {
        var data = CreateData(length, seed: length);
        using var stream = new PooledMemoryStream(SmallTiers());

        stream.Write(data, 0, data.Length);
        Assert.Equal(length, stream.Length);
        Assert.Equal(length, stream.Position);

        stream.Position = 0;
        var read = new byte[length];
        var total = 0;
        int n;
        while ((n = stream.Read(read, total, read.Length - total)) > 0)
            total += n;

        Assert.Equal(length, total);
        Assert.Equal(data, read);
        Assert.Equal(data, stream.ToArray());
    }

    [Fact]
    public void Write_InMultipleCalls_AcrossSegments()
    {
        var data = CreateData(1000, seed: 1);
        using var stream = new PooledMemoryStream(SmallTiers());

        var offset = 0;
        foreach (var chunk in new[] { 1, 2, 7, 16, 31, 100, 200, 343 })
        {
            stream.Write(data, offset, chunk);
            offset += chunk;
        }

        stream.Write(data.AsSpan(offset)); // remaining
        Assert.Equal(data, stream.ToArray());
    }

    [Fact]
    public void ReadByte_And_WriteByte()
    {
        using var stream = new PooledMemoryStream(SmallTiers());
        for (var i = 0; i < 100; i++)
            stream.WriteByte((byte)i);

        stream.Position = 0;
        for (var i = 0; i < 100; i++)
            Assert.Equal(i, stream.ReadByte());

        Assert.Equal(-1, stream.ReadByte());
    }

    [Fact]
    public void SequentialByteAccess_AcrossManySegments_IsCorrect()
    {
        // Tiny single-tier blocks => thousands of segments; exercises the Locate cursor on sequential access.
        var data = CreateData(10_000, seed: 42);
        using var stream = new PooledMemoryStream(new PooledMemoryStreamOptions { BufferSizes = [4] });
        foreach (var b in data)
            stream.WriteByte(b);

        stream.Position = 0;
        for (var i = 0; i < data.Length; i++)
            Assert.Equal(data[i], stream.ReadByte());

        // Backward seek then forward read still works (cursor falls back to walking from the start).
        stream.Position = 5000;
        Assert.Equal(data[5000], stream.ReadByte());
        stream.Position = 100;
        Assert.Equal(data[100], stream.ReadByte());
    }

    [Fact]
    public void Seek_FromAllOrigins()
    {
        using var stream = new PooledMemoryStream();
        stream.Write(CreateData(100));

        Assert.Equal(10, stream.Seek(10, SeekOrigin.Begin));
        Assert.Equal(15, stream.Seek(5, SeekOrigin.Current));
        Assert.Equal(90, stream.Seek(-10, SeekOrigin.End));
        Assert.Throws<IOException>(() => { stream.Seek(-1, SeekOrigin.Begin); });
    }

    [Fact]
    public void Overwrite_InTheMiddle()
    {
        using var stream = new PooledMemoryStream(SmallTiers());
        stream.Write(CreateData(100, seed: 2));

        stream.Position = 20;
        var overwrite = CreateData(50, seed: 99);
        stream.Write(overwrite);

        var result = stream.ToArray();
        Assert.Equal(100, result.Length);
        Assert.Equal(overwrite, result[20..70]);
    }

    [Fact]
    public void SeekPastEnd_ThenWrite_ZeroFillsGap()
    {
        using var stream = new PooledMemoryStream(SmallTiers());
        stream.Write("abc"u8);

        stream.Position = 20;
        stream.Write("xyz"u8);

        var result = stream.ToArray();
        Assert.Equal(23, result.Length);
        Assert.Equal("abc"u8.ToArray(), result[0..3]);
        Assert.All(result[3..20], b => Assert.Equal(0, b));
        Assert.Equal("xyz"u8.ToArray(), result[20..23]);
    }

    [Fact]
    public void SetLength_Grow_ZeroFills()
    {
        using var stream = new PooledMemoryStream(SmallTiers());
        stream.Write(CreateData(10, seed: 3));

        stream.SetLength(100);
        Assert.Equal(100, stream.Length);

        var result = stream.ToArray();
        Assert.Equal(100, result.Length);
        Assert.All(result[10..], b => Assert.Equal(0, b));
    }

    [Fact]
    public void SetLength_Shrink_Truncates()
    {
        var data = CreateData(1000, seed: 4);
        using var stream = new PooledMemoryStream(SmallTiers());
        stream.Write(data);

        stream.SetLength(37);
        Assert.Equal(37, stream.Length);
        Assert.Equal(37, stream.Position);
        Assert.Equal(data[..37], stream.ToArray());
    }

    [Fact]
    public void Capacity_GrowsAndStaysAtLeastLength()
    {
        using var stream = new PooledMemoryStream(SmallTiers());
        stream.Capacity = 500;
        Assert.True(stream.Capacity >= 500);

        stream.Write(CreateData(500, seed: 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => stream.Capacity = 10);
    }

    [Fact]
    public void Constructor_WithInitialCapacity()
    {
        using var stream = new PooledMemoryStream(1234);
        Assert.True(stream.Capacity >= 1234);
        Assert.Equal(0, stream.Length);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(1000)]
    [InlineData(100_000)]
    public void ToArray_GetBuffer_TryGetBuffer_WriteTo(int length)
    {
        var data = CreateData(length, seed: length);
        using var stream = new PooledMemoryStream(SmallTiers());
        stream.Write(data);

        Assert.Equal(data, stream.ToArray());

        var buffer = stream.GetBuffer();
        Assert.True(buffer.Length >= length);
        Assert.Equal(data, buffer.AsSpan(0, length).ToArray());

        Assert.True(stream.TryGetBuffer(out var segment));
        Assert.Equal(length, segment.Count);
        Assert.Equal(data, segment.AsSpan().ToArray());

        using var target = new MemoryStream();
        stream.WriteTo(target);
        Assert.Equal(data, target.ToArray());
    }

    [Fact]
    public void CopyTo_FromCurrentPosition()
    {
        var data = CreateData(1000, seed: 6);
        using var stream = new PooledMemoryStream(SmallTiers());
        stream.Write(data);
        stream.Position = 100;

        using var target = new MemoryStream();
        stream.CopyTo(target);

        Assert.Equal(data[100..], target.ToArray());
        Assert.Equal(stream.Length, stream.Position);
    }

    [Fact]
    public async Task CopyToAsync_FromCurrentPosition()
    {
        var data = CreateData(1000, seed: 7);
        await using var stream = new PooledMemoryStream(SmallTiers());
        await stream.WriteAsync(data);
        stream.Position = 250;

        using var target = new MemoryStream();
        await stream.CopyToAsync(target);

        Assert.Equal(data[250..], target.ToArray());
    }

    [Fact]
    public async Task AsyncReadWrite_RoundTrip()
    {
        var data = CreateData(5000, seed: 8);
        await using var stream = new PooledMemoryStream(SmallTiers());
        await stream.WriteAsync(data.AsMemory());

        stream.Position = 0;
        var read = new byte[data.Length];
        var total = 0;
        int n;
        while ((n = await stream.ReadAsync(read.AsMemory(total))) > 0)
            total += n;

        Assert.Equal(data, read);
    }

    [Fact]
    public void IBufferWriter_GetSpan_ReturnsAtLeastSizeHint_AndWritesAcrossBoundary()
    {
        using var stream = new PooledMemoryStream(SmallTiers());
        IBufferWriter<byte> writer = stream;

        var span = writer.GetSpan(20);
        Assert.True(span.Length >= 20);
        for (var i = 0; i < 20; i++)
            span[i] = (byte)i;
        writer.Advance(20);

        Assert.Equal(20, stream.Length);
        Assert.Equal(20, stream.Position);

        var span2 = writer.GetSpan(10);
        Assert.True(span2.Length >= 10);
        for (var i = 0; i < 10; i++)
            span2[i] = (byte)(100 + i);
        writer.Advance(10);

        var result = stream.ToArray();
        Assert.Equal(30, result.Length);
        for (var i = 0; i < 20; i++)
            Assert.Equal((byte)i, result[i]);
        for (var i = 0; i < 10; i++)
            Assert.Equal((byte)(100 + i), result[20 + i]);
    }

    [Fact]
    public void IBufferWriter_WorksWithUtf8JsonWriter()
    {
        using var stream = new PooledMemoryStream(SmallTiers());
        using (var json = new Utf8JsonWriter((IBufferWriter<byte>)stream))
        {
            json.WriteStartObject();
            json.WriteString("message", "hello");
            json.WriteNumber("value", 42);
            json.WriteEndObject();
        }

        Assert.Equal("""{"message":"hello","value":42}""", System.Text.Encoding.UTF8.GetString(stream.ToArray()));
    }

    [Fact]
    public void IBufferWriter_Advance_PastReservedBuffer_Throws()
    {
        using var stream = new PooledMemoryStream(SmallTiers());
        IBufferWriter<byte> writer = stream;
        var spanLength = writer.GetSpan(8).Length;
        Assert.Throws<InvalidOperationException>(() => writer.Advance(spanLength + 1));
    }

    [Fact]
    public void Dispose_ThenUse_Throws()
    {
        var stream = new PooledMemoryStream();
        stream.Write(CreateData(10));
        stream.Dispose();

        Assert.Throws<ObjectDisposedException>(() => stream.WriteByte(1));
        Assert.Throws<ObjectDisposedException>(() => { stream.ReadByte(); });
        Assert.Throws<ObjectDisposedException>(() => { _ = stream.Position; });
        Assert.Throws<ObjectDisposedException>(() => { _ = stream.Length; });
    }

    [Fact]
    public void Pool_IsShared_AndReusesBuffers()
    {
        // Use a unique buffer size so this test owns its pool bucket.
        var options = new PooledMemoryStreamOptions { BufferSizes = [3989, 8192, 65536] };

        byte[] firstBuffer;
        using (var stream = new PooledMemoryStream(options))
        {
            stream.Write(CreateData(10));
            firstBuffer = stream.GetBuffer();
        }

        // After disposing, the buffer is returned to the shared pool and should be reused by the next stream.
        using var stream2 = new PooledMemoryStream(options);
        stream2.Write(CreateData(10));
        Assert.Same(firstBuffer, stream2.GetBuffer());
    }

    [Fact]
    public void ClearOnReturn_ZeroesBufferWhenReturned()
    {
        var options = new PooledMemoryStreamOptions { BufferSizes = [4093, 8192, 65536], ClearOnReturn = true };

        byte[] buffer;
        using (var stream = new PooledMemoryStream(options))
        {
            var data = new byte[100];
            Array.Fill(data, (byte)0xAB);
            stream.Write(data);
            buffer = stream.GetBuffer();
            Assert.Equal((byte)0xAB, buffer[0]);
        }

        // Disposing returns the buffer to the pool and, because ClearOnReturn is set, zeroes it.
        Assert.All(buffer[..100], b => Assert.Equal(0, b));
    }

    [Fact]
    public void Options_AllowsArbitraryNumberOfTiers()
    {
        var data = CreateData(50_000, seed: 11);
        using var stream = new PooledMemoryStream(new PooledMemoryStreamOptions { BufferSizes = [4096, 131072, 1048576, 10485760] });
        stream.Write(data);
        Assert.Equal(data, stream.ToArray());
    }

    [Fact]
    public void Options_SingleTier_UsesUniformBlocks()
    {
        var data = CreateData(1000, seed: 12);
        using var stream = new PooledMemoryStream(new PooledMemoryStreamOptions { BufferSizes = [64] });
        stream.Write(data);
        Assert.Equal(data, stream.ToArray());
    }

    [Theory]
    [InlineData(new[] { 20, 10, 30 })]
    [InlineData(new[] { 10, 9 })]
    [InlineData(new[] { 10, 10 })]
    public void Options_NonAscendingSizes_Throw(int[] sizes)
    {
        var options = new PooledMemoryStreamOptions();
        Assert.Throws<ArgumentException>(() => options.BufferSizes = [.. sizes]);
    }

    [Theory]
    [InlineData(new[] { 0 })]
    [InlineData(new[] { -1, 10 })]
    public void Options_NonPositiveSizes_Throw(int[] sizes)
    {
        var options = new PooledMemoryStreamOptions();
        Assert.Throws<ArgumentOutOfRangeException>(() => options.BufferSizes = [.. sizes]);
    }

    [Fact]
    public void Options_Default_IsFrozen()
    {
        Assert.True(PooledMemoryStreamOptions.Default.IsFrozen);
        Assert.Throws<InvalidOperationException>(() => PooledMemoryStreamOptions.Default.BufferSizes = [8]);
        Assert.Throws<InvalidOperationException>(() => PooledMemoryStreamOptions.Default.ClearOnReturn = true);
    }

    [Fact]
    public void Options_AreMutableBeforeUse_AndFrozenAfter()
    {
        var options = new PooledMemoryStreamOptions { BufferSizes = [32] };
        Assert.False(options.IsFrozen);

        // Mutation is allowed before the options are used.
        options.BufferSizes = [32, 128];

        using (var stream = new PooledMemoryStream(options))
        {
            Assert.True(options.IsFrozen);
        }

        // Even after the stream is disposed, the options remain frozen.
        Assert.Throws<InvalidOperationException>(() => options.BufferSizes = [64]);
        Assert.Throws<InvalidOperationException>(() => options.MaxRetainedBytesPerBucket = 0);
        Assert.Throws<InvalidOperationException>(() => options.ClearOnReturn = true);
    }

    [Fact]
    public void Options_SharedAcrossStreams_StayFrozen()
    {
        var options = new PooledMemoryStreamOptions();
        using var stream1 = new PooledMemoryStream(options);
        using var stream2 = new PooledMemoryStream(options); // already frozen, must not throw
        Assert.True(options.IsFrozen);
    }

    [Fact]
    public void ParityWithMemoryStream_RandomOperations()
    {
        var state = 12345u;
        using var pooled = new PooledMemoryStream(SmallTiers());
        using var reference = new MemoryStream();

        for (var i = 0; i < 2000; i++)
        {
            switch (NextRandom(ref state, 5))
            {
                case 0: // write
                    var data = CreateData(NextRandom(ref state, 100), seed: (int)NextRandom(ref state));
                    pooled.Write(data);
                    reference.Write(data);
                    break;
                case 1: // seek
                    var pos = NextRandom(ref state, (int)reference.Length + 50);
                    pooled.Position = pos;
                    reference.Position = pos;
                    break;
                case 2: // write byte
                    var b = (byte)NextRandom(ref state, 256);
                    pooled.WriteByte(b);
                    reference.WriteByte(b);
                    break;
                case 3: // set length
                    var len = NextRandom(ref state, (int)reference.Length + 100);
                    pooled.SetLength(len);
                    reference.SetLength(len);
                    break;
                case 4: // read
                    var count = NextRandom(ref state, 50);
                    var bufA = new byte[count];
                    var bufB = new byte[count];
                    var readA = pooled.Read(bufA, 0, count);
                    var readB = reference.Read(bufB, 0, count);
                    Assert.Equal(readB, readA);
                    Assert.Equal(bufB, bufA);
                    break;
            }

            Assert.Equal(reference.Length, pooled.Length);
            Assert.Equal(reference.Position, pooled.Position);
        }

        Assert.Equal(reference.ToArray(), pooled.ToArray());
    }
}
