using System;
using System.Runtime.InteropServices;
using Xunit;

namespace TestUtilities
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class RunIfFactAttribute : FactAttribute
    {
        public RunIfFactAttribute(FactOperatingSystem operatingSystems = FactOperatingSystem.All, bool enableOnGitHubActions = true, FactInvariantGlobalizationMode globalizationMode = FactInvariantGlobalizationMode.Any)
        {
            OperatingSystems = operatingSystems;
            EnableOnGitHubActions = enableOnGitHubActions;
            GlobalizationMode = globalizationMode;

            Skip = GetSkipReason(operatingSystems, enableOnGitHubActions, globalizationMode);
        }

        internal static string GetSkipReason(FactOperatingSystem operatingSystems, bool enableOnGitHubActions = true, FactInvariantGlobalizationMode globalizationMode = FactInvariantGlobalizationMode.Any)
        {
            if (!enableOnGitHubActions)
            {
                if (IsOnGitHubActions())
                {
                    return "Does not run on GitHub Actions";
                }
            }

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
            if (operatingSystems != FactOperatingSystem.All)
            {
                if (!IsValidOperatingSystem(operatingSystems))
                    return "Run only on " + operatingSystems;
            }

            return null;

            static bool IsValidOperatingSystem(FactOperatingSystem operatingSystems)
            {
                if (operatingSystems.HasFlag(FactOperatingSystem.Windows) && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    return true;

                if (operatingSystems.HasFlag(FactOperatingSystem.Linux) && RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    return true;

                if (operatingSystems.HasFlag(FactOperatingSystem.OSX) && RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    return true;

                return false;
            }
        }

        public static bool IsOnGitHubActions()
        {
            return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));
        }

        public static bool IsGlobalizationInvariant()
        {
            if (AppContext.TryGetSwitch("System.Globalization.Invariant", out var isEnabled))
                return isEnabled;

            return false;
        }

        public FactOperatingSystem OperatingSystems { get; }
        public bool EnableOnGitHubActions { get; }
        public FactInvariantGlobalizationMode GlobalizationMode { get; }
    }
}
