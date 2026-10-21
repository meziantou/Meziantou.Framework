using Meziantou.Framework.JavaScriptEngine.Test262.Runner;

namespace Meziantou.Framework.JavaScriptEngine.Test262;

public sealed class Test262SummaryTests
{
    [Fact]
    public void SummaryIsUpToDate()
    {
        if (!Test262Paths.IsAvailable)
        {
            global::Xunit.Assert.Skip("test262 is not fetched. Run 'dotnet run eng/javascriptengine/fetch-tests.cs'.");
        }

        var expected = Test262Summary.Generate();
        var actual = File.Exists(Test262Paths.SummaryFile) ? File.ReadAllText(Test262Paths.SummaryFile) : "";
        if (expected != actual)
        {
            if (Test262Expectations.UpdateMode)
            {
                File.WriteAllText(Test262Paths.SummaryFile, expected);
                return;
            }

            Assert.Fail("summary.md is out of date: run the tests with TEST262_UPDATE_EXPECTATIONS=1, or run this test alone with it.");
        }
    }
}
