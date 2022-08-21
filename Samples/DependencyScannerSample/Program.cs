using Meziantou.Framework.DependencyScanning;

namespace DependencyScannerSample;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        foreach (var item in await DependencyScanner.ScanDirectoryAsync(args[0], options: null, CancellationToken.None).ConfigureAwait(false))
        {
            Console.WriteLine(item.ToString());
        }
    }
}
