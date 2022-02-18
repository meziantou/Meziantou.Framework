using BenchmarkDotNet.Running;

namespace DependencyScanningBenchmarks;

internal static class Program
{
    private static void Main()
    {
        BenchmarkRunner.Run<DependencyScannerBenchmark>();
    }
}
