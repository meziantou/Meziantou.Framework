namespace Meziantou.Framework.Imaging.FuzzTests;

/// <summary>
/// Budget of the mutation fuzz tests. The default budget is small and deterministic, so the fuzz
/// tests run in every CI job; a longer campaign is opt-in through environment variables:
/// <list type="bullet">
/// <item><description><c>MEZIANTOU_FRAMEWORK_IMAGING_FUZZ_ITERATIONS</c>: mutated inputs per seed group (default 1500);</description></item>
/// <item><description><c>MEZIANTOU_FRAMEWORK_IMAGING_FUZZ_SEED</c>: base seed (default fixed; any 64-bit integer gives another reproducible campaign);</description></item>
/// <item><description><c>MEZIANTOU_FRAMEWORK_IMAGING_FUZZ_DURATION_SECONDS</c>: optional wall-clock budget per seed group (stops earlier when reached);</description></item>
/// <item><description><c>MEZIANTOU_FRAMEWORK_IMAGING_FUZZ_CASE_TIMEOUT_SECONDS</c>: a single input running longer than this is reported as a hang (default 60).</description></item>
/// </list>
/// </summary>
internal static class FuzzSettings
{
    public const string IterationsVariable = "MEZIANTOU_FRAMEWORK_IMAGING_FUZZ_ITERATIONS";
    public const string SeedVariable = "MEZIANTOU_FRAMEWORK_IMAGING_FUZZ_SEED";
    public const string DurationVariable = "MEZIANTOU_FRAMEWORK_IMAGING_FUZZ_DURATION_SECONDS";
    public const string CaseTimeoutVariable = "MEZIANTOU_FRAMEWORK_IMAGING_FUZZ_CASE_TIMEOUT_SECONDS";

    public const int DefaultIterations = 1500;
    public const ulong DefaultSeed = 0x4D657A69616E746FUL;

    public static int Iterations => ReadInt32(IterationsVariable) ?? DefaultIterations;

    public static ulong Seed => ulong.TryParse(Environment.GetEnvironmentVariable(SeedVariable), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : DefaultSeed;

    public static TimeSpan? Duration => ReadInt32(DurationVariable) is { } seconds ? TimeSpan.FromSeconds(seconds) : null;

    public static TimeSpan CaseTimeout => TimeSpan.FromSeconds(ReadInt32(CaseTimeoutVariable) ?? 60);

    private static int? ReadInt32(string variable)
        => int.TryParse(Environment.GetEnvironmentVariable(variable), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0 ? value : null;
}
