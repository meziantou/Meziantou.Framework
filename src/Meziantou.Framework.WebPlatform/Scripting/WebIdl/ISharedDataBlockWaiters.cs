namespace Meziantou.Framework.WebPlatform.Scripting;

/// <summary>
/// The waiters of a shared data block whose memory is shared with a WebAssembly runtime (the bytes of a shared
/// <c>WebAssembly.Memory</c>), which <c>Atomics.wait</c>, <c>Atomics.waitAsync</c> and <c>Atomics.notify</c> use instead of
/// the waiters of the script engine (see <see cref="IGrowableSharedDataBlock.SetWaiters"/>): they notify the
/// waiters of the <c>memory.atomic.wait32</c> and <c>memory.atomic.wait64</c> instructions, and are notified by the
/// <c>memory.atomic.notify</c> instruction.
/// </summary>
public interface ISharedDataBlockWaiters
{
    /// <summary>Notifies the waiters of a location of the block (<c>Atomics.notify</c>), and returns the number of waiters notified.</summary>
    /// <param name="byteIndex">The index of the location in the block: a multiple of 4 within the block.</param>
    /// <param name="count">The maximum number of waiters to notify; <see cref="int.MaxValue"/> notifies all of them.</param>
    int Notify(int byteIndex, int count);

    /// <summary>
    /// Blocks the calling thread until the waiter of a location of the block is notified, when the location contains
    /// <paramref name="expectedValue"/> (<c>Atomics.wait</c>). A location that no longer contains the expected value
    /// completes the wait as a notification (the script engine checked the value before).
    /// </summary>
    /// <param name="byteIndex">The index of the location in the block: a multiple of <paramref name="size"/> within the block.</param>
    /// <param name="expectedValue">The expected value of the location (sign-extended for a 32-bit location).</param>
    /// <param name="size">The size of the location in bytes: 4 or 8.</param>
    /// <param name="timeout">The maximum duration of the wait, or <see langword="null"/> to wait until notified.</param>
    /// <param name="cancellationToken">A token that stops the wait.</param>
    /// <returns><see langword="true"/> when the waiter was notified; <see langword="false"/> when the wait timed out or was canceled.</returns>
    bool Wait(int byteIndex, long expectedValue, int size, TimeSpan? timeout, CancellationToken cancellationToken);

    /// <summary>
    /// Adds an asynchronous waiter of a location of the block (<c>Atomics.waitAsync</c>), when the location contains
    /// <paramref name="expectedValue"/>: it is added when the method returns, so that a notification that follows notifies
    /// it. <paramref name="completed"/> is called once, on any thread: with <see langword="true"/> when the waiter was
    /// notified (or when the location no longer contained the expected value), and with <see langword="false"/> when the
    /// wait timed out or was canceled.
    /// </summary>
    /// <param name="byteIndex">The index of the location in the block: a multiple of <paramref name="size"/> within the block.</param>
    /// <param name="expectedValue">The expected value of the location (sign-extended for a 32-bit location).</param>
    /// <param name="size">The size of the location in bytes: 4 or 8.</param>
    /// <param name="timeout">The maximum duration of the wait, or <see langword="null"/> to wait until notified.</param>
    /// <param name="completed">The callback called when the wait completes.</param>
    /// <param name="cancellationToken">A token that stops the wait (the agent of the waiter stops).</param>
    /// <returns>
    /// <see langword="true"/> when the waiter is added; <see langword="false"/> when the runtime cannot add more waiters
    /// (<paramref name="completed"/> is then not called).
    /// </returns>
    bool WaitAsync(int byteIndex, long expectedValue, int size, TimeSpan? timeout, Action<bool> completed, CancellationToken cancellationToken);
}
