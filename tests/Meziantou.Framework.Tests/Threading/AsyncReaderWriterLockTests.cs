using System.Threading.Tasks;
using Xunit;

namespace Meziantou.Framework.Threading.Tests
{
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
                        using (await l.WriterLockAsync().ConfigureAwait(false))
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
                        using (await l.ReaderLockAsync().ConfigureAwait(false))
                        {
                            Assert.Equal(0, count);
                            Assert.True(value <= 128);
                        }
                    });
                }
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);
            Assert.Equal(64, value);
        }
    }
}
