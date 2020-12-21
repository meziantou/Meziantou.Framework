using System;
using System.Collections.Generic;
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
        private static readonly HashSet<string> s_createdDirectories = new(StringComparer.OrdinalIgnoreCase);

        public FullPath FullPath { get; }

        private TemporaryDirectory(FullPath path)
        {
            FullPath = path;
        }

        public static TemporaryDirectory Create()
        {
            return new TemporaryDirectory(CreateUniqueDirectory(FullPath.Combine(Path.GetTempPath(), "TD", DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture))));
        }

        public FullPath GetFullPath(string relativePath)
        {
            return FullPath.Combine(FullPath, relativePath);
        }

        public void CreateEmptyFile(string relativePath)
        {
            var path = GetFullPath(relativePath);
            Directory.CreateDirectory(path.Parent);
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        }

        private static FullPath CreateUniqueDirectory(FullPath folderPath)
        {
            using (var mutex = new Mutex(initiallyOwned: false, name: "Meziantou.Framework.TemporaryDirectory"))
            {
                mutex.WaitOne();
                try
                {
                    var count = 1;

                    var tempPath = folderPath.Value + "_";
                    while (s_createdDirectories.Contains(folderPath) || Directory.Exists(folderPath))
                    {
                        folderPath = FullPath.FromPath(tempPath + count.ToString(CultureInfo.InvariantCulture));
                        count++;
                    }

                    s_createdDirectories.Add(folderPath);
                    Directory.CreateDirectory(folderPath);
                }
                finally
                {
                    mutex.ReleaseMutex();
                }
            }

            return folderPath;
        }

        public void Dispose()
        {
            IOUtilities.Delete(new DirectoryInfo(FullPath));
        }

        public ValueTask DisposeAsync()
        {
            return IOUtilities.DeleteAsync(new DirectoryInfo(FullPath), CancellationToken.None);
        }
    }
}
