namespace Meziantou.Framework.Tests;

public sealed class RestrictedStreamTests
{
    [Fact]
    public void CanRead_WhenReadingAllowed_ReturnsTrue()
    {
        using var baseStream = new MemoryStream();
        var options = new RestrictedStreamOptions { AllowReading = true };
        using var restrictedStream = new RestrictedStream(baseStream, options);

        Assert.True(restrictedStream.CanRead);
    }

    [Fact]
    public void CanRead_WhenReadingNotAllowed_ReturnsFalse()
    {
        using var baseStream = new MemoryStream();
        var options = new RestrictedStreamOptions { AllowReading = false };
        using var restrictedStream = new RestrictedStream(baseStream, options);

        Assert.False(restrictedStream.CanRead);
    }

    [Fact]
    public void CanWrite_WhenWritingAllowed_ReturnsTrue()
    {
        using var baseStream = new MemoryStream();
        var options = new RestrictedStreamOptions { AllowWriting = true };
        using var restrictedStream = new RestrictedStream(baseStream, options);

        Assert.True(restrictedStream.CanWrite);
    }

    [Fact]
    public void CanWrite_WhenWritingNotAllowed_ReturnsFalse()
    {
        using var baseStream = new MemoryStream();
        var options = new RestrictedStreamOptions { AllowWriting = false };
        using var restrictedStream = new RestrictedStream(baseStream, options);

        Assert.False(restrictedStream.CanWrite);
    }

    [Fact]
    public void CanSeek_WhenSeekingAllowed_ReturnsTrue()
    {
        using var baseStream = new MemoryStream();
        var options = new RestrictedStreamOptions { AllowSeeking = true };
        using var restrictedStream = new RestrictedStream(baseStream, options);

        Assert.True(restrictedStream.CanSeek);
    }

    [Fact]
    public void CanSeek_WhenSeekingNotAllowed_ReturnsFalse()
    {
        using var baseStream = new MemoryStream();
        var options = new RestrictedStreamOptions { AllowSeeking = false };
        using var restrictedStream = new RestrictedStream(baseStream, options);

        Assert.False(restrictedStream.CanSeek);
    }

    [Fact]
    public void Length_ReturnsUnderlyingStreamLength()
    {
        using var baseStream = new MemoryStream([1, 2, 3, 4, 5]);
        var options = new RestrictedStreamOptions();
        using var restrictedStream = new RestrictedStream(baseStream, options);

        Assert.Equal(5, restrictedStream.Length);
    }

    [Fact]
    public void Position_Get_ReturnsUnderlyingStreamPosition()
    {
        using var baseStream = new MemoryStream([1, 2, 3, 4, 5]);
        baseStream.Position = 3;
        var options = new RestrictedStreamOptions();
        using var restrictedStream = new RestrictedStream(baseStream, options);

        Assert.Equal(3, restrictedStream.Position);
    }

    [Fact]
    public void Position_Set_WhenSeekingAllowed_SetsPosition()
    {
        using var baseStream = new MemoryStream([1, 2, 3, 4, 5]);
        var options = new RestrictedStreamOptions { AllowSeeking = true };
        using var restrictedStream = new RestrictedStream(baseStream, options);

        restrictedStream.Position = 2;

        Assert.Equal(2, restrictedStream.Position);
        Assert.Equal(2, baseStream.Position);
    }

    [Fact]
    public void Position_Set_WhenSeekingNotAllowed_ThrowsNotSupportedException()
    {
        using var baseStream = new MemoryStream([1, 2, 3, 4, 5]);
        var options = new RestrictedStreamOptions { AllowSeeking = false };
        using var restrictedStream = new RestrictedStream(baseStream, options);

        var exception = Assert.Throws<NotSupportedException>(() => restrictedStream.Position = 2);
        Assert.Equal("Seeking is not allowed on this stream.", exception.Message);
    }

    [Fact]
    public void Flush_WhenSynchronousAndWritingAllowed_FlushesStream()
    {
        using var baseStream = new MemoryStream();
        var options = new RestrictedStreamOptions { AllowSynchronousCalls = true, AllowWriting = true };
        using var restrictedStream = new RestrictedStream(baseStream, options);

        restrictedStream.Flush();
    }

    [Fact]
    public void Flush_WhenSynchronousNotAllowed_ThrowsNotSupportedException()
    {
        using var baseStream = new MemoryStream();
        var options = new RestrictedStreamOptions { AllowSynchronousCalls = false, AllowWriting = true };
        using var restrictedStream = new RestrictedStream(baseStream, options);

        var exception = Assert.Throws<NotSupportedException>(() => restrictedStream.Flush());
        Assert.Equal("Synchronous operations are not allowed on this stream.", exception.Message);
    }

