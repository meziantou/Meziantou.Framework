using System.Globalization;
using BenchmarkDotNet.Running;
using Meziantou.Framework.BloomFilters.Benchmarks;

if (args is ["accuracy"])
{
    LayoutAccuracyReport.Run();
    return;
}

if (args is ["interleaved", ..])
{
    LayoutInterleavedComparison.Run(args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 30);
    return;
}

BenchmarkSwitcher.FromAssembly(typeof(BloomFilterBenchmark).Assembly).Run(args);
