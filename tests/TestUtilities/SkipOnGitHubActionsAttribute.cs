using System.Reflection;
using Xunit.v3;

namespace TestUtilities;

public sealed class SkipOnGitHubActionsAttribute : BeforeAfterTestAttribute
{
    public SkipOnGitHubActionsAttribute(FactOperatingSystem operatingSystems = FactOperatingSystem.All)
    {
        OperatingSystems = operatingSystems;
    }

    public FactOperatingSystem OperatingSystems { get; }

    public static bool IsOnGitHubActions()
    {
        return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));
    }

    public override void Before(MethodInfo methodUnderTest, IXunitTest test)
    {
        if (IsOnGitHubActions() && IsMatchingOperatingSystem(OperatingSystems))
            throw new Exception("$XunitDynamicSkip$Does not run on GitHub Actions");
    }

    private static bool IsMatchingOperatingSystem(FactOperatingSystem operatingSystems)
    {
        if (operatingSystems == FactOperatingSystem.All)
            return true;

        if (operatingSystems.HasFlag(FactOperatingSystem.Windows) && OperatingSystem.IsWindows())
            return true;

        if (operatingSystems.HasFlag(FactOperatingSystem.Linux) && OperatingSystem.IsLinux())
            return true;

        if (operatingSystems.HasFlag(FactOperatingSystem.OSX) && OperatingSystem.IsMacOS())
            return true;

        return false;
    }
}
