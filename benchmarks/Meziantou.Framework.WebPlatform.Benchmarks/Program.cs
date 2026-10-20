using System.Globalization;
using BenchmarkDotNet.Running;
using Meziantou.Framework.WebPlatform.Benchmarks;

// Usage:
//   dotnet run -c Release -- [BenchmarkDotNet arguments]                          runs the benchmarks
//   dotnet run -c Release -- memory                                                 bytes per node of the parsed documents
//   dotnet run -c Release -- compare <base> <head> [<time> <allocations>]           compares two BenchmarkDotNet JSON reports
//   dotnet run -c Release -- invalidation <class|row|offset|frame|transition-frame> <count> runs a scenario of InvalidationBenchmarks in a loop (profiling)
//   dotnet run -c Release -- style <wikipedia-styled|small-bootstrap> <count>       runs StyleBenchmarks in a loop (profiling)
return args switch
{
    ["memory"] => MemoryReport.Run(),
    ["compare", var @base, var head] => BenchmarkComparison.Run(@base, head, timeThreshold: 0.25, allocationThreshold: 0.05),
    ["compare", var @base, var head, var time, var allocations] => BenchmarkComparison.Run(@base, head, double.Parse(time, CultureInfo.InvariantCulture), double.Parse(allocations, CultureInfo.InvariantCulture)),
    ["invalidation", var scenario, var count] => InvalidationProfile.Run(scenario, int.Parse(count, CultureInfo.InvariantCulture)),
    ["style", var document, var count] => StyleProfile.Run(document, int.Parse(count, CultureInfo.InvariantCulture)),
    _ => RunBenchmarks(args),
};

static int RunBenchmarks(string[] args)
{
    BenchmarkSwitcher.FromAssembly(typeof(MemoryReport).Assembly).Run(args);
    return 0;
}
