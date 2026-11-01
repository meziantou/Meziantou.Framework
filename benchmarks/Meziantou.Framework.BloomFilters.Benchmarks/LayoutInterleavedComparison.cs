using System.Diagnostics;
using System.Globalization;

namespace Meziantou.Framework.BloomFilters.Benchmarks;

/// <summary>
/// Runs the <see cref="BloomFilterLayoutBenchmark"/> methods for every layout in rotation and reports the minimum and
/// median time per operation. On a shared, loaded machine, sequential BenchmarkDotNet cases see different load; a
/// rotation exposes every layout to the same conditions. Run with <c>dotnet run -c Release -f net10.0 -- interleaved</c>.
/// </summary>
internal static class LayoutInterleavedComparison
{
    private const int OperationsPerInvoke = 1 << 18;

    public static void Run(int rounds)
    {
        var operations = new (string Name, int OperationCount, Action<BloomFilterLayoutBenchmark> Action)[]
        {
            ("Add", OperationsPerInvoke, benchmark => benchmark.Add()),
            ("MayContain_Present", OperationsPerInvoke, benchmark => benchmark.MayContain_Present()),
            ("MayContain_Absent", OperationsPerInvoke, benchmark => benchmark.MayContain_Absent()),
            ("MayContain_Mixed50", OperationsPerInvoke, benchmark => benchmark.MayContain_Mixed50()),
            ("Add_Concurrent", OperationsPerInvoke, benchmark => benchmark.Add_Concurrent()),
            ("Construct", 1, benchmark => benchmark.Construct()),
        };

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Processor count: {Environment.ProcessorCount}, rounds: {rounds}, ns/op as min (median)"));
        foreach (var memory in Enum.GetValues<FilterMemory>())
        {
            var layouts = Enum.GetValues<FilterLayout>();
            var benchmarks = layouts.Select(layout =>
            {
                var benchmark = new BloomFilterLayoutBenchmark { Memory = memory, Layout = layout };
                benchmark.Setup();
                return benchmark;
            }).ToArray();

            Console.WriteLine();
            Console.WriteLine($"| {memory} | " + string.Join(" | ", layouts) + " |");
            Console.WriteLine("|---|" + string.Concat(Enumerable.Repeat("---:|", layouts.Length)));
            foreach (var (name, operationCount, action) in operations)
            {
                var timings = new double[layouts.Length][];
                for (var i = 0; i < layouts.Length; i++)
                {
                    timings[i] = new double[rounds];
                    action(benchmarks[i]); // warm up
                }

                for (var round = 0; round < rounds; round++)
                {
                    for (var offset = 0; offset < layouts.Length; offset++)
                    {
                        var i = (round + offset) % layouts.Length;
                        var start = Stopwatch.GetTimestamp();
                        action(benchmarks[i]);
                        timings[i][round] = Stopwatch.GetElapsedTime(start).TotalNanoseconds / operationCount;
                    }
                }

                var cells = timings.Select(values =>
                {
                    Array.Sort(values);
                    return operationCount is 1
                        ? string.Create(CultureInfo.InvariantCulture, $"{values[0] / 1000:F0} µs ({values[values.Length / 2] / 1000:F0} µs)")
                        : string.Create(CultureInfo.InvariantCulture, $"{values[0]:F2} ({values[values.Length / 2]:F2})");
                });
                Console.WriteLine($"| {name} | " + string.Join(" | ", cells) + " |");
            }

            foreach (var benchmark in benchmarks)
            {
                benchmark.Cleanup();
            }
        }
    }
}
