namespace Meziantou.Framework;

/// <summary>
/// Detects an unattended build. WSL and containers are deliberately not treated as one: both are ordinary
/// local development environments, and a build server running inside a container sets one of the variables
/// below anyway.
/// </summary>
internal static class BuildServerDetector
{
    private static bool HasEnvironmentVariable(string name) => Environment.GetEnvironmentVariable(name) is not null;

    private static bool HasEnvironmentVariable(string name, string value) => string.Equals(Environment.GetEnvironmentVariable(name), value, StringComparison.OrdinalIgnoreCase);

    public static bool Detected { get; } = HasEnvironmentVariable("CI")
        || HasEnvironmentVariable("GITHUB_ACTION")
        || HasEnvironmentVariable("JENKINS_URL")
        || HasEnvironmentVariable("TEAMCITY_VERSION")
        || HasEnvironmentVariable("GITLAB_CI")
        || HasEnvironmentVariable("GO_SERVER_URL")
        || HasEnvironmentVariable("TRAVIS_BUILD_ID")
        || HasEnvironmentVariable("APPVEYOR")
        || HasEnvironmentVariable("BuildRunner", "MyGet")
        || HasEnvironmentVariable("TF_BUILD", "True")
        ;
}
