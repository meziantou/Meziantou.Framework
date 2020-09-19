using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Meziantou.Framework
{
    [DebuggerDisplay("{FullPath}")]
    public sealed class TemporaryDirectory : IDisposable, IAsyncDisposable
    {
        public FullPath FullPath { get; }

        private TemporaryDirectory(FullPath path)
        {
            FullPath = path;
        }

        public static TemporaryDirectory Create()
        {
            return new TemporaryDirectory(CreateUniqueDirectory(FullPath.FromPath(Path.GetTempPath(), "TD", DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture))));
        }

        public FullPath GetFullPath(string relativePath)
        {
            return FullPath.FromPath(FullPath, relativePath);
        }

        private static FullPath CreateUniqueDirectory(FullPath filePath)
        {
            using (var mutex = new Mutex(initiallyOwned: false, name: "Meziantou.Framework.TemporaryDirectory"))
            {
                mutex.WaitOne();
                try
                {
                    var count = 1;

                    var tempPath = filePath.Value + "_";
                    while (Directory.Exists(filePath))
                    {
                        filePath = FullPath.FromPath(tempPath + count.ToString(CultureInfo.InvariantCulture));
                        count++;
                    }

                    Directory.CreateDirectory(filePath);
                }
                finally
                {
                    mutex.ReleaseMutex();
                }
            }

            return filePath;
        }

        public void Dispose()
        {
            IOUtilities.Delete(new DirectoryInfo(FullPath));
        }

        public ValueTask DisposeAsync()
        {
            return IOUtilities.DeleteAsync(new DirectoryInfo(FullPath));
        }
    }
}
