using System.Globalization;

namespace Meziantou.Framework.BloomFilters.Benchmarks;

/// <summary>
/// Measures what BenchmarkDotNet cannot: the false positive rate, the false negatives (must be zero) and the memory
/// each layout needs for a target false positive rate. Run with <c>dotnet run -c Release -f net10.0 -- accuracy</c>.
/// </summary>
internal static class LayoutAccuracyReport
{
    private const long AbsentQueryCount = 20_000_000;

    public static void Run()
    {
        // Bits per item each layout needs for a target false positive rate, from the models (no filter is built).
        const long ModelItemCount = 1_000_000;
        Console.WriteLine("| Target FPR | Classic bits/item (k) | Blocked bits/item (k) | Extra memory |");
        Console.WriteLine("|---:|---:|---:|---:|");
        foreach (var target in new[] { 0.1, 0.01, 0.001, 0.0001, 0.00001 })
        {
            var classic = BloomFilterSize.CreateOptimalSize(ModelItemCount, target);
            var blocked = BlockedBloomFilterSize.CreateOptimalSize(ModelItemCount, target);
            var classicBits = (double)classic.BitCount / ModelItemCount;
            var blockedBits = (double)blocked.BitCount / ModelItemCount;
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"| {target:P3} | {classicBits:F2} ({classic.HashCount}) | {blockedBits:F2} ({blocked.HashCount}) | {(blockedBits / classicBits) - 1:+0.0%} |"));
        }

        Console.WriteLine();

        Console.WriteLine("| Memory | Layout | Bytes | Bits/item | k | Expected FPR | Measured FPR | False negatives |");
        Console.WriteLine("|---|---|---:|---:|---:|---:|---:|---:|");
        foreach (var memory in Enum.GetValues<FilterMemory>())
        {
            foreach (var layout in Enum.GetValues<FilterLayout>())
            {
                var scenario = LayoutScenario.CreateFilled(memory, layout);
                var falseNegatives = Count(0, scenario.ItemCount, scenario, expected: false);
                var falsePositives = Count(scenario.ItemCount, AbsentQueryCount, scenario, expected: true);
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"| {memory} | {layout} | {scenario.BitCount / 8:N0} | {(double)scenario.BitCount / scenario.ItemCount:F2} | {scenario.HashCount} | {scenario.ExpectedFalsePositiveRate:P3} | {(double)falsePositives / AbsentQueryCount:P3} | {falseNegatives} |"));
            }
        }
    }

    // Counts the keys of [start, start + count) whose MayContain answer is the given value.
    private static long Count(long start, long count, LayoutScenario scenario, bool expected)
    {
        const int ChunkSize = 1 << 16;
        long result = 0;
        Parallel.For(0, (count + ChunkSize - 1) / ChunkSize, () => 0L, (chunk, _, local) =>
        {
            var end = Math.Min(count, (chunk + 1) * ChunkSize);
            for (var index = chunk * ChunkSize; index < end; index++)
            {
                if (scenario.MayContain(LayoutScenario.Key(start + index)) == expected)
                {
                    local++;
                }
            }

            return local;
        }, local => Interlocked.Add(ref result, local));
        return result;
    }
}
