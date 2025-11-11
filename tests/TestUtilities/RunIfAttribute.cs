using System.Reflection;
using Xunit.v3;

namespace TestUtilities;

public sealed class RunIfAttribute : BeforeAfterTestAttribute
{
    public RunIfAttribute(FactOperatingSystem operatingSystems = FactOperatingSystem.All, FactInvariantGlobalizationMode globalizationMode = FactInvariantGlobalizationMode.Any)
    {
        OperatingSystems = operatingSystems;
        GlobalizationMode = globalizationMode;
    }

    public static bool IsGlobalizationInvariant()
    {
        if (AppContext.TryGetSwitch("System.Globalization.Invariant", out var isEnabled))
            return isEnabled;

        return false;
    }

    public FactOperatingSystem OperatingSystems { get; }
    public FactInvariantGlobalizationMode GlobalizationMode { get; }

    public override void Before(MethodInfo methodUnderTest, IXunitTest test)
    {
        var skipReason = GetSkipReason(OperatingSystems, GlobalizationMode);

        // We use the dynamic skip exception message pattern to turn this into a skipped test
        // when it's not running on one of the targeted OSes
        if (skipReason is not null)
            throw new Exception("$XunitDynamicSkip$" + skipReason);
    }

    private static string? GetSkipReason(FactOperatingSystem operatingSystems, FactInvariantGlobalizationMode globalizationMode = FactInvariantGlobalizationMode.Any)
    {
        if (globalizationMode != FactInvariantGlobalizationMode.Any)
        {
            var isEnabled = IsGlobalizationInvariant();
            if (globalizationMode == FactInvariantGlobalizationMode.Enabled && !isEnabled)
            {
                return "Does not run in non-invariant globalization mode";
            }

            if (globalizationMode == FactInvariantGlobalizationMode.Disabled && isEnabled)
            {
                return "Does not run in invariant globalization mode";
            }
        }

        if (operatingSystems != FactOperatingSystem.NotSpecified)
        {
            if (!IsValidOperatingSystem(operatingSystems))
                return "Run only on " + operatingSystems;
        }

        return null;

        static bool IsValidOperatingSystem(FactOperatingSystem operatingSystems)
        {
            if (operatingSystems.HasFlag(FactOperatingSystem.Windows) && OperatingSystem.IsWindows())
                return true;

            if (operatingSystems.HasFlag(FactOperatingSystem.Linux) && OperatingSystem.IsLinux())
                return true;

            if (operatingSystems.HasFlag(FactOperatingSystem.OSX) && OperatingSystem.IsMacOS())
                return true;

            return false;
        }
    }
}
