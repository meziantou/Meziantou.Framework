using BenchmarkDotNet.Running;

namespace GlobbingBenchmarks;

internal static class Program
{
    private static void Main()
    {
        BenchmarkRunner.Run<GlobIsMatchBenchmark>();
        BenchmarkRunner.Run<EnumerateFilesBenchmark>();
    }
}
