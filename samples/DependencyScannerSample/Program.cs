using Meziantou.Framework.DependencyScanning;

foreach (var item in await DependencyScanner.ScanDirectoryAsync(args[0], options: null, CancellationToken.None).ConfigureAwait(false))
{
    Console.WriteLine(item.ToString());
}
