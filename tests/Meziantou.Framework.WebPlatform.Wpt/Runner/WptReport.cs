using System.Text.Json;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

/// <summary>Writes the detailed results (including the messages) to the file set in <c>WPT_REPORT</c>, one JSON object per line.</summary>
internal static class WptReport
{
    private static readonly Lock LockObject = new();

    public static void Append(WptTest test, WptResult result, TimeSpan duration)
    {
        if (Path is null)
            return;

        Write(JsonSerializer.Serialize(new
        {
            test = test.Id,
            harness = result.HarnessStatus,
            durationMs = (int)duration.TotalMilliseconds,
            message = result.HarnessMessage,
            subtests = result.Subtests.Select(s => new { name = s.Name, status = s.Status, message = s.Message }),
            log = result.Log,
        }));
    }

    /// <summary>Writes the result of a reftest: its status (<c>reftest</c>) and the differences of the failing comparisons (<c>message</c>).</summary>
    public static void Append(WptReftest test, WptReftestResult result, TimeSpan duration)
    {
        if (Path is null)
            return;

        Write(JsonSerializer.Serialize(new
        {
            test = test.Id,
            reftest = result.Status,
            durationMs = (int)duration.TotalMilliseconds,
            message = result.Message,
            log = result.Log,
        }));
    }

    private static string? Path => Environment.GetEnvironmentVariable("WPT_REPORT") is { Length: > 0 } path ? path : null;

    private static void Write(string line)
    {
        lock (LockObject)
        {
            File.AppendAllText(Path!, line + "\n");
        }
    }
}
