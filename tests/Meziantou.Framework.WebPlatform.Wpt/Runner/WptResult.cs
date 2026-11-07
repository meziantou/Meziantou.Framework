namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

internal sealed record WptSubtestResult(string Name, string Status, string? Message);

internal sealed record WptResult(string HarnessStatus, string? HarnessMessage, IReadOnlyList<WptSubtestResult> Subtests, IReadOnlyList<string> Log)
{
    public static string GetSubtestStatus(int status) => status switch
    {
        0 => "PASS",
        1 => "FAIL",
        2 => "TIMEOUT",
        3 => "NOTRUN",
        4 => "PRECONDITION_FAILED",
        _ => "UNKNOWN",
    };

    public static string GetHarnessStatus(int status) => status switch
    {
        0 => "OK",
        1 => "ERROR",
        2 => "TIMEOUT",
        3 => "PRECONDITION_FAILED",
        _ => "UNKNOWN",
    };
}
