using System;
using System.Runtime.InteropServices;
using Xunit;

namespace TestUtilities;

[AttributeUsage(AttributeTargets.Method)]
public sealed class RunIfFactAttribute : FactAttribute
{
    public RunIfFactAttribute(FactOperatingSystem operatingSystems, bool enableOnGitHubActions = true)
    {
        OperatingSystems = operatingSystems;
        EnableOnGitHubActions = enableOnGitHubActions;

        if (operatingSystems != FactOperatingSystem.All)
        {
            if (operatingSystems.HasFlag(FactOperatingSystem.Windows) && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return;

            if (operatingSystems.HasFlag(FactOperatingSystem.Linux) && RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return;

            if (operatingSystems.HasFlag(FactOperatingSystem.OSX) && RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return;

            Skip = "Run only on " + operatingSystems;
            return;
        }

        if (!enableOnGitHubActions)
        {
            if (IsOnGitHubActions())
            {
                Skip = "Does not run on GitHub Actions";
                return;
            }
        }
    }

    public static bool IsOnGitHubActions()
    {
        return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));
    }

    public FactOperatingSystem OperatingSystems { get; }

    public bool EnableOnGitHubActions { get; }
}
