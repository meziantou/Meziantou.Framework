using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Meziantou.Framework.WebPlatform.Benchmarks;

/// <summary>
/// Compares two BenchmarkDotNet JSON reports (the base and the head of a pull request, run on the same machine): fails
/// when a benchmark of the library is slower or allocates more than the thresholds. The allocations are deterministic, so
/// their threshold is lower than the threshold of the time, which is noisy on shared machines.
/// </summary>
internal static class BenchmarkComparison
{
    public static int Run(string baseDirectory, string headDirectory, double timeThreshold, double allocationThreshold)
    {
        var baseResults = Load(baseDirectory);
        var headResults = Load(headDirectory);
        var report = new StringBuilder();
        report.Append("| Benchmark | Base | Head | Time | Base allocated | Head allocated | Allocated |\n");
        report.Append("|---|---:|---:|---:|---:|---:|---:|\n");
        var regressions = 0;
        foreach (var (name, head) in headResults.OrderBy(r => r.Key, StringComparer.Ordinal))
        {
            if (!baseResults.TryGetValue(name, out var @base))
                continue;

            var timeRatio = head.Median / @base.Median;
            var allocationRatio = @base.Allocated == 0 ? (head.Allocated == 0 ? 1 : double.PositiveInfinity) : head.Allocated / (double)@base.Allocated;
            var timeRegression = timeRatio > 1 + timeThreshold;
            var allocationRegression = allocationRatio > 1 + allocationThreshold;
            if (timeRegression || allocationRegression)
            {
                regressions++;
            }

            report.Append(CultureInfo.InvariantCulture, $"| {name} | {FormatTime(@base.Median)} | {FormatTime(head.Median)} | {FormatRatio(timeRatio)}{(timeRegression ? " ❌" : "")} | {@base.Allocated:N0} B | {head.Allocated:N0} B | {FormatRatio(allocationRatio)}{(allocationRegression ? " ❌" : "")} |\n");
        }

        report.Append(CultureInfo.InvariantCulture, $"\n{regressions} regression(s) (thresholds: time +{timeThreshold:P0}, allocations +{allocationThreshold:P0})\n");
        Console.Write(report);
        if (Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY") is { Length: > 0 } summary)
        {
            File.AppendAllText(summary, report.ToString());
        }

        return regressions == 0 ? 0 : 1;
    }

    private static string FormatTime(double nanoseconds) => nanoseconds switch
    {
        >= 1_000_000 => (nanoseconds / 1_000_000).ToString("N2", CultureInfo.InvariantCulture) + " ms",
        >= 1_000 => (nanoseconds / 1_000).ToString("N2", CultureInfo.InvariantCulture) + " µs",
        _ => nanoseconds.ToString("N0", CultureInfo.InvariantCulture) + " ns",
    };

    private static string FormatRatio(double ratio) => double.IsInfinity(ratio) ? "new" : (ratio - 1).ToString("+0.0%;-0.0%;0.0%", CultureInfo.InvariantCulture);

    /// <summary>Loads the results of the benchmarks of the library (the AngleSharp and HtmlAgilityPack benchmarks are ignored).</summary>
    private static Dictionary<string, (double Median, long Allocated)> Load(string directory)
    {
        var results = new Dictionary<string, (double, long)>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(directory, "*-report*.json", SearchOption.AllDirectories))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            foreach (var benchmark in document.RootElement.GetProperty("Benchmarks").EnumerateArray())
            {
                var method = benchmark.GetProperty("Method").GetString()!;
                if (method.StartsWith("AngleSharp", StringComparison.Ordinal) || method.StartsWith("HtmlAgilityPack", StringComparison.Ordinal))
                    continue;

                var name = benchmark.GetProperty("Type").GetString() + "." + method + benchmark.GetProperty("Parameters").GetString() switch
                {
                    { Length: > 0 } parameters => "(" + parameters + ")",
                    _ => "",
                };
                var median = benchmark.GetProperty("Statistics").GetProperty("Median").GetDouble();
                var allocated = benchmark.TryGetProperty("Memory", out var memory) ? memory.GetProperty("BytesAllocatedPerOperation").GetInt64() : 0;
                results[name] = (median, allocated);
            }
        }

        return results;
    }
}
