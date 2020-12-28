using System;
using System.Runtime.InteropServices;
using Xunit;

namespace TestUtilities
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class RunIfTheoryAttribute : TheoryAttribute
    {
        public RunIfTheoryAttribute(FactOperatingSystem operatingSystems)
        {
            OperatingSystems = operatingSystems;

            if (operatingSystems != FactOperatingSystem.None)
            {
                if (operatingSystems.HasFlag(FactOperatingSystem.Windows) && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    return;

                if (operatingSystems.HasFlag(FactOperatingSystem.Linux) && RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    return;

                if (operatingSystems.HasFlag(FactOperatingSystem.OSX) && RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    return;

                Skip = "Run only on " + operatingSystems;
            }
        }

        public FactOperatingSystem OperatingSystems { get; }
    }
}