    [Fact]
    public void Flush_WhenWritingNotAllowed_ThrowsNotSupportedException()
    {
        using var baseStream = new MemoryStream();
        var options = new RestrictedStreamOptions { AllowSynchronousCalls = true, AllowWriting = false };
        using var restrictedStream = new RestrictedStream(baseStream, options);

        var exception = Assert.Throws<NotSupportedException>(() => restrictedStream.Flush());
        Assert.Equal("Writing is not allowed on this stream.", exception.Message);
    }

    [Fact]
    public void Read_ByteArray_WhenSynchronousAndReadingAllowed_ReadsData()
    {
        using var baseStream = new MemoryStream([1, 2, 3, 4, 5]);
        var options = new RestrictedStreamOptions { AllowSynchronousCalls = true, AllowReading = true };
        using var restrictedStream = new RestrictedStream(baseStream, options);
        var buffer = new byte[3];

        var bytesRead = restrictedStream.Read(buffer, 0, 3);

        Assert.Equal(3, bytesRead);
        Assert.Equal([1, 2, 3], buffer);
    }

    [Fact]
    public void Read_ByteArray_WhenSynchronousNotAllowed_ThrowsNotSupportedException()
    {
        using var baseStream = new MemoryStream([1, 2, 3]);
        var options = new RestrictedStreamOptions { AllowSynchronousCalls = false, AllowReading = true };
        using var restrictedStream = new RestrictedStream(baseStream, options);
        var buffer = new byte[3];

        var exception = Assert.Throws<NotSupportedException>(() => restrictedStream.Read(buffer, 0, 3));
        Assert.Equal("Synchronous operations are not allowed on this stream.", exception.Message);
    }

    [Fact]
    public void Read_ByteArray_WhenReadingNotAllowed_ThrowsNotSupportedException()
    {
        using var baseStream = new MemoryStream([1, 2, 3]);
        var options = new RestrictedStreamOptions { AllowSynchronousCalls = true, AllowReading = false };
        using var restrictedStream = new RestrictedStream(baseStream, options);
        var buffer = new byte[3];

        var exception = Assert.Throws<NotSupportedException>(() => restrictedStream.Read(buffer, 0, 3));
        Assert.Equal("Reading is not allowed on this stream.", exception.Message);
    }

    [Fact]
    public void Read_Span_WhenSynchronousAndReadingAllowed_ReadsData()
    {
        using var baseStream = new MemoryStream([1, 2, 3, 4, 5]);
        var options = new RestrictedStreamOptions { AllowSynchronousCalls = true, AllowReading = true };
        using var restrictedStream = new RestrictedStream(baseStream, options);
        Span<byte> buffer = stackalloc byte[3];

        var bytesRead = restrictedStream.Read(buffer);

        Assert.Equal(3, bytesRead);
        Assert.Equal([1, 2, 3], buffer.ToArray());
    }

    [Fact]
    public void Read_Span_WhenSynchronousNotAllowed_ThrowsNotSupportedException()
    {
        using var baseStream = new MemoryStream([1, 2, 3]);
        var options = new RestrictedStreamOptions { AllowSynchronousCalls = false, AllowReading = true };
        using var restrictedStream = new RestrictedStream(baseStream, options);
        var buffer = new byte[3];

        var exception = Assert.Throws<NotSupportedException>(() => restrictedStream.Read(buffer.AsSpan()));
        Assert.Equal("Synchronous operations are not allowed on this stream.", exception.Message);
    }

    [Fact]
    public void ReadByte_WhenSynchronousAndReadingAllowed_ReadsData()
    {
        using var baseStream = new MemoryStream([42]);
        var options = new RestrictedStreamOptions { AllowSynchronousCalls = true, AllowReading = true };
        using var restrictedStream = new RestrictedStream(baseStream, options);

        var value = restrictedStream.ReadByte();

        Assert.Equal(42, value);
    }

    [Fact]
    public void ReadByte_WhenReadingNotAllowed_ThrowsNotSupportedException()
    {
        using var baseStream = new MemoryStream([42]);
        var options = new RestrictedStreamOptions { AllowSynchronousCalls = true, AllowReading = false };
        using var restrictedStream = new RestrictedStream(baseStream, options);

        var exception = Assert.Throws<NotSupportedException>(() => restrictedStream.ReadByte());
        Assert.Equal("Reading is not allowed on this stream.", exception.Message);
    }

    [Fact]
    public async Task ReadAsync_ByteArray_WhenAsynchronousAndReadingAllowed_ReadsData()
    {
        using var baseStream = new MemoryStream([1, 2, 3, 4, 5]);
        var options = new RestrictedStreamOptions { AllowAsynchronousCalls = true, AllowReading = true };
        using var restrictedStream = new RestrictedStream(baseStream, options);
        var buffer = new byte[3];

        var bytesRead = await restrictedStream.ReadAsync(buffer.AsMemory());

        Assert.Equal(3, bytesRead);
        Assert.Equal([1, 2, 3], buffer);
    }

