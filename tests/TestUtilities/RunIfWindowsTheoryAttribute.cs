using System;
using Xunit;

namespace TestUtilities
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class RunIfWindowsTheoryAttribute : TheoryAttribute
    {
        public RunIfWindowsTheoryAttribute()
        {
            if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            {
                Skip = "Run only on Windows";
                return;
            }
        }
    }
}
