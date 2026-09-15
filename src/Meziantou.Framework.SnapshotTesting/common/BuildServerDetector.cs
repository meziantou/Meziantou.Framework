namespace Meziantou.Framework;

/// <summary>
/// Detects the environments where the developer does not interact with the tests: a build server, but also a
/// container or WSL, where no diff tool can show up. Snapshots are not updated and merge tools are not launched there.
/// </summary>
internal static class BuildServerDetector
{
    /// <summary>Gets the name of the environment variable that identified the environment, or <see langword="null" /> when none was found.</summary>
    public static string? DetectedVariable { get; } = Detect(Environment.GetEnvironmentVariable);

    public static bool Detected => DetectedVariable is not null;

    internal static string? Detect(Func<string, string?> getEnvironmentVariable)
    {
        foreach (var name in (ReadOnlySpan<string>)["CI", "GITHUB_ACTION", "JENKINS_URL", "TEAMCITY_VERSION", "GITLAB_CI", "GO_SERVER_URL", "TRAVIS_BUILD_ID", "DOTNET_RUNNING_IN_CONTAINER", "APPVEYOR", "WSL_DISTRO_NAME"])
        {
            if (getEnvironmentVariable(name) is not null)
                return name;
        }

        if (string.Equals(getEnvironmentVariable("BuildRunner"), "MyGet", StringComparison.OrdinalIgnoreCase))
            return "BuildRunner";

        if (string.Equals(getEnvironmentVariable("TF_BUILD"), "True", StringComparison.OrdinalIgnoreCase))
            return "TF_BUILD";

        return null;
    }
}
