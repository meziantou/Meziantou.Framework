namespace Meziantou.Framework.JavaScriptEngine.Test262.Runner;

/// <summary>Writes the failures (with their messages) to the file of the TEST262_REPORT environment variable: one line per failing run, tab-separated.</summary>
internal static class Test262Report
{
    private static readonly Lock LockObject = new();
    private static readonly string? ReportPath = Environment.GetEnvironmentVariable("TEST262_REPORT") is { Length: > 0 } path ? path : null;

    public static void Write(Test262Test test, IReadOnlyList<Test262Result> results)
    {
        if (ReportPath is null)
            return;

        lock (LockObject)
        {
            foreach (var result in results)
            {
                if (!result.Passed)
                {
                    var message = (result.Message ?? "").Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ');
                    File.AppendAllText(ReportPath, test.Id + "\t" + result.Mode + "\t" + message + "\n");
                }
            }
        }
    }
}
