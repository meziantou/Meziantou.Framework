// Compares two sets of BenchmarkDotNet "full" JSON reports (JsonExporter.Full, *-report-full.json) measured on the same
// machine, and fails when a benchmark regresses beyond the budget with statistical significance:
// - time: the median per-operation time of the current run exceeds the baseline by more than --threshold percent AND a
//   one-sided Mann-Whitney U test on the individual iteration measurements gives p < --alpha;
// - allocations: the managed bytes allocated per operation exceed the baseline by more than --allocation-threshold percent
//   and by more than 1 KB (allocations are nearly deterministic, so no test is needed).
// Benchmarks present in only one set are listed but never fail the comparison.
// Usage: dotnet run eng/compare-benchmarks.cs -- <baseline-results-dir> <current-results-dir> [--threshold 10] [--alpha 0.01]
//        [--allocation-threshold 5] [--output report.md]
using System.Globalization;
using System.Text;
using System.Text.Json;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: dotnet run eng/compare-benchmarks.cs -- <baseline-dir> <current-dir> [--threshold 10] [--alpha 0.01] [--allocation-threshold 5] [--output report.md]");
    return 2;
}

var threshold = GetOption("--threshold", 10) / 100;
var alpha = GetOption("--alpha", 0.01);
var allocationThreshold = GetOption("--allocation-threshold", 5) / 100;
var outputIndex = Array.IndexOf(args, "--output");
var outputPath = outputIndex >= 0 && outputIndex + 1 < args.Length ? args[outputIndex + 1] : null;

var baseline = Load(args[0]);
var current = Load(args[1]);
var report = new StringBuilder();
report.AppendLine(CultureInfo.InvariantCulture, $"Budget: time +{threshold:P0} (one-sided Mann-Whitney U, p < {alpha}), allocations +{allocationThreshold:P0} and +1 KB.");
report.AppendLine();
report.AppendLine("| Benchmark | Baseline median | Current median | Ratio | p | Baseline alloc/op | Current alloc/op | Verdict |");
report.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |");
var regressions = 0;
foreach (var name in baseline.Keys.Union(current.Keys).Order(StringComparer.Ordinal))
{
    baseline.TryGetValue(name, out var before);
    current.TryGetValue(name, out var after);
    if (before is null || after is null)
    {
        report.AppendLine(CultureInfo.InvariantCulture, $"| {name} | {(before is null ? "-" : FormatTime(Median(before.Times)))} | {(after is null ? "-" : FormatTime(Median(after.Times)))} | | | | | only in {(before is null ? "current" : "baseline")} |");
        continue;
    }

    var ratio = Median(after.Times) / Median(before.Times);
    var p = MannWhitneyGreater(after.Times, before.Times);
    var verdicts = new List<string>();
    if (ratio > 1 + threshold && p < alpha)
    {
        verdicts.Add("**time regression**");
    }
    else if (ratio < 1 - threshold && MannWhitneyGreater(before.Times, after.Times) < alpha)
    {
        verdicts.Add("faster");
    }

    if (after.Allocated is { } allocatedAfter && before.Allocated is { } allocatedBefore && allocatedAfter > allocatedBefore * (1 + allocationThreshold) && allocatedAfter - allocatedBefore > 1024)
    {
        verdicts.Add("**allocation regression**");
    }

    if (verdicts.Any(verdict => verdict.Contains("regression", StringComparison.Ordinal)))
    {
        regressions++;
    }

    report.AppendLine(CultureInfo.InvariantCulture, $"| {name} | {FormatTime(Median(before.Times))} | {FormatTime(Median(after.Times))} | {ratio:0.000} | {p:0.0000} | {FormatBytes(before.Allocated)} | {FormatBytes(after.Allocated)} | {(verdicts.Count == 0 ? "same" : string.Join(", ", verdicts))} |");
}

report.AppendLine();
report.AppendLine(CultureInfo.InvariantCulture, $"{regressions} regression(s).");
Console.WriteLine(report);
if (outputPath is not null)
{
    File.WriteAllText(outputPath, report.ToString());
}

return regressions == 0 ? 0 : 1;

double GetOption(string name, double defaultValue)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? double.Parse(args[index + 1], CultureInfo.InvariantCulture) : defaultValue;
}

