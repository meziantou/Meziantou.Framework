using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.Threading.Tests
{
    [TestClass]
    public class AsyncLockTests
    {
        [TestMethod]
        public async Task Lock()
        {
            using (var asyncLock = new AsyncLock())
            {
                for (var i = 0; i < 2; i++)
                {
                    using (await asyncLock.LockAsync())
                    {
                        if (await asyncLock.TryLockAsync(TimeSpan.Zero))
                        {
                            Assert.Fail("Should not be able to acquire the lock");
                        }

                        if (asyncLock.TryLock())
                        {
                            Assert.Fail("Should not be able to acquire the lock");
                        }
                    }
                }
            }
        }
    }
}