    [Fact]
    public async Task ReadAsync_ByteArray_WhenAsynchronousNotAllowed_ThrowsNotSupportedException()
    {
        using var baseStream = new MemoryStream([1, 2, 3]);
        var options = new RestrictedStreamOptions { AllowAsynchronousCalls = false, AllowReading = true };
        using var restrictedStream = new RestrictedStream(baseStream, options);
        var buffer = new byte[3];

        var exception = await Assert.ThrowsAsync<NotSupportedException>(() => restrictedStream.ReadAsync(buffer, 0, 3));
        Assert.Equal("Asynchronous operations are not allowed on this stream.", exception.Message);
    }

    [Fact]
    public async Task ReadAsync_Memory_WhenAsynchronousAndReadingAllowed_ReadsData()
    {
        using var baseStream = new MemoryStream([1, 2, 3, 4, 5]);
        var options = new RestrictedStreamOptions { AllowAsynchronousCalls = true, AllowReading = true };
        using var restrictedStream = new RestrictedStream(baseStream, options);
        var buffer = new byte[3];

        var bytesRead = await restrictedStream.ReadAsync(buffer.AsMemory());

        Assert.Equal(3, bytesRead);
        Assert.Equal([1, 2, 3], buffer);
    }

    [Fact]
    public async Task ReadAsync_Memory_WhenReadingNotAllowed_ThrowsNotSupportedException()
    {
        using var baseStream = new MemoryStream([1, 2, 3]);
        var options = new RestrictedStreamOptions { AllowAsynchronousCalls = true, AllowReading = false };
        using var restrictedStream = new RestrictedStream(baseStream, options);
        var buffer = new byte[3];

        var exception = await Assert.ThrowsAsync<NotSupportedException>(() => restrictedStream.ReadAsync(buffer.AsMemory()).AsTask());
        Assert.Equal("Reading is not allowed on this stream.", exception.Message);
    }

    [Fact]
    public void Write_ByteArray_WhenSynchronousAndWritingAllowed_WritesData()
    {
        using var baseStream = new MemoryStream();
        var options = new RestrictedStreamOptions { AllowSynchronousCalls = true, AllowWriting = true };
        using var restrictedStream = new RestrictedStream(baseStream, options);
        var data = new byte[] { 1, 2, 3 };

        restrictedStream.Write(data, 0, 3);

        Assert.Equal([1, 2, 3], baseStream.ToArray());
    }

    [Fact]
    public void Write_ByteArray_WhenSynchronousNotAllowed_ThrowsNotSupportedException()
    {
        using var baseStream = new MemoryStream();
        var options = new RestrictedStreamOptions { AllowSynchronousCalls = false, AllowWriting = true };
        using var restrictedStream = new RestrictedStream(baseStream, options);
        var data = new byte[] { 1, 2, 3 };

        var exception = Assert.Throws<NotSupportedException>(() => restrictedStream.Write(data, 0, 3));
        Assert.Equal("Synchronous operations are not allowed on this stream.", exception.Message);
    }

    [Fact]
    public void Write_ByteArray_WhenWritingNotAllowed_ThrowsNotSupportedException()
    {
        using var baseStream = new MemoryStream();
        var options = new RestrictedStreamOptions { AllowSynchronousCalls = true, AllowWriting = false };
        using var restrictedStream = new RestrictedStream(baseStream, options);
        var data = new byte[] { 1, 2, 3 };

        var exception = Assert.Throws<NotSupportedException>(() => restrictedStream.Write(data, 0, 3));
        Assert.Equal("Writing is not allowed on this stream.", exception.Message);
    }

    [Fact]
    public void Write_Span_WhenSynchronousAndWritingAllowed_WritesData()
    {
        using var baseStream = new MemoryStream();
        var options = new RestrictedStreamOptions { AllowSynchronousCalls = true, AllowWriting = true };
        using var restrictedStream = new RestrictedStream(baseStream, options);
        ReadOnlySpan<byte> data = [1, 2, 3];

        restrictedStream.Write(data);

        Assert.Equal([1, 2, 3], baseStream.ToArray());
    }

    [Fact]
    public void Write_Span_WhenWritingNotAllowed_ThrowsNotSupportedException()
    {
        using var baseStream = new MemoryStream();
        var options = new RestrictedStreamOptions { AllowSynchronousCalls = true, AllowWriting = false };
        using var restrictedStream = new RestrictedStream(baseStream, options);
        var data = new byte[] { 1, 2, 3 };

        var exception = Assert.Throws<NotSupportedException>(() => restrictedStream.Write(data.AsSpan()));
        Assert.Equal("Writing is not allowed on this stream.", exception.Message);
    }

    [Fact]
    public void WriteByte_WhenSynchronousAndWritingAllowed_WritesData()
    {
        using var baseStream = new MemoryStream();
        var options = new RestrictedStreamOptions { AllowSynchronousCalls = true, AllowWriting = true };
        using var restrictedStream = new RestrictedStream(baseStream, options);

        restrictedStream.WriteByte(42);

        Assert.Equal([42], baseStream.ToArray());
    }