static Dictionary<string, Result> Load(string directory)
{
    var results = new Dictionary<string, Result>(StringComparer.Ordinal);
    foreach (var file in Directory.EnumerateFiles(directory, "*-report-full.json", SearchOption.AllDirectories))
    {
        using var document = JsonDocument.Parse(File.ReadAllText(file));
        foreach (var benchmark in document.RootElement.GetProperty("Benchmarks").EnumerateArray())
        {
            var name = benchmark.GetProperty("FullName").GetString()!;
            var parameters = benchmark.TryGetProperty("Parameters", out var value) ? value.GetString() : null;
            if (!string.IsNullOrEmpty(parameters) && !name.Contains('(', StringComparison.Ordinal))
            {
                name += "(" + parameters + ")";
            }

            // Per-operation times of the result iterations (workload minus overhead); the actual ones when absent
            var measurements = benchmark.GetProperty("Measurements").EnumerateArray().Where(m => m.GetProperty("IterationMode").GetString() == "Workload").ToList();
            var stage = measurements.Any(m => m.GetProperty("IterationStage").GetString() == "Result") ? "Result" : "Actual";
            var times = measurements
                .Where(m => m.GetProperty("IterationStage").GetString() == stage)
                .Select(m => m.GetProperty("Nanoseconds").GetDouble() / m.GetProperty("Operations").GetInt64())
                .ToArray();
            if (times.Length == 0)
                continue;

            long? allocated = benchmark.TryGetProperty("Memory", out var memory) && memory.ValueKind == JsonValueKind.Object ? memory.GetProperty("BytesAllocatedPerOperation").GetInt64() : null;
            results[name] = new Result(times, allocated);
        }
    }

    return results;
}

static double Median(double[] values)
{
    var sorted = values.Order().ToArray();
    return sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[(sorted.Length / 2) - 1] + sorted[sorted.Length / 2]) / 2;
}

// One-sided Mann-Whitney U test (H1: values of x tend to be greater than those of y), normal approximation with tie and
// continuity corrections. Adequate for the 8 to 20 iterations of each benchmark.
static double MannWhitneyGreater(double[] x, double[] y)
{
    var all = x.Select(value => (Value: value, FromX: true)).Concat(y.Select(value => (Value: value, FromX: false))).OrderBy(item => item.Value).ToArray();
    var ranks = new double[all.Length];
    var tieTerm = 0.0;
    for (var i = 0; i < all.Length;)
    {
        var j = i;
        while (j + 1 < all.Length && all[j + 1].Value == all[i].Value)
        {
            j++;
        }

        var rank = ((i + j) / 2.0) + 1;
        for (var k = i; k <= j; k++)
        {
            ranks[k] = rank;
        }

        var ties = j - i + 1;
        tieTerm += (Math.Pow(ties, 3) - ties);
        i = j + 1;
    }

    double n1 = x.Length, n2 = y.Length, n = n1 + n2;
    var rankSum = all.Select((item, index) => item.FromX ? ranks[index] : 0).Sum();
    var u = rankSum - (n1 * (n1 + 1) / 2);
    var mean = n1 * n2 / 2;
    var variance = n1 * n2 / 12 * ((n + 1) - (tieTerm / (n * (n - 1))));
    if (variance <= 0)
        return u > mean ? 0 : 1;

    var z = (u - mean - 0.5) / Math.Sqrt(variance);
    return 1 - NormalCdf(z);
}

static double NormalCdf(double z) => 0.5 * Erfc(-z / Math.Sqrt(2));

// Complementary error function from Abramowitz and Stegun, Handbook of Mathematical Functions, formula 7.1.26 (absolute
// error below 1.5e-7), extended to negative arguments with erfc(-x) = 2 - erfc(x)
static double Erfc(double x)
{
    var z = Math.Abs(x);
    var t = 1 / (1 + (0.3275911 * z));
    var polynomial = t * (0.254829592 + (t * (-0.284496736 + (t * (1.421413741 + (t * (-1.453152027 + (t * 1.061405429))))))));
    var value = polynomial * Math.Exp(-z * z);
    return x >= 0 ? value : 2 - value;
}

static string FormatTime(double nanoseconds) => nanoseconds switch
{
    < 1_000 => nanoseconds.ToString("0.0", CultureInfo.InvariantCulture) + " ns",
    < 1_000_000 => (nanoseconds / 1_000).ToString("0.00", CultureInfo.InvariantCulture) + " us",
    _ => (nanoseconds / 1_000_000).ToString("0.00", CultureInfo.InvariantCulture) + " ms",
};

static string FormatBytes(long? bytes) => bytes switch
{
    null => "-",
    < 1024 => bytes.Value.ToString(CultureInfo.InvariantCulture) + " B",
    _ => (bytes.Value / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " KB",
};

internal sealed record Result(double[] Times, long? Allocated);
