using System;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Xunit;

namespace TestUtilities
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class RunIfWindowsAdministratorFactAttribute : FactAttribute
    {
        public RunIfWindowsAdministratorFactAttribute()
        {
#if NETSTANDARD2_0 || NETCOREAPP3_1
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Skip = "Run only on Windows";
                return;
            }
#elif NET461
#else
#error Plateform not supported
#endif

            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
            {
                Skip = "Current user is not in the administator group";
            }
        }
    }
}
