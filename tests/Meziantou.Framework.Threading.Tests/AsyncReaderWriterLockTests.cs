using FluentAssertions;
using Xunit;

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
                        count.Should().Be(1);
                        value++;
                        count--;
                        count.Should().Be(0);
                    }
                });
            }
            else
            {
                tasks[i] = Task.Run(async () =>
                {
                    using (await l.ReaderLockAsync())
                    {
                        count.Should().Be(0);
                        value.Should().BeLessOrEqualTo(128);
                    }
                });
            }
        }

        await Task.WhenAll(tasks);
        value.Should().Be(64);
    }
}
