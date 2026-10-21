using System.Globalization;
using System.Text.Json;

namespace SpecUpdate;

/// <summary>
/// Compares two reports of the web-platform-tests runner (<c>WPT_REPORT</c>, one JSON object per test), e.g. the reports of
/// two script engines (<c>WPT_ENGINE</c>): the tests whose passing subtests regress or improve, and the duration ratio.
/// </summary>
internal static class WptCompare
{
    /// <summary>Compares the reports and writes the differences. Returns false when a regression is not allowed.</summary>
    /// <param name="baselinePath">The report of the reference run.</param>
    /// <param name="candidatePath">The report of the compared run.</param>
    /// <param name="allowedRegressions">The ids of the tests whose regressions are known (a trailing <c>*</c> matches a prefix).</param>
    /// <param name="output">The writer of the summary.</param>
    public static bool Compare(string baselinePath, string candidatePath, IReadOnlyCollection<string> allowedRegressions, TextWriter output)
    {
        var baseline = Read(baselinePath);
        var candidate = Read(candidatePath);
        var regressions = new List<string>();
        var unlisted = 0;
        var improvements = new List<string>();
        int baselinePassed = 0, baselineTotal = 0, candidatePassed = 0, candidateTotal = 0;
        double baselineDuration = 0, candidateDuration = 0;
        foreach (var id in baseline.Keys.Union(candidate.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var before = baseline.GetValueOrDefault(id);
            var after = candidate.GetValueOrDefault(id);
            baselinePassed += before.Passed;
            baselineTotal += before.Total;
            candidatePassed += after.Passed;
            candidateTotal += after.Total;
            baselineDuration += before.DurationMs;
            candidateDuration += after.DurationMs;
            var line = string.Create(CultureInfo.InvariantCulture, $"{id}: {before.Passed}/{before.Total} -> {after.Passed}/{after.Total}");
            if (after.Passed < before.Passed)
            {
                var allowed = IsAllowed(id, allowedRegressions);
                if (!allowed)
                {
                    unlisted++;
                }

                regressions.Add((allowed ? "  (known) " : "  ") + line + (after.Message is { Length: > 0 } message ? ": " + Truncate(message) : ""));
            }
            else if (after.Passed > before.Passed)
            {
                improvements.Add("  " + line);
            }
        }

        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Tests: {baseline.Count} -> {candidate.Count}; passing subtests: {baselinePassed}/{baselineTotal} -> {candidatePassed}/{candidateTotal}"));
        var ratio = baselineDuration > 0 ? candidateDuration / baselineDuration : double.NaN;
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Duration: {baselineDuration / 1000:0.0}s -> {candidateDuration / 1000:0.0}s (ratio {ratio:0.00})"));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Regressions: {regressions.Count} ({unlisted} not listed)"));
        foreach (var line in regressions)
        {
            output.WriteLine(line);
        }

        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Improvements: {improvements.Count}"));
        foreach (var line in improvements)
        {
            output.WriteLine(line);
        }

        return unlisted == 0;
    }

    /// <summary>
    /// Reads the known regressions: a JSON object whose keys are the test ids (and whose values are the reasons), or a text
    /// file with one test id per line (# starts a comment). A trailing <c>*</c> matches a prefix.
    /// </summary>
    public static string[] ReadAllowedRegressions(string path)
    {
        if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
            return [.. document.RootElement.EnumerateObject().Select(property => property.Name)];
        }

        return [.. File.ReadLines(path).Select(line => line.Trim()).Where(line => line.Length > 0 && !line.StartsWith('#', StringComparison.Ordinal))];
    }

    private static bool IsAllowed(string id, IReadOnlyCollection<string> allowedRegressions)
    {
        foreach (var pattern in allowedRegressions)
        {
            if (pattern.EndsWith('*', StringComparison.Ordinal) ? id.StartsWith(pattern[..^1], StringComparison.Ordinal) : string.Equals(id, pattern, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static string Truncate(string value) => value.Length <= 200 ? value.ReplaceLineEndings(" ") : value[..200].ReplaceLineEndings(" ") + "...";

    /// <summary>Reads a report: the number of passing subtests of each test (a test without subtests counts as one subtest).</summary>
    private static Dictionary<string, TestSummary> Read(string path)
    {
        var result = new Dictionary<string, TestSummary>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var id = root.GetProperty("test").GetString()!;
            var duration = root.TryGetProperty("durationMs", out var durationElement) && durationElement.ValueKind == JsonValueKind.Number ? durationElement.GetDouble() : 0;
            var message = root.TryGetProperty("message", out var messageElement) && messageElement.ValueKind == JsonValueKind.String ? messageElement.GetString() : null;
            int passed = 0, total = 0;
            if (root.TryGetProperty("subtests", out var subtests) && subtests.ValueKind == JsonValueKind.Array && subtests.GetArrayLength() > 0)
            {
                foreach (var subtest in subtests.EnumerateArray())
                {
                    total++;
                    if (subtest.TryGetProperty("status", out var status) && status.GetString() == "PASS")
                    {
                        passed++;
                    }
                }
            }
            else
            {
                // A reftest, or a test whose harness did not complete
                total = 1;
                var status = root.TryGetProperty("reftest", out var reftestElement) ? reftestElement.GetString() : root.TryGetProperty("harness", out var harnessElement) ? harnessElement.GetString() : null;
                passed = status is "PASS" or "OK" ? 1 : 0;
            }

            // A test that runs several times (e.g. retried) keeps its last result
            result[id] = new TestSummary(passed, total, duration, message);
        }

        return result;
    }

    private readonly record struct TestSummary(int Passed, int Total, double DurationMs, string? Message);
}
