using System.Reflection;
using System;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Xunit;
using Xunit.v3;

namespace TestUtilities;

[AttributeUsage(AttributeTargets.Method)]
public sealed class RunIfWindowsAdministratorAttribute : BeforeAfterTestAttribute
{
    public override void Before(MethodInfo methodUnderTest, IXunitTest test)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new Exception("$XunitDynamicSkip$Run only on Windows");

        var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
            throw new Exception("$XunitDynamicSkip$Current user is not in the administrator group");
    }
}
