using System.Globalization;

namespace Meziantou.Framework.Pdf.FuzzTests;

/// <summary>
/// Budget of the mutation fuzz tests. The default budget is small and deterministic, so the fuzz tests run in every CI
/// job; a longer campaign is opt-in through environment variables:
/// <list type="bullet">
/// <item><description><c>MEZIANTOU_FRAMEWORK_PDF_FUZZ_ITERATIONS</c>: mutated inputs per seed (default 60);</description></item>
/// <item><description><c>MEZIANTOU_FRAMEWORK_PDF_FUZZ_SEED</c>: base seed (default fixed; any 64-bit integer gives another reproducible campaign);</description></item>
/// <item><description><c>MEZIANTOU_FRAMEWORK_PDF_FUZZ_CASE_TIMEOUT_SECONDS</c>: a single input running longer than this is a failure (default 20).</description></item>
/// </list>
/// </summary>
internal static class PdfFuzzSettings
{
    public const ulong DefaultSeed = 0x5044465A5A4D4546UL;

    public static int Iterations => ReadInt32("MEZIANTOU_FRAMEWORK_PDF_FUZZ_ITERATIONS") ?? 60;

    public static ulong Seed => ulong.TryParse(Environment.GetEnvironmentVariable("MEZIANTOU_FRAMEWORK_PDF_FUZZ_SEED"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : DefaultSeed;

    public static TimeSpan CaseTimeout => TimeSpan.FromSeconds(ReadInt32("MEZIANTOU_FRAMEWORK_PDF_FUZZ_CASE_TIMEOUT_SECONDS") ?? 20);

    private static int? ReadInt32(string name) => int.TryParse(Environment.GetEnvironmentVariable(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0 ? value : null;
}
