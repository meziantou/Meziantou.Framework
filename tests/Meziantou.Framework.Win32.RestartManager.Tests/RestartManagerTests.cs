using System.IO;
using System.Linq;
using Xunit;

namespace Meziantou.Framework.Win32.Tests
{
    public class RestartManagerTests
    {
        private readonly int _currentProcessId;

        public RestartManagerTests()
        {
#if NET461 || NETCOREAPP3_1
            _currentProcessId = System.Diagnostics.Process.GetCurrentProcess().Id;
#else
            _currentProcessId = System.Environment.ProcessId;
#endif
        }

        [Fact]
        public void GetProcessesLockingFile()
        {
            var path = Path.GetTempFileName();
            try
            {
                using (File.Open(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                {
                    var processes = RestartManager.GetProcessesLockingFile(path);
                    Assert.Equal(_currentProcessId, processes.Single().Id);
                }
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void IsFileLocked_True()
        {
            var path = Path.GetTempFileName();
            try
            {
                using (File.Open(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                {
                    var actual = RestartManager.IsFileLocked(path);
                    Assert.True(actual);
                }
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void IsFileLocked_False()
        {
            var path = Path.GetTempFileName();
            try
            {
                var actual = RestartManager.IsFileLocked(path);
                Assert.False(actual);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
