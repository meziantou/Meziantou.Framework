namespace Meziantou.Framework.WebPlatform.Scripting;

/// <summary>
/// A growable shared data block of the script engine whose bytes never move (the bytes of a shared
/// <c>WebAssembly.Memory</c>, which the native code of a WebAssembly runtime reads and writes), created by
/// <see cref="IJsAdapter{TValue}.CreateGrowableSharedDataBlock"/>. Its members can be called from any thread.
/// </summary>
public interface IGrowableSharedDataBlock
{
    /// <summary>The data block of the <c>SharedArrayBuffer</c> objects of the block (see <see cref="IJsAdapter{TValue}.GetSharedDataBlock"/>).</summary>
    object DataBlock { get; }

    /// <summary>The address of the first byte, which never changes while the block is alive.</summary>
    nint Address { get; }

    /// <summary>The current length of the block in bytes.</summary>
    int ByteLength { get; }

    /// <summary>
    /// Grows the block to <paramref name="newByteLength"/> bytes (the new bytes are zeroed), and returns false when it is
    /// less than the current length or greater than the maximum length of the block.
    /// </summary>
    bool TryGrow(int newByteLength);

    /// <summary>
    /// Makes <c>Atomics.wait</c>, <c>Atomics.waitAsync</c> and <c>Atomics.notify</c> use <paramref name="waiters"/> for the
    /// <c>SharedArrayBuffer</c> objects of the block (the waiters of the WebAssembly runtime). It is called before the scripts
    /// get a buffer of the block.
    /// </summary>
    void SetWaiters(ISharedDataBlockWaiters waiters);
}