    [Fact]
    public void WriteByte_WhenWritingNotAllowed_ThrowsNotSupportedException()
    {
        using var baseStream = new MemoryStream();
        var options = new RestrictedStreamOptions { AllowSynchronousCalls = true, AllowWriting = false };
        using var restrictedStream = new RestrictedStream(baseStream, options);

        var exception = Assert.Throws<NotSupportedException>(() => restrictedStream.WriteByte(42));
        Assert.Equal("Writing is not allowed on this stream.", exception.Message);
    }

    [Fact]
    public async Task WriteAsync_ByteArray_WhenAsynchronousAndWritingAllowed_WritesData()
    {
        using var baseStream = new MemoryStream();
        var options = new RestrictedStreamOptions { AllowAsynchronousCalls = true, AllowWriting = true };
        using var restrictedStream = new RestrictedStream(baseStream, options);
        var data = new byte[] { 1, 2, 3 };

        await restrictedStream.WriteAsync(data.AsMemory());

        Assert.Equal([1, 2, 3], baseStream.ToArray());
    }

    [Fact]
    public async Task WriteAsync_ByteArray_WhenAsynchronousNotAllowed_ThrowsNotSupportedException()
    {
        using var baseStream = new MemoryStream();
        var options = new RestrictedStreamOptions { AllowAsynchronousCalls = false, AllowWriting = true };
        using var restrictedStream = new RestrictedStream(baseStream, options);
        var data = new byte[] { 1, 2, 3 };

        var exception = await Assert.ThrowsAsync<NotSupportedException>(() => restrictedStream.WriteAsync(data, 0, 3));
        Assert.Equal("Asynchronous operations are not allowed on this stream.", exception.Message);
    }

    [Fact]
    public async Task WriteAsync_Memory_WhenAsynchronousAndWritingAllowed_WritesData()
    {
        using var baseStream = new MemoryStream();
        var options = new RestrictedStreamOptions { AllowAsynchronousCalls = true, AllowWriting = true };
        using var restrictedStream = new RestrictedStream(baseStream, options);
        var data = new byte[] { 1, 2, 3 };

        await restrictedStream.WriteAsync(data.AsMemory());

        Assert.Equal([1, 2, 3], baseStream.ToArray());
    }

    [Fact]
    public async Task WriteAsync_Memory_WhenWritingNotAllowed_ThrowsNotSupportedException()
    {
        using var baseStream = new MemoryStream();
        var options = new RestrictedStreamOptions { AllowAsynchronousCalls = true, AllowWriting = false };
        using var restrictedStream = new RestrictedStream(baseStream, options);
        var data = new byte[] { 1, 2, 3 };

        var exception = await Assert.ThrowsAsync<NotSupportedException>(() => restrictedStream.WriteAsync(data.AsMemory()).AsTask());
        Assert.Equal("Writing is not allowed on this stream.", exception.Message);
    }

    [Fact]
    public void Seek_WhenSeekingAllowed_SeeksPosition()
    {
        using var baseStream = new MemoryStream([1, 2, 3, 4, 5]);
        var options = new RestrictedStreamOptions { AllowSeeking = true };
        using var restrictedStream = new RestrictedStream(baseStream, options);

        var newPosition = restrictedStream.Seek(2, SeekOrigin.Begin);

        Assert.Equal(2, newPosition);
        Assert.Equal(2, restrictedStream.Position);
    }

    [Fact]
    public void Seek_WhenSeekingNotAllowed_ThrowsNotSupportedException()
    {
        using var baseStream = new MemoryStream([1, 2, 3, 4, 5]);
        var options = new RestrictedStreamOptions { AllowSeeking = false };
        using var restrictedStream = new RestrictedStream(baseStream, options);

        var exception = Assert.Throws<NotSupportedException>(() => restrictedStream.Seek(2, SeekOrigin.Begin));
        Assert.Equal("Seeking is not allowed on this stream.", exception.Message);
    }

    [Fact]
    public void SetLength_WhenWritingAllowed_SetsLength()
    {
        using var baseStream = new MemoryStream();
        var options = new RestrictedStreamOptions { AllowWriting = true };
        using var restrictedStream = new RestrictedStream(baseStream, options);

        restrictedStream.SetLength(10);

        Assert.Equal(10, restrictedStream.Length);
        Assert.Equal(10, baseStream.Length);
    }

    [Fact]
    public void SetLength_WhenWritingNotAllowed_ThrowsNotSupportedException()
    {
        using var baseStream = new MemoryStream();
        var options = new RestrictedStreamOptions { AllowWriting = false };
        using var restrictedStream = new RestrictedStream(baseStream, options);

        var exception = Assert.Throws<NotSupportedException>(() => restrictedStream.SetLength(10));
        Assert.Equal("Writing is not allowed on this stream.", exception.Message);
    }

