namespace Meziantou.Xunit;

public static class TestEnvironment
{
    public static bool IsGlobalizationInvariant()
    {
        if (AppContext.TryGetSwitch("System.Globalization.Invariant", out var isEnabled))
            return isEnabled;

        return false;
    }

    public static bool IsOnContinuousIntegration(ContinuousIntegrationEnvironment environment)
    {
        if (environment == ContinuousIntegrationEnvironment.None)
            return false;

        if (environment.HasFlag(ContinuousIntegrationEnvironment.GitHubActions) && IsOnGitHubActions())
            return true;

        return false;
    }

    public static bool IsOnGitHubActions()
    {
        return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));
    }
}
