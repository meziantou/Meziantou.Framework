using System.Reflection;
using System.Security.Principal;
using Xunit.v3;

namespace TestUtilities;

[AttributeUsage(AttributeTargets.Method)]
public sealed class RunIfWindowsAdministratorAttribute : BeforeAfterTestAttribute
{
    public override void Before(MethodInfo methodUnderTest, IXunitTest test)
    {
        if (!OperatingSystem.IsWindows())
            throw new Exception("$XunitDynamicSkip$Run only on Windows");

        var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
            throw new Exception("$XunitDynamicSkip$Current user is not in the administrator group");
    }
}
