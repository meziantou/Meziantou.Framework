using System.Reflection;
using Xunit.v3;

namespace Meziantou.Framework.Unix.ControlGroups.Tests;

public sealed class RequiresCGroup2Attribute : BeforeAfterTestAttribute
{
    public override void Before(MethodInfo methodUnderTest, IXunitTest test)
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new Exception("$XunitDynamicSkip$Test runs only on Linux");
        }

        if (!Directory.Exists("/sys/fs/cgroup"))
        {
            throw new Exception("$XunitDynamicSkip$cgroup v2 not available");
        }

        if (!Environment.IsPrivilegedProcess)
        {
            throw new Exception("$XunitDynamicSkip$Test requires elevated privileges");
        }

        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != null)
        {
            throw new Exception("$XunitDynamicSkip$Test cannot run in GitHub Actions");
        }
    }
}