    [Fact]
    public void CopyTo_WhenSynchronousAndReadingAllowed_CopiesData()
    {
        using var baseStream = new MemoryStream([1, 2, 3, 4, 5]);
        var options = new RestrictedStreamOptions { AllowSynchronousCalls = true, AllowReading = true };
        using var restrictedStream = new RestrictedStream(baseStream, options);
        using var destination = new MemoryStream();

        restrictedStream.CopyTo(destination, 4096);

        Assert.Equal([1, 2, 3, 4, 5], destination.ToArray());
    }

    [Fact]
    public void CopyTo_WhenSynchronousNotAllowed_ThrowsNotSupportedException()
    {
        using var baseStream = new MemoryStream([1, 2, 3]);
        var options = new RestrictedStreamOptions { AllowSynchronousCalls = false, AllowReading = true };
        using var restrictedStream = new RestrictedStream(baseStream, options);
        using var destination = new MemoryStream();

        var exception = Assert.Throws<NotSupportedException>(() => restrictedStream.CopyTo(destination, 4096));
        Assert.Equal("Synchronous operations are not allowed on this stream.", exception.Message);
    }

    [Fact]
    public void CopyTo_WhenReadingNotAllowed_ThrowsNotSupportedException()
    {
        using var baseStream = new MemoryStream([1, 2, 3]);
        var options = new RestrictedStreamOptions { AllowSynchronousCalls = true, AllowReading = false };
        using var restrictedStream = new RestrictedStream(baseStream, options);
        using var destination = new MemoryStream();

        var exception = Assert.Throws<NotSupportedException>(() => restrictedStream.CopyTo(destination, 4096));
        Assert.Equal("Reading is not allowed on this stream.", exception.Message);
    }

    [Fact]
    public async Task CopyToAsync_WhenAsynchronousAndReadingAllowed_CopiesData()
    {
        using var baseStream = new MemoryStream([1, 2, 3, 4, 5]);
        var options = new RestrictedStreamOptions { AllowAsynchronousCalls = true, AllowReading = true };
        using var restrictedStream = new RestrictedStream(baseStream, options);
        using var destination = new MemoryStream();

        await restrictedStream.CopyToAsync(destination, 4096);

        Assert.Equal([1, 2, 3, 4, 5], destination.ToArray());
    }

    [Fact]
    public async Task CopyToAsync_WhenAsynchronousNotAllowed_ThrowsNotSupportedException()
    {
        using var baseStream = new MemoryStream([1, 2, 3]);
        var options = new RestrictedStreamOptions { AllowAsynchronousCalls = false, AllowReading = true };
        using var restrictedStream = new RestrictedStream(baseStream, options);
        using var destination = new MemoryStream();

        var exception = await Assert.ThrowsAsync<NotSupportedException>(() => restrictedStream.CopyToAsync(destination, 4096));
        Assert.Equal("Asynchronous operations are not allowed on this stream.", exception.Message);
    }

    [Fact]
    public async Task FlushAsync_WhenAsynchronousAndWritingAllowed_FlushesStream()
    {
        using var baseStream = new MemoryStream();
        var options = new RestrictedStreamOptions { AllowAsynchronousCalls = true, AllowWriting = true };
        using var restrictedStream = new RestrictedStream(baseStream, options);

        await restrictedStream.FlushAsync();
    }

    [Fact]
    public async Task FlushAsync_WhenAsynchronousNotAllowed_ThrowsNotSupportedException()
    {
        using var baseStream = new MemoryStream();
        var options = new RestrictedStreamOptions { AllowAsynchronousCalls = false, AllowWriting = true };
        using var restrictedStream = new RestrictedStream(baseStream, options);

        var exception = await Assert.ThrowsAsync<NotSupportedException>(() => restrictedStream.FlushAsync());
        Assert.Equal("Asynchronous operations are not allowed on this stream.", exception.Message);
    }

    [Fact]
    public void BeginRead_WhenAsynchronousAndReadingAllowed_BeginsRead()
    {
        using var baseStream = new MemoryStream([1, 2, 3, 4, 5]);
        var options = new RestrictedStreamOptions { AllowAsynchronousCalls = true, AllowReading = true };
        using var restrictedStream = new RestrictedStream(baseStream, options);
        var buffer = new byte[3];

        var result = restrictedStream.BeginRead(buffer, 0, 3, null, null);
        result.AsyncWaitHandle.WaitOne();
        var bytesRead = restrictedStream.EndRead(result);

        Assert.NotNull(result);
        Assert.Equal(3, bytesRead);
        Assert.Equal([1, 2, 3], buffer);
    }

    [Fact]
    public void BeginRead_WhenAsynchronousNotAllowed_ThrowsNotSupportedException()
    {
        using var baseStream = new MemoryStream([1, 2, 3]);
        var options = new RestrictedStreamOptions { AllowAsynchronousCalls = false, AllowReading = true };
        using var restrictedStream = new RestrictedStream(baseStream, options);
        var buffer = new byte[3];

        var exception = Assert.Throws<NotSupportedException>(() => restrictedStream.BeginRead(buffer, 0, 3, null, null));
        Assert.Equal("Asynchronous operations are not allowed on this stream.", exception.Message);
    }

