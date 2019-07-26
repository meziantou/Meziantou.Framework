using System;
using Xunit;

namespace TestUtilities
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class RunIfWindowsFactAttribute : FactAttribute
    {
        public RunIfWindowsFactAttribute()
        {
#if NETSTANDARD2_0
            if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            {
                Skip = "Run only on Windows";
                return;
            }
#elif NET461
#else
#error Plateform not supported
#endif
        }
    }
}
