namespace Meziantou.Framework.Threading.Tests;

public class AsyncReaderWriterLockTests
{
    [Fact]
    public async Task AsyncReaderWriterLock_ReaderWriter()
    {
        var value = 0;
        var count = 0;

        var l = new AsyncReaderWriterLock();

        var tasks = new Task[128];
        for (var i = 0; i < 128; i++)
        {
            if (i % 2 == 0)
            {
                tasks[i] = Task.Run(async () =>
                {
                    using (await l.WriterLockAsync())
                    {
                        count++;
                        Assert.Equal(1, count);
                        value++;
                        count--;
                        Assert.Equal(0, count);
                    }
                });
            }
            else
            {
                tasks[i] = Task.Run(async () =>
                {
                    using (await l.ReaderLockAsync())
                    {
                        Assert.Equal(0, count);
                        Assert.True(value <= 128);
                    }
                });
            }
        }

        await Task.WhenAll(tasks);
        Assert.Equal(64, value);
    }

    [Fact]
    public async Task WriterWaitsForActiveReader()
    {
        var l = new AsyncReaderWriterLock();
        var reader = await l.ReaderLockAsync();

        var writerTask = l.WriterLockAsync().AsTask();
        Assert.False(writerTask.IsCompleted); // blocked by the active reader

        reader.Dispose();
        using (await writerTask.WaitAsync(TimeSpan.FromSeconds(30)))
        {
        }
    }

    [Fact]
    public async Task NewReaderWaitsWhileWriterIsQueued()
    {
        var l = new AsyncReaderWriterLock();
        var reader1 = await l.ReaderLockAsync();

        var writerTask = l.WriterLockAsync().AsTask(); // queued behind the active reader
        var reader2Task = l.ReaderLockAsync().AsTask(); // must wait because a writer is queued (no writer starvation)
        Assert.False(reader2Task.IsCompleted);

        reader1.Dispose();

        using (await writerTask.WaitAsync(TimeSpan.FromSeconds(30)))
        {
            Assert.False(reader2Task.IsCompleted); // reader stays blocked while the writer holds the lock
        }

        using (await reader2Task.WaitAsync(TimeSpan.FromSeconds(30)))
        {
        }
    }