    [Fact]
    public void BeginRead_WhenReadingNotAllowed_ThrowsNotSupportedException()
    {
        using var baseStream = new MemoryStream([1, 2, 3]);
        var options = new RestrictedStreamOptions { AllowAsynchronousCalls = true, AllowReading = false };
        using var restrictedStream = new RestrictedStream(baseStream, options);
        var buffer = new byte[3];

        var exception = Assert.Throws<NotSupportedException>(() => restrictedStream.BeginRead(buffer, 0, 3, null, null));
        Assert.Equal("Reading is not allowed on this stream.", exception.Message);
    }

    [Fact]
    public void EndRead_WhenAsynchronousAndReadingAllowed_EndsRead()
    {
        using var baseStream = new MemoryStream([1, 2, 3, 4, 5]);
        var options = new RestrictedStreamOptions { AllowAsynchronousCalls = true, AllowReading = true };
        using var restrictedStream = new RestrictedStream(baseStream, options);
        var buffer = new byte[3];
        var asyncResult = restrictedStream.BeginRead(buffer, 0, 3, null, null);
        asyncResult.AsyncWaitHandle.WaitOne();

        var bytesRead = restrictedStream.EndRead(asyncResult);

        Assert.Equal(3, bytesRead);
        Assert.Equal([1, 2, 3], buffer);
    }

    [Fact]
    public void EndRead_WhenAsynchronousNotAllowed_ThrowsNotSupportedException()
    {
        using var baseStream = new MemoryStream([1, 2, 3]);
        var options = new RestrictedStreamOptions { AllowAsynchronousCalls = false, AllowReading = true };
        using var restrictedStream = new RestrictedStream(baseStream, options);

        var exception = Assert.Throws<NotSupportedException>(() => restrictedStream.EndRead(null!));
        Assert.Equal("Asynchronous operations are not allowed on this stream.", exception.Message);
    }

    [Fact]
    public void BeginWrite_WhenAsynchronousAndWritingAllowed_BeginsWrite()
    {
        using var baseStream = new MemoryStream();
        var options = new RestrictedStreamOptions { AllowAsynchronousCalls = true, AllowWriting = true };
        using var restrictedStream = new RestrictedStream(baseStream, options);
        var data = new byte[] { 1, 2, 3 };

        var result = restrictedStream.BeginWrite(data, 0, 3, null, null);
        result.AsyncWaitHandle.WaitOne();
        restrictedStream.EndWrite(result);

        Assert.NotNull(result);
        Assert.Equal([1, 2, 3], baseStream.ToArray());
    }

    [Fact]
    public void BeginWrite_WhenAsynchronousNotAllowed_ThrowsNotSupportedException()
    {
        using var baseStream = new MemoryStream();
        var options = new RestrictedStreamOptions { AllowAsynchronousCalls = false, AllowWriting = true };
        using var restrictedStream = new RestrictedStream(baseStream, options);
        var data = new byte[] { 1, 2, 3 };

        var exception = Assert.Throws<NotSupportedException>(() => restrictedStream.BeginWrite(data, 0, 3, null, null));
        Assert.Equal("Asynchronous operations are not allowed on this stream.", exception.Message);
    }

    [Fact]
    public void BeginWrite_WhenWritingNotAllowed_ThrowsNotSupportedException()
    {
        using var baseStream = new MemoryStream();
        var options = new RestrictedStreamOptions { AllowAsynchronousCalls = true, AllowWriting = false };
        using var restrictedStream = new RestrictedStream(baseStream, options);
        var data = new byte[] { 1, 2, 3 };

        var exception = Assert.Throws<NotSupportedException>(() => restrictedStream.BeginWrite(data, 0, 3, null, null));
        Assert.Equal("Writing is not allowed on this stream.", exception.Message);
    }

    [Fact]
    public void EndWrite_WhenAsynchronousAndWritingAllowed_EndsWrite()
    {
        using var baseStream = new MemoryStream();
        var options = new RestrictedStreamOptions { AllowAsynchronousCalls = true, AllowWriting = true };
        using var restrictedStream = new RestrictedStream(baseStream, options);
        var data = new byte[] { 1, 2, 3 };
        var asyncResult = restrictedStream.BeginWrite(data, 0, 3, null, null);
        asyncResult.AsyncWaitHandle.WaitOne();

        restrictedStream.EndWrite(asyncResult);

        Assert.Equal([1, 2, 3], baseStream.ToArray());
    }

    [Fact]
    public void EndWrite_WhenAsynchronousNotAllowed_ThrowsNotSupportedException()
    {
        using var baseStream = new MemoryStream();
        var options = new RestrictedStreamOptions { AllowAsynchronousCalls = false, AllowWriting = true };
        using var restrictedStream = new RestrictedStream(baseStream, options);

        var exception = Assert.Throws<NotSupportedException>(() => restrictedStream.EndWrite(null!));
        Assert.Equal("Asynchronous operations are not allowed on this stream.", exception.Message);
    }

