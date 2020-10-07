﻿using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Meziantou.Framework.DependencyScanning
{
    public abstract class Location
    {
        protected Location(string filePath)
        {
            FilePath = filePath;
        }

        public string FilePath { get; }

        public abstract bool IsUpdatable { get; }

        internal async Task UpdateAsync(string newVersion, CancellationToken cancellationToken)
        {
            using var stream = File.Open(FilePath, FileMode.Open, FileAccess.ReadWrite);
            await UpdateAsync(stream, newVersion, cancellationToken).ConfigureAwait(false);
        }

        internal protected abstract Task UpdateAsync(Stream stream, string newVersion, CancellationToken cancellationToken);
    }
}
