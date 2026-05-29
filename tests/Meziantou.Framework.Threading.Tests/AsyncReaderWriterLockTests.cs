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

        var writerTask = l.WriterLockAsync();
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

        var writerTask = l.WriterLockAsync(); // queued behind the active reader
        var reader2Task = l.ReaderLockAsync(); // must wait because a writer is queued (no writer starvation)
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
        var r2 = await l.ReaderLockAsync().WaitAsync(TimeSpan.FromSeconds(30));
        var r3 = await l.ReaderLockAsync().WaitAsync(TimeSpan.FromSeconds(30));

        r1.Dispose();
        r2.Dispose();
        r3.Dispose();
    }

    [Fact]
    public void DefaultReleaser_DisposeIsNoop()
    {
        default(AsyncReaderWriterLock.Releaser).Dispose();
    }
}
