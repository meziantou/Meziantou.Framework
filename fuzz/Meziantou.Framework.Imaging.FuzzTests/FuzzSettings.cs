namespace Meziantou.Framework.Imaging.FuzzTests;

/// <summary>
/// Budget of the mutation fuzz tests. The default budget is small and deterministic, so the fuzz
/// tests run in every CI job; a longer campaign is opt-in through environment variables (the weekly campaign of
/// <c>.github/workflows/fuzz-imaging.yml</c> sets them, runs the tests with <c>eng/run-imaging-fuzz.ps1</c> and opens an issue for every failure):
/// <list type="bullet">
/// <item><description><c>MEZIANTOU_FRAMEWORK_IMAGING_FUZZ_ITERATIONS</c>: mutated inputs per seed group (default 1500);</description></item>
/// <item><description><c>MEZIANTOU_FRAMEWORK_IMAGING_FUZZ_SEED</c>: base seed (default fixed; any 64-bit integer gives another reproducible campaign);</description></item>
/// <item><description><c>MEZIANTOU_FRAMEWORK_IMAGING_FUZZ_DURATION_SECONDS</c>: optional wall-clock budget per seed group (stops earlier when reached);</description></item>
/// <item><description><c>MEZIANTOU_FRAMEWORK_IMAGING_FUZZ_CASE_TIMEOUT_SECONDS</c>: a single input running longer than this is reported as a hang (default 60).</description></item>
/// <item><description><c>MEZIANTOU_FRAMEWORK_IMAGING_FUZZ_GROUPS</c>: the seed groups of a campaign, separated by commas (default: every group).</description></item>
/// <item><description><c>MEZIANTOU_FRAMEWORK_IMAGING_FUZZ_SHARD</c>: <c>index/count</c>, the seed groups whose position modulo <c>count</c> is <c>index</c> (default: every group); splits a campaign across CI jobs.</description></item>
/// </list>
/// </summary>
internal static class FuzzSettings
{
    public const string IterationsVariable = "MEZIANTOU_FRAMEWORK_IMAGING_FUZZ_ITERATIONS";
    public const string SeedVariable = "MEZIANTOU_FRAMEWORK_IMAGING_FUZZ_SEED";
    public const string DurationVariable = "MEZIANTOU_FRAMEWORK_IMAGING_FUZZ_DURATION_SECONDS";
    public const string CaseTimeoutVariable = "MEZIANTOU_FRAMEWORK_IMAGING_FUZZ_CASE_TIMEOUT_SECONDS";
    public const string SeedFilterVariable = "MEZIANTOU_FRAMEWORK_IMAGING_FUZZ_SEED_FILTER";
    public const string GroupsVariable = "MEZIANTOU_FRAMEWORK_IMAGING_FUZZ_GROUPS";
    public const string ShardVariable = "MEZIANTOU_FRAMEWORK_IMAGING_FUZZ_SHARD";

    public const int DefaultIterations = 1500;
    public const ulong DefaultSeed = 0x4D657A69616E746FUL;

    public static int Iterations => ReadInt32(IterationsVariable) ?? DefaultIterations;

    public static ulong Seed => ulong.TryParse(Environment.GetEnvironmentVariable(SeedVariable), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : DefaultSeed;

    public static TimeSpan? Duration => ReadInt32(DurationVariable) is { } seconds ? TimeSpan.FromSeconds(seconds) : null;

    /// <summary>
    /// Gets a text the identifier of a seed must contain for a campaign on one kind of input, or <see langword="null"/>
    /// for every seed. Only the fuzzers whose seeds have identifiers use it.
    /// </summary>
    public static string? SeedFilter => Environment.GetEnvironmentVariable(SeedFilterVariable) is { Length: > 0 } value ? value : null;

    /// <summary>Gets the seed groups of a campaign on some groups only, or <see langword="null"/> for every group.</summary>
    public static IReadOnlyList<string>? Groups => Environment.GetEnvironmentVariable(GroupsVariable) is { Length: > 0 } value ? value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : null;

    /// <summary>Gets the shard of a campaign split across jobs, or <see langword="null"/> for every group.</summary>
    public static (int Index, int Count)? Shard
    {
        get
        {
            var value = Environment.GetEnvironmentVariable(ShardVariable);
            if (string.IsNullOrEmpty(value))
                return null;

            var parts = value.Split('/');
            if (parts.Length != 2 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var index) || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var count) || count < 1 || index >= count)
                throw new InvalidOperationException($"{ShardVariable} must be 'index/count' with 0 <= index < count, not '{value}'.");

            return (index, count);
        }
    }

    public static TimeSpan CaseTimeout => TimeSpan.FromSeconds(ReadInt32(CaseTimeoutVariable) ?? 60);

    private static int? ReadInt32(string variable)
        => int.TryParse(Environment.GetEnvironmentVariable(variable), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0 ? value : null;
}