    [Fact]
    public async Task MultipleReadersAcquireConcurrently()
    {
        var l = new AsyncReaderWriterLock();
        var r1 = await l.ReaderLockAsync();
        var r2 = await l.ReaderLockAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30));
        var r3 = await l.ReaderLockAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30));

        r1.Dispose();
        r2.Dispose();
        r3.Dispose();
    }

    [Fact]
    public void DefaultReleaser_DisposeIsNoop()
    {
        default(AsyncReaderWriterLock.Releaser).Dispose();
    }

    [Fact]
    public async Task WriterLockAsync_AlreadyCanceledToken_ReturnsCanceledTask()
    {
        var rwLock = new AsyncReaderWriterLock();

        await Assert.ThrowsAsync<TaskCanceledException>(() => rwLock.WriterLockAsync(new CancellationToken(canceled: true)).AsTask());
    }

    [Fact]
    public async Task ReaderLockAsync_AlreadyCanceledToken_ReturnsCanceledTask()
    {
        var rwLock = new AsyncReaderWriterLock();

        await Assert.ThrowsAsync<TaskCanceledException>(() => rwLock.ReaderLockAsync(new CancellationToken(canceled: true)).AsTask());
    }

    [Fact]
    public async Task WriterLockAsync_CanceledWhileWaiting_DoesNotHoldTheLock()
    {
        var rwLock = new AsyncReaderWriterLock();
        using var cts = new CancellationTokenSource();

        var releaser = await rwLock.WriterLockAsync();
        var pending = rwLock.WriterLockAsync(cts.Token).AsTask();

        await cts.CancelAsync();
        await Assert.ThrowsAsync<TaskCanceledException>(() => pending);

        // The canceled waiter must not be granted ownership when the current writer releases.
        releaser.Dispose();

        var next = rwLock.WriterLockAsync().AsTask();
        (await next.WaitAsync(TimeSpan.FromSeconds(30))).Dispose();
    }

    [Fact]
    public async Task ReaderLockAsync_CanceledWhileWaiting_DoesNotHoldTheLock()
    {
        var rwLock = new AsyncReaderWriterLock();
        using var cts = new CancellationTokenSource();

        var releaser = await rwLock.WriterLockAsync();
        var pending = rwLock.ReaderLockAsync(cts.Token).AsTask();

        await cts.CancelAsync();
        await Assert.ThrowsAsync<TaskCanceledException>(() => pending);

        releaser.Dispose();

        var next = rwLock.WriterLockAsync().AsTask();
        (await next.WaitAsync(TimeSpan.FromSeconds(30))).Dispose();
    }

    [Fact]
    public async Task CancelingOneWaiter_LeavesTheOthersQueued()
    {
        var rwLock = new AsyncReaderWriterLock();
        using var cts = new CancellationTokenSource();

        var releaser = await rwLock.WriterLockAsync();
        var canceled = rwLock.WriterLockAsync(cts.Token).AsTask();
        var survivor = rwLock.WriterLockAsync().AsTask();

        await cts.CancelAsync();
        await Assert.ThrowsAsync<TaskCanceledException>(() => canceled);
        Assert.False(survivor.IsCompleted);

        releaser.Dispose();

        (await survivor.WaitAsync(TimeSpan.FromSeconds(30))).Dispose();
    }

    [Fact]
    public async Task CancelingTheQueuedWriters_ReleasesTheReadersBlockedBehindThem()
    {
        var rwLock = new AsyncReaderWriterLock();
        using var cts = new CancellationTokenSource();

        var writer = await rwLock.WriterLockAsync();
        var queuedWriter = rwLock.WriterLockAsync(cts.Token).AsTask();
        var queuedReader = rwLock.ReaderLockAsync().AsTask();

        await cts.CancelAsync();
        await Assert.ThrowsAsync<TaskCanceledException>(() => queuedWriter);

        writer.Dispose();

        (await queuedReader.WaitAsync(TimeSpan.FromSeconds(30))).Dispose();
    }

    [Fact]
    public async Task CancelingTheQueuedWriter_ReleasesTheReadersBlockedBehindIt_WhileAnotherReaderHoldsTheLock()
    {
        var rwLock = new AsyncReaderWriterLock();
        using var cts = new CancellationTokenSource();

        var reader1 = await rwLock.ReaderLockAsync();
        var queuedWriter = rwLock.WriterLockAsync(cts.Token).AsTask();
        var reader2 = rwLock.ReaderLockAsync().AsTask(); // queued behind the writer

        await cts.CancelAsync();
        await Assert.ThrowsAsync<TaskCanceledException>(() => queuedWriter);

        // No writer is left, so the queued reader can join the reader already holding the lock.
        var releaser2 = await reader2.WaitAsync(TimeSpan.FromSeconds(30));

        reader1.Dispose();
        releaser2.Dispose();

        // Both readers must have been accounted for, otherwise the lock never becomes free again.
        (await rwLock.WriterLockAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30))).Dispose();
    }

    [Fact]
    public async Task CancelingWritersAtEveryPosition_PreservesTheOrderOfTheOthers()
    {
        // A canceled waiter is unlinked from the queue. The survivors must keep their FIFO order whether the
        // canceled waiter was the head, the tail, or in the middle, and consecutive cancellations must not
        // corrupt the queue.
        var rwLock = new AsyncReaderWriterLock();
        var held = await rwLock.WriterLockAsync();

        var sources = new CancellationTokenSource[10];
        var waiters = new Task<AsyncReaderWriterLock.Releaser>[sources.Length];
        for (var i = 0; i < sources.Length; i++)
        {
            sources[i] = new CancellationTokenSource();
            waiters[i] = rwLock.WriterLockAsync(sources[i].Token).AsTask();
        }

        int[] canceled = [0, 3, 4, 7, 9];
        foreach (var index in canceled)
        {
            await sources[index].CancelAsync();
            await Assert.ThrowsAsync<TaskCanceledException>(() => waiters[index]);
        }

        var current = held;
        int[] survivors = [1, 2, 5, 6, 8];
        foreach (var index in survivors)
        {
            Assert.False(waiters[index].IsCompleted);
            current.Dispose();
            current = await waiters[index].WaitAsync(TimeSpan.FromSeconds(30));
        }

        // The queue is empty again, so a writer that arrives now must still be reachable.
        var late = rwLock.WriterLockAsync().AsTask();
        current.Dispose();
        (await late.WaitAsync(TimeSpan.FromSeconds(30))).Dispose();

        foreach (var source in sources)
        {
            source.Dispose();
        }
    }

    [Fact]
    public async Task CancelingSomeQueuedReaders_AdmitsOnlyTheRemainingOnes()
    {
        var rwLock = new AsyncReaderWriterLock();
        var held = await rwLock.WriterLockAsync();

        var sources = new CancellationTokenSource[6];
        var waiters = new Task<AsyncReaderWriterLock.Releaser>[sources.Length];
        for (var i = 0; i < sources.Length; i++)
        {
            sources[i] = new CancellationTokenSource();
            waiters[i] = rwLock.ReaderLockAsync(sources[i].Token).AsTask();
        }

        int[] canceled = [0, 2, 5];
        foreach (var index in canceled)
        {
            await sources[index].CancelAsync();
            await Assert.ThrowsAsync<TaskCanceledException>(() => waiters[index]);
        }

        held.Dispose();

        // The remaining readers are admitted together, and the reader count must match them exactly: a writer
        // can only run once every one of them has released.
        int[] survivors = [1, 3, 4];
        var releasers = new List<AsyncReaderWriterLock.Releaser>(survivors.Length);
        foreach (var index in survivors)
        {
            releasers.Add(await waiters[index].WaitAsync(TimeSpan.FromSeconds(30)));
        }

        var writer = rwLock.WriterLockAsync().AsTask();
        foreach (var releaser in releasers)
        {
            Assert.False(writer.IsCompleted);
            releaser.Dispose();
        }

        var writerReleaser = await writer.WaitAsync(TimeSpan.FromSeconds(30));

        // The reader queue is empty again, so a reader that arrives now must still be reachable.
        var late = rwLock.ReaderLockAsync().AsTask();
        writerReleaser.Dispose();
        (await late.WaitAsync(TimeSpan.FromSeconds(30))).Dispose();

        foreach (var source in sources)
        {
            source.Dispose();
        }
    }

    [Fact]
    public async Task ReaderContinuationsAreNotInlinedOnTheReleasingThread()
    {
        // The waiting readers used to share a TaskCompletionSource created without
        // RunContinuationsAsynchronously, so releasing a writer ran every reader's continuation synchronously on
        // the releasing thread.
        var rwLock = new AsyncReaderWriterLock();
        var writer = await rwLock.WriterLockAsync();

        var readerThreadId = 0;
        var readerRan = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reader = rwLock.ReaderLockAsync().AsTask();
        _ = reader.ContinueWith(
            t =>
            {
                readerThreadId = Environment.CurrentManagedThreadId;
                t.Result.Dispose();
                readerRan.SetResult();
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        // Release from a dedicated thread rather than a pool thread: the thread pool is free to run the
        // continuation on the releasing thread once it goes idle, which would make a plain "different thread"
        // assertion flaky. A non-pool thread can only run the continuation if it was inlined.
        var releasingThreadId = 0;
        var releasingThread = new Thread(() =>
        {
            releasingThreadId = Environment.CurrentManagedThreadId;
            writer.Dispose();
        });

        releasingThread.Start();
        Assert.True(releasingThread.Join(TimeSpan.FromSeconds(30)));

        await readerRan.Task.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.NotEqual(releasingThreadId, readerThreadId);
    }

    [Fact]
    public async Task ConcurrentReadersAndWritersKeepTheStateConsistent()
    {
        var rwLock = new AsyncReaderWriterLock();
        var readers = 0;
        var writers = 0;
        var failures = 0;

        async Task ReadAsync()
        {
            for (var i = 0; i < 100; i++)
            {
                using (await rwLock.ReaderLockAsync())
                {
                    Interlocked.Increment(ref readers);
                    if (Volatile.Read(ref writers) != 0)
                    {
                        Interlocked.Increment(ref failures);
                    }

                    Interlocked.Decrement(ref readers);
                }
            }
        }

        async Task WriteAsync()
        {
            for (var i = 0; i < 100; i++)
            {
                using (await rwLock.WriterLockAsync())
                {
                    Interlocked.Increment(ref writers);
                    if (Volatile.Read(ref readers) != 0 || Volatile.Read(ref writers) != 1)
                    {
                        Interlocked.Increment(ref failures);
                    }

                    Interlocked.Decrement(ref writers);
                }
            }
        }

        var tasks = new List<Task>();
        for (var i = 0; i < 4; i++)
        {
            tasks.Add(Task.Run(ReadAsync));
            tasks.Add(Task.Run(WriteAsync));
        }

        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(60));

        Assert.Equal(0, failures);
    }

    [Fact]
    public async Task ReaderReleaser_DisposedTwice_DoesNotReleaseAnotherReader()
    {
        var rwLock = new AsyncReaderWriterLock();
        var stale = await rwLock.ReaderLockAsync();
        stale.Dispose();

        var reader = await rwLock.ReaderLockAsync();
        stale.Dispose();

        // The remaining reader still holds the lock, so a writer must not be able to acquire it.
        var writer = rwLock.WriterLockAsync().AsTask();
        Assert.False(writer.IsCompleted, "The second disposal released a reader lock it no longer owns");

        reader.Dispose();
        (await writer.WaitAsync(TimeSpan.FromSeconds(30))).Dispose();
    }

    [Fact]
    public async Task WriterReleaser_DisposedTwice_DoesNotReleaseAnotherWriter()
    {
        var rwLock = new AsyncReaderWriterLock();
        var stale = await rwLock.WriterLockAsync();
        stale.Dispose();

        var writer = await rwLock.WriterLockAsync();
        stale.Dispose();

        var other = rwLock.WriterLockAsync().AsTask();
        Assert.False(other.IsCompleted, "The second disposal released a writer lock it no longer owns");

        writer.Dispose();
        (await other.WaitAsync(TimeSpan.FromSeconds(30))).Dispose();
    }

    [Fact]
    public async Task Releaser_DisposedTwice_KeepsTheLockUsable()
    {
        var rwLock = new AsyncReaderWriterLock();
        var reader = await rwLock.ReaderLockAsync();
        reader.Dispose();
        reader.Dispose();

        // A duplicate release must not drive the reader count below zero, which would let a reader and a writer
        // hold the lock at the same time.
        using var writer = await rwLock.WriterLockAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30));
        var blocked = rwLock.ReaderLockAsync().AsTask();
        Assert.False(blocked.IsCompleted);

        writer.Dispose();
        (await blocked.WaitAsync(TimeSpan.FromSeconds(30))).Dispose();
    }
}
