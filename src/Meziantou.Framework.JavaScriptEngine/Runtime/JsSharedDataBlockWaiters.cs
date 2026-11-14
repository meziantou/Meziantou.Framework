namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// The waiters of a <see cref="JsSharedDataBlock"/> whose memory is shared with another runtime that has its own waiters,
/// such as a shared WebAssembly memory (the <c>memory.atomic.wait32</c> and <c>memory.atomic.notify</c> instructions use the
/// waiters of the WebAssembly runtime). <c>Atomics.wait</c>, <c>Atomics.waitAsync</c> and <c>Atomics.notify</c> use them
/// instead of the waiter lists of the engines, so that the waiters of both runtimes are notified by both of them.
/// </summary>
/// <remarks>
/// The engine checks the value of the location before it calls <see cref="Wait"/> or <see cref="WaitAsync"/>: when another
/// agent changes it before the waiter is added, the waiter completes as if it was notified.
/// </remarks>
/// <seealso cref="JsSharedDataBlock.Waiters"/>
public abstract class JsSharedDataBlockWaiters
{
    /// <summary>Notifies the waiters of a location of the block (<c>Atomics.notify</c>).</summary>
    /// <param name="byteIndex">The index of the location in the block: a multiple of 4 within the block.</param>
    /// <param name="count">The maximum number of waiters to notify; <see cref="int.MaxValue"/> notifies all of them.</param>
    /// <returns>The number of waiters notified.</returns>
    public abstract int Notify(int byteIndex, int count);

    /// <summary>
    /// Blocks the calling thread until the waiter of a location of the block is notified, when the location contains
    /// <paramref name="expectedValue"/> (<c>Atomics.wait</c>).
    /// </summary>
    /// <param name="byteIndex">The index of the location in the block: a multiple of <paramref name="size"/> within the block.</param>
    /// <param name="expectedValue">The expected value of the location (sign-extended for a 32-bit location).</param>
    /// <param name="size">The size of the location in bytes: 4 or 8.</param>
    /// <param name="timeout">The maximum duration of the wait, or <see langword="null"/> to wait until notified.</param>
    /// <param name="cancellationToken">A token that stops the wait (the limits of the running script).</param>
    /// <returns><see langword="true"/> when the waiter was notified; <see langword="false"/> when the wait timed out or was canceled.</returns>
    public abstract bool Wait(int byteIndex, long expectedValue, int size, TimeSpan? timeout, CancellationToken cancellationToken);

    /// <summary>
    /// Adds an asynchronous waiter of a location of the block (<c>Atomics.waitAsync</c>), when the location contains
    /// <paramref name="expectedValue"/>: it is added when the method returns, so that a notification that follows notifies
    /// it. <paramref name="completed"/> is called once, on any thread: with <see langword="true"/> when the waiter was
    /// notified, and with <see langword="false"/> when the wait timed out.
    /// </summary>
    /// <param name="byteIndex">The index of the location in the block: a multiple of <paramref name="size"/> within the block.</param>
    /// <param name="expectedValue">The expected value of the location (sign-extended for a 32-bit location).</param>
    /// <param name="size">The size of the location in bytes: 4 or 8.</param>
    /// <param name="timeout">The maximum duration of the wait, or <see langword="null"/> to wait until notified.</param>
    /// <param name="completed">The callback called when the wait completes.</param>
    public abstract void WaitAsync(int byteIndex, long expectedValue, int size, TimeSpan? timeout, Action<bool> completed);
}
