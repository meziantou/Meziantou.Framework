using System;
using System.Threading;
using System.Threading.Tasks;
using Meziantou.Framework.DependencyScanning;

namespace DependencyScannerSample
{
    internal static class Program
    {
        private static async Task Main(string[] args)
        {
            await foreach (var item in DependencyScanner.ScanDirectoryAsync(args[0], options: null, CancellationToken.None).ConfigureAwait(false))
            {
                Console.WriteLine(item.ToString());
            }
        }
    }
}
