using System.Text;

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

        Assert.NotNull(result);
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
        var asyncResult = baseStream.BeginRead(buffer, 0, 3, null, null);
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

        Assert.NotNull(result);
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
        var asyncResult = baseStream.BeginWrite(data, 0, 3, null, null);
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
}
