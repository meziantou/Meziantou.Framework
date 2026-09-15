namespace Meziantou.Framework;

/// <summary>
/// Detects whether the process runs on a continuous integration server, where a snapshot must never be updated.
/// </summary>
/// <remarks>
/// Only variables that a build server sets are used. Variables that are also set on a developer machine - such as
/// <c>WSL_DISTRO_NAME</c> in every WSL shell or <c>DOTNET_RUNNING_IN_CONTAINER</c> in a devcontainer - would silently
/// disable snapshot updates for that developer.
/// </remarks>
internal static class BuildServerDetector
{
    /// <summary>Gets the name of the environment variable that identified the build server, or <see langword="null" /> when none was found.</summary>
    public static string? DetectedVariable { get; } = Detect(Environment.GetEnvironmentVariable);

    public static bool Detected => DetectedVariable is not null;

    internal static string? Detect(Func<string, string?> getEnvironmentVariable)
    {
        if (IsTrue("CI"))
            return "CI";

        foreach (var name in (ReadOnlySpan<string>)["GITHUB_ACTION", "JENKINS_URL", "TEAMCITY_VERSION", "GITLAB_CI", "GO_SERVER_URL", "TRAVIS_BUILD_ID", "APPVEYOR"])
        {
            if (!string.IsNullOrEmpty(getEnvironmentVariable(name)))
                return name;
        }

        if (string.Equals(getEnvironmentVariable("BuildRunner"), "MyGet", StringComparison.OrdinalIgnoreCase))
            return "BuildRunner";

        if (IsTrue("TF_BUILD"))
            return "TF_BUILD";

        return null;

        // CI=false and an empty CI= are how a script says it is not running on a build server.
        bool IsTrue(string name)
        {
            var value = getEnvironmentVariable(name)?.Trim();
            return value is "1" ||
                string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
        }
    }
}
