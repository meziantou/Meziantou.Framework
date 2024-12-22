using System.Reflection;
using Xunit.v3;

namespace TestUtilities;

public sealed class SkipOnGitHubActionsAttribute : BeforeAfterTestAttribute
{
    public static bool IsOnGitHubActions()
    {
        return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));
    }

    public override void Before(MethodInfo methodUnderTest, IXunitTest test)
    {
        if (IsOnGitHubActions())
            throw new Exception("$XunitDynamicSkip$Does not run on GitHub Actions");
    }
}
