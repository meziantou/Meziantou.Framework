using Meziantou.Framework;

namespace Meziantou.Xunit;

/// <summary>
/// Provides information about the environment the test suite is running in.
/// </summary>
public static class TestEnvironment
{
    /// <summary>
    /// Determines whether the application runs in invariant globalization mode.
    /// </summary>
    /// <returns><see langword="true"/> when invariant globalization mode is enabled; otherwise <see langword="false"/>.</returns>
    /// <remarks>
    /// This mirrors how the runtime resolves the setting: the <c>System.Globalization.Invariant</c> switch, which is set by the
    /// <c>InvariantGlobalization</c> MSBuild property, takes precedence over the <c>DOTNET_SYSTEM_GLOBALIZATION_INVARIANT</c>
    /// environment variable.
    /// </remarks>
    public static bool IsGlobalizationInvariant() => GlobalizationHelper.IsGlobalizationInvariant();

    /// <summary>
    /// Determines whether the test suite runs on one of the specified continuous integration environments.
    /// </summary>
    /// <param name="environment">The continuous integration environments to test for.</param>
    /// <returns><see langword="true"/> when the test suite runs on any of the specified environments; otherwise <see langword="false"/>.</returns>
    public static bool IsOnContinuousIntegration(ContinuousIntegrationEnvironments environment)
    {
        if (environment == ContinuousIntegrationEnvironments.None)
            return false;

        if (environment.HasFlag(ContinuousIntegrationEnvironments.GitHubActions) && IsOnGitHubActions())
            return true;

        return false;
    }

    /// <summary>
    /// Determines whether the test suite runs on GitHub Actions.
    /// </summary>
    /// <returns><see langword="true"/> when the test suite runs on GitHub Actions; otherwise <see langword="false"/>.</returns>
    public static bool IsOnGitHubActions()
    {
        return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));
    }
}