    [Fact]
    public void Dispose_DisposesUnderlyingStream()
    {
        var baseStream = new MemoryStream();
        var options = new RestrictedStreamOptions();
        var restrictedStream = new RestrictedStream(baseStream, options);

        restrictedStream.Dispose();

        Assert.Throws<ObjectDisposedException>(() => baseStream.ReadByte());
    }

    [Fact]
    public async Task DisposeAsync_DisposesUnderlyingStream()
    {
        var baseStream = new MemoryStream();
        var options = new RestrictedStreamOptions();
        var restrictedStream = new RestrictedStream(baseStream, options);

        await restrictedStream.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() => baseStream.ReadByte());
    }

    [Fact]
    public void Read_ByteArray_WhenMaxReadLengthSet_ReturnsAtMostMaxReadLength()
    {
        using var baseStream = new MemoryStream([1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);
        var options = new RestrictedStreamOptions { AllowSynchronousCalls = true, AllowReading = true, MaxReadLength = 5 };
        using var restrictedStream = new RestrictedStream(baseStream, options);
        var buffer = new byte[10];

        var bytesRead = restrictedStream.Read(buffer, 0, 10);

        Assert.Equal(5, bytesRead);
        Assert.Equal([1, 2, 3, 4, 5], buffer.AsSpan(0, 5));
    }

    [Fact]
    public void Read_ByteArray_WhenMaxReadLengthNegative_ReadsAllRequested()
    {
        using var baseStream = new MemoryStream([1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);
        var options = new RestrictedStreamOptions { AllowSynchronousCalls = true, AllowReading = true, MaxReadLength = -1 };
        using var restrictedStream = new RestrictedStream(baseStream, options);
        var buffer = new byte[10];

        var bytesRead = restrictedStream.Read(buffer, 0, 10);

        Assert.Equal(10, bytesRead);
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 9, 10], buffer);
    }

    [Fact]
    public void Read_ByteArray_WhenMaxReadLengthZero_ReadsAllRequested()
    {
        using var baseStream = new MemoryStream([1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);
        var options = new RestrictedStreamOptions { AllowSynchronousCalls = true, AllowReading = true, MaxReadLength = 0 };
        using var restrictedStream = new RestrictedStream(baseStream, options);
        var buffer = new byte[10];

        var bytesRead = restrictedStream.Read(buffer, 0, 10);

        Assert.Equal(10, bytesRead);
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 9, 10], buffer);
    }

    [Fact]
    public void Read_Span_WhenMaxReadLengthSet_ReturnsAtMostMaxReadLength()
    {
        using var baseStream = new MemoryStream([1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);
        var options = new RestrictedStreamOptions { AllowSynchronousCalls = true, AllowReading = true, MaxReadLength = 5 };
        using var restrictedStream = new RestrictedStream(baseStream, options);
        Span<byte> buffer = stackalloc byte[10];

        var bytesRead = restrictedStream.Read(buffer);

        Assert.Equal(5, bytesRead);
        Assert.Equal([1, 2, 3, 4, 5], buffer[..5].ToArray());
    }

    [Fact]
    public async Task ReadAsync_ByteArray_WhenMaxReadLengthSet_ReturnsAtMostMaxReadLength()
    {
        using var baseStream = new MemoryStream([1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);
        var options = new RestrictedStreamOptions { AllowAsynchronousCalls = true, AllowReading = true, MaxReadLength = 5 };
        using var restrictedStream = new RestrictedStream(baseStream, options);
        var buffer = new byte[10];

        var bytesRead = await restrictedStream.ReadAsync(buffer.AsMemory(0, 10));

        Assert.Equal(5, bytesRead);
        Assert.Equal([1, 2, 3, 4, 5], buffer.AsSpan(0, 5));
    }

    [Fact]
    public async Task ReadAsync_Memory_WhenMaxReadLengthSet_ReturnsAtMostMaxReadLength()
    {
        using var baseStream = new MemoryStream([1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);
        var options = new RestrictedStreamOptions { AllowAsynchronousCalls = true, AllowReading = true, MaxReadLength = 5 };
        using var restrictedStream = new RestrictedStream(baseStream, options);
        var buffer = new byte[10];

        var bytesRead = await restrictedStream.ReadAsync(buffer.AsMemory());

        Assert.Equal(5, bytesRead);
        Assert.Equal([1, 2, 3, 4, 5], buffer.AsSpan(0, 5));
    }

    [Fact]
    public void BeginRead_WhenMaxReadLengthSet_ReturnsAtMostMaxReadLength()
    {
        using var baseStream = new MemoryStream([1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);
        var options = new RestrictedStreamOptions { AllowAsynchronousCalls = true, AllowReading = true, MaxReadLength = 5 };
        using var restrictedStream = new RestrictedStream(baseStream, options);
        var buffer = new byte[10];
        var asyncResult = restrictedStream.BeginRead(buffer, 0, 5, null, null);
        asyncResult.AsyncWaitHandle.WaitOne();

        var bytesRead = restrictedStream.EndRead(asyncResult);

        Assert.Equal(5, bytesRead);
        Assert.Equal([1, 2, 3, 4, 5], buffer.AsSpan(0, 5));
    }

    [Fact]
    public void Read_ByteArray_WhenMaxReadLengthGreaterThanRequest_ReadsRequestedAmount()
    {
        using var baseStream = new MemoryStream([1, 2, 3, 4, 5]);
        var options = new RestrictedStreamOptions { AllowSynchronousCalls = true, AllowReading = true, MaxReadLength = 10 };
        using var restrictedStream = new RestrictedStream(baseStream, options);
        var buffer = new byte[5];

        var bytesRead = restrictedStream.Read(buffer, 0, 5);

        Assert.Equal(5, bytesRead);
        Assert.Equal([1, 2, 3, 4, 5], buffer);
    }

    [Fact]
    public void Read_ByteArray_WithMaxReadLength_CanReadMultipleTimes()
    {
        using var baseStream = new MemoryStream([1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);
        var options = new RestrictedStreamOptions { AllowSynchronousCalls = true, AllowReading = true, MaxReadLength = 3 };
        using var restrictedStream = new RestrictedStream(baseStream, options);
        var buffer = new byte[10];

        var bytesRead1 = restrictedStream.Read(buffer, 0, 10);
        var bytesRead2 = restrictedStream.Read(buffer, bytesRead1, 10);
        var bytesRead3 = restrictedStream.Read(buffer, bytesRead1 + bytesRead2, 10);

        Assert.Equal(3, bytesRead1);
        Assert.Equal(3, bytesRead2);
        Assert.Equal(3, bytesRead3);
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 9], buffer.AsSpan(0, 9));
    }

    [Fact]
    public void CopyTo_HonorsMaxReadLength()
    {
        var inner = new CountingStream(new MemoryStream(new byte[1000]));
        using var stream = new RestrictedStream(inner, new RestrictedStreamOptions
        {
            AllowSynchronousCalls = true,
            AllowReading = true,
            MaxReadLength = 10,
        });

        using var destination = new MemoryStream();
        stream.CopyTo(destination, bufferSize: 256);

        Assert.Equal(1000, destination.Length);
        Assert.True(inner.MaxReadRequested <= 10, $"A single read asked for {inner.MaxReadRequested} bytes, above the 10-byte cap");
    }

    [Fact]
    public async Task CopyToAsync_HonorsMaxReadLength()
    {
        var inner = new CountingStream(new MemoryStream(new byte[1000]));
        await using var stream = new RestrictedStream(inner, new RestrictedStreamOptions
        {
            AllowAsynchronousCalls = true,
            AllowReading = true,
            MaxReadLength = 10,
        });

        using var destination = new MemoryStream();
        await stream.CopyToAsync(destination, bufferSize: 256);

        Assert.Equal(1000, destination.Length);
        Assert.True(inner.MaxReadRequested <= 10, $"A single read asked for {inner.MaxReadRequested} bytes, above the 10-byte cap");
    }

    [Fact]
    public async Task DisposeAsync_DisposesTheWrappedStreamOnce()
    {
        var inner = new CountingStream(new MemoryStream());
        var stream = new RestrictedStream(inner, new RestrictedStreamOptions { AllowReading = true });

        await stream.DisposeAsync();

        Assert.Equal(1, inner.DisposeCount);
    }

    [Fact]
    public void Dispose_DisposesTheWrappedStreamOnce()
    {
        var inner = new CountingStream(new MemoryStream());
        var stream = new RestrictedStream(inner, new RestrictedStreamOptions { AllowReading = true });

        stream.Dispose();
        stream.Dispose();

        Assert.Equal(1, inner.DisposeCount);
    }

    private sealed class CountingStream(Stream inner) : Stream
    {
        private bool _insideDisposeAsync;

        public int MaxReadRequested { get; private set; }
        public int DisposeCount { get; private set; }

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush();
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);

        public override int Read(byte[] buffer, int offset, int count)
        {
            MaxReadRequested = Math.Max(MaxReadRequested, count);
            return inner.Read(buffer, offset, count);
        }

        public override int Read(Span<byte> buffer)
        {
            MaxReadRequested = Math.Max(MaxReadRequested, buffer.Length);
            return inner.Read(buffer);
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            MaxReadRequested = Math.Max(MaxReadRequested, buffer.Length);
            return inner.ReadAsync(buffer, cancellationToken);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            MaxReadRequested = Math.Max(MaxReadRequested, count);
            return inner.ReadAsync(buffer, offset, count, cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            // Stream.DisposeAsync routes through Dispose, so that inner call is not counted twice
            if (!_insideDisposeAsync)
            {
                DisposeCount++;
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            DisposeCount++;
            _insideDisposeAsync = true;
            try
            {
                await base.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                _insideDisposeAsync = false;
            }
        }
    }
}
