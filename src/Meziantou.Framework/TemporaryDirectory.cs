#if NETCOREAPP3_1
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
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

        public void Dispose()
        {
            IOUtilities.Delete(new DirectoryInfo(FullPath));
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
                        filePath = FullPath.FromPath(tempPath + count.ToStringInvariant());
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

        [SuppressMessage("Code Quality", "IDE0051:Remove unused private members", Justification = "Can be used from the debugger")]
        private void OpenInExplorer()
        {
            Process.Start(FullPath);
        }

        public ValueTask DisposeAsync()
        {
            return IOUtilities.DeleteAsync(new DirectoryInfo(FullPath));
        }
    }
}
#elif NET461 || NETSTANDARD2_0
#else
#error Platform not supported
#endif
