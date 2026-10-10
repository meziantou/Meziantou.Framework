using System.Diagnostics;
using Meziantou.Framework.Imaging.TestHarness.Golden;
using Meziantou.Framework.Imaging.TestHarness.Pixels;

namespace Meziantou.Framework.Imaging.FuzzTests;

/// <summary>
/// Deterministic, bounded mutation fuzzing over the golden corpus: every valid, invalid,
/// unsupported and limit fixture of a seed group is first checked unchanged, then mutated inputs (<see cref="FuzzMutator"/>)
/// are checked by <see cref="FuzzOracle"/> (documented exceptions only, no leak or use after return, identification and
/// eager/sequential consistency, no clean end on malformed data), each under a hang watchdog. A failing input is minimized
/// (<see cref="FuzzMinimizer"/>) and written to the test artifacts; commit it under <c>fuzz/Meziantou.Framework.Imaging.FuzzTests/FuzzRegressions</c>
/// (<see cref="FuzzRegressionTests"/>) together with the fix. The budget is set by <see cref="FuzzSettings"/>.
/// </summary>
public sealed class MutationFuzzTests
{
    private const int MaxReportedFailures = 4;

    public static TheoryData<string> Groups => [.. FuzzSeeds.Groups];

    [Theory]
    [MemberData(nameof(Groups))]
    public void MutatedCorpusInputsFailOnlyWithDocumentedErrors(string group)
    {
        var seeds = FuzzSeeds.Get(group);
        var spliceSources = seeds.Select(seed => seed.Data).ToList();
        var random = new FuzzRandom(FuzzSettings.Seed ^ FuzzSeeds.GetGroupSalt(group));
        var failures = new Dictionary<string, string>(StringComparer.Ordinal);
        var outcomes = new Dictionary<string, int>(StringComparer.Ordinal);
        var stopwatch = Stopwatch.StartNew();
        var slowest = TimeSpan.Zero;
        var iterations = FuzzSettings.Iterations;
        var duration = FuzzSettings.Duration;
        var executed = 0;

        // The unchanged seeds first (every reader variant over the whole group), then the mutations
        var cases = seeds.Select((seed, index) => (Name: seed.Id, Data: (byte[]?)seed.Data, Variant: index % 16))
            .Concat(Enumerable.Range(0, iterations).Select(i => (Name: $"{seeds[i % seeds.Count].Id} mutation {i}", Data: (byte[]?)null, Variant: 0)));
        var iteration = 0;
        foreach (var (name, original, fixedVariant) in cases)
        {
            if (duration is { } budget && stopwatch.Elapsed > budget)
                break;

            byte[] input;
            int variant;
            if (original is null)
            {
                input = FuzzMutator.Mutate(random, seeds[iteration % seeds.Count].Data, spliceSources);
                variant = random.Next(256);
                iteration++;
            }
            else
            {
                input = original;
                variant = fixedVariant;
            }

            var started = stopwatch.Elapsed;
            var failure = RunWithWatchdog(input, variant, out var outcome);
            var elapsed = stopwatch.Elapsed - started;
            slowest = elapsed > slowest ? elapsed : slowest;
            executed++;
            outcomes[outcome] = outcomes.GetValueOrDefault(outcome) + 1;
            if (failure is null || failures.ContainsKey(failure.Signature))
                continue;

            failures[failure.Signature] = Report(group, name, input, variant, failure);
            if (failures.Count >= MaxReportedFailures)
                break;
        }

        TestContext.Current.TestOutputHelper?.WriteLine(FormattableString.Invariant(
            $"{group}: {executed} inputs ({seeds.Count} seeds, base seed {FuzzSettings.Seed}) in {stopwatch.Elapsed.TotalSeconds:F1} s, slowest {slowest.TotalMilliseconds:F0} ms; eager load outcomes: {string.Join(", ", outcomes.OrderByDescending(pair => pair.Value).Select(pair => $"{pair.Key}={pair.Value}"))}"));
        if (failures.Count > 0)
            Assert.Fail($"{failures.Count} distinct fuzz failure(s) in group '{group}':{Environment.NewLine}{string.Join(Environment.NewLine + Environment.NewLine, failures.Values)}");

        // The campaign must exercise decoding, not only header rejection
        Assert.True(outcomes.GetValueOrDefault("decoded") > 0, "No mutated or seed input decoded successfully.");
    }

    internal static FuzzFailure? RunWithWatchdog(byte[] input, int variant, out string outcome)
    {
        string? caseOutcome = null;
        var task = Task.Run(() =>
        {
            var failure = FuzzOracle.Check(input, variant, out var result);
            caseOutcome = result;
            return failure;
        });
        if (!task.Wait(FuzzSettings.CaseTimeout))
        {
            outcome = "hang";
            return FuzzFailure.Property("hang", $"The input was not processed within {FuzzSettings.CaseTimeout.TotalSeconds} s (infinite loop or unbounded work).");
        }

        outcome = caseOutcome ?? "?";
        return task.Result;
    }

    private static string Report(string group, string name, byte[] input, int variant, FuzzFailure failure)
    {
        var minimized = failure.Signature == "property:hang" ? input : FuzzMinimizer.Minimize(input, failure.Signature, candidate => RunWithWatchdog(candidate, variant, out _));
        var minimizedFailure = RunWithWatchdog(minimized, variant, out _) ?? failure;
        var directory = TestArtifacts.GetDirectory("fuzz-" + group);
        var fileName = TestArtifacts.SanitizeFileName(failure.Signature);
        if (fileName.Length > 80)
        {
            fileName = fileName[..80];
        }

        var path = directory / (fileName + ".bin");
        File.WriteAllBytes(path, minimized);
        File.WriteAllBytes(directory / (fileName + ".original.bin"), input);
        return string.Create(CultureInfo.InvariantCulture, $"""
            [{failure.Signature}] {name} (reader variant {variant}, {input.Length} bytes, minimized to {minimized.Length} bytes: {path})
            {minimizedFailure.Message}
            minimized (base64): {Convert.ToBase64String(minimized)}
            """);
    }
}
