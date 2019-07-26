using System;
using System.Runtime.InteropServices;
using Xunit;

namespace TestUtilities
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class RunIfWindowsFactAttribute : FactAttribute
    {
        public RunIfWindowsFactAttribute()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Skip = "Run only on Windows";
                return;
            }
        }
    }
}
