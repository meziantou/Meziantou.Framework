﻿using System.IO;
using System.Linq;
using FluentAssertions;
using TestUtilities;

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

        [RunIfFact(FactOperatingSystem.Windows)]
        public void GetProcessesLockingFile()
        {
            var path = Path.GetTempFileName();
            try
            {
                using (File.Open(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                {
                    var processes = RestartManager.GetProcessesLockingFile(path);
                    processes.Single().Id.Should().Be(_currentProcessId);
                }
            }
            finally
            {
                File.Delete(path);
            }
        }

        [RunIfFact(FactOperatingSystem.Windows)]
        public void IsFileLocked_True()
        {
            var path = Path.GetTempFileName();
            try
            {
                using (File.Open(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                {
                    RestartManager.IsFileLocked(path).Should().BeTrue();
                }
            }
            finally
            {
                File.Delete(path);
            }
        }

        [RunIfFact(FactOperatingSystem.Windows)]
        public void IsFileLocked_False()
        {
            var path = Path.GetTempFileName();
            try
            {
                RestartManager.IsFileLocked(path).Should().BeFalse();
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
