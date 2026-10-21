namespace Meziantou.Framework.JavaScriptEngine.Test262.Runner;

/// <summary>Runs a test262 file with the engine, in each of its modes.</summary>
internal static class Test262Executor
{
    public static IReadOnlyList<Test262Result> Run(Test262Test test)
    {
        var results = new List<Test262Result>();
        foreach (var mode in test.Modes)
        {
            results.Add(new Test262Result(mode, Passed: false, "The engine cannot run scripts yet"));
        }

        return results;
    }
}
