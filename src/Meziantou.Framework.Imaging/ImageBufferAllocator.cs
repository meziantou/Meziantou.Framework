namespace Meziantou.Framework.Imaging;

/// <summary>
/// Provides the byte arrays the library uses for pixels, decoders, encoders and scratch memory, in place of the
/// process-wide pool (<see cref="ImageBufferPool"/>), for the images and operations of a configuration
/// (<see cref="ImageConfiguration.BufferAllocator"/>).
/// </summary>
/// <remarks>
/// <para>
/// Use it to take the memory of the library from a pool of your own (an <see cref="System.Buffers.ArrayPool{T}"/>,
/// pinned arrays shared with native code, a pool per tenant), or to measure what it allocates. The resource limits are
/// unchanged: before renting a buffer, the library charges <see cref="GetBufferLength"/> bytes to the operation and
/// rejects the request with an <see cref="ImageResourceLimitException"/> when it exceeds
/// <see cref="ImageResourceLimits.MaxLiveAllocationBytes"/>, so the allocator is never asked for more than the limits
/// allow.
/// </para>
/// <para>
/// The library rents an array with <see cref="Rent"/>, uses its first <c>length</c> bytes, and returns it exactly once
/// with <see cref="Return"/> when the image is disposed or the operation ends, after which it never reads or writes it.
/// It clears the bytes it exposes, so <see cref="Rent"/> may return arrays with any content. An array whose length is not
/// the one <see cref="GetBufferLength"/> declared is returned and the operation fails with an
/// <see cref="InvalidOperationException"/>. An exception of <see cref="Rent"/>, such as an
/// <see cref="OutOfMemoryException"/>, fails the operation and releases what it acquired.
/// </para>
/// <para>
/// The methods are called concurrently by the images and the operations that use the configuration (and by the
/// workers of an operation that runs in parallel): an implementation must be thread-safe. Requests are from 1 byte to
/// <see cref="Array.MaxLength"/> bytes. The memory that <see cref="Image.WrapMemory(PixelFormat, Memory{byte}, int, int, int, Action?, ImageConfiguration?)"/>
/// wraps is the caller's, and never comes from the allocator.
/// </para>
/// </remarks>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Imaging.Tests/AllocationScopeTests.cs" ref:region="ImageBufferAllocator_Usage" ref:dedent="true">
/// var allocator = new PinnedAllocator();
/// var configuration = new ImageConfiguration { BufferAllocator = allocator };
/// using (var image = new Image&lt;Rgba32&gt;(64, 64, configuration))
/// {
///     image.GaussianBlur(2);
///     using var clone = image.CloneAs&lt;Rgba64&gt;();
///     Assert.True(allocator.LiveBytes &gt; 0);
/// }
///
/// // Every array is returned once the images are disposed
/// Assert.Equal(0, allocator.LiveBytes);
/// </code>
/// </example>
public abstract class ImageBufferAllocator
{
    /// <summary>Initializes a new instance of the <see cref="ImageBufferAllocator"/> class.</summary>
    protected ImageBufferAllocator()
    {
    }

    /// <summary>Gets the length of the array <see cref="Rent"/> returns for a request, which is the size the library charges.</summary>
    /// <param name="length">The requested length, from 1 to <see cref="Array.MaxLength"/>.</param>
    /// <returns>The length of the array, from <paramref name="length"/> to <see cref="Array.MaxLength"/>. The default is <paramref name="length"/>.</returns>
    public virtual int GetBufferLength(int length) => length;

    /// <summary>Rents an array of <see cref="GetBufferLength"/> bytes.</summary>
    /// <param name="length">The requested length, from 1 to <see cref="Array.MaxLength"/>.</param>
    /// <returns>An array of exactly <see cref="GetBufferLength"/> bytes, with any content.</returns>
    public abstract byte[] Rent(int length);

    /// <summary>Returns an array that <see cref="Rent"/> gave. The library does not use it afterward.</summary>
    /// <param name="buffer">The array.</param>
    public abstract void Return(byte[] buffer);
}
