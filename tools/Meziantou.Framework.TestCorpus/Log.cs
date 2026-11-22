namespace Meziantou.Framework.TestCorpus;

/// <summary>The messages of the fetchers.</summary>
public static class Log
{
    /// <summary>Writes a warning: an annotation in GitHub Actions, else a line of the standard error.</summary>
    public static void Warning(string message)
    {
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") is "true")
        {
            Console.WriteLine("::warning::" + message);
        }
        else
        {
            Console.Error.WriteLine("warning: " + message);
        }
    }
}
