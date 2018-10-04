using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.Threading.Tests
{
    [TestClass]
    public class AsyncReaderWriterLockTests
    {
        [TestMethod]
        public async Task AsyncReaderWriterLock_ReaderWriter()
        {
            int value = 0;
            int count = 0;

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
                            Assert.AreEqual(1, count);
                            value++;
                            count--;
                            Assert.AreEqual(0, count);
                        }
                    });
                }
                else
                {
                    tasks[i] = Task.Run(async () =>
                    {
                        using (await l.ReaderLockAsync())
                        {
                            Assert.AreEqual(0, count);
                            Assert.IsTrue(value <= 128);
                        }
                    });
                }
            }

            await Task.WhenAll(tasks);
            Assert.AreEqual(64, value);
        }
    }
}
