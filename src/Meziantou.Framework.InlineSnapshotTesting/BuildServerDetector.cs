namespace Meziantou.Framework.InlineSnapshotTesting;

internal static class BuildServerDetector
{
    private static bool HasEnvironmentVariable(string name) => Environment.GetEnvironmentVariable(name) != null;
    private static bool HasEnvironmentVariable(string name, string value) => string.Equals(Environment.GetEnvironmentVariable(name), value, StringComparison.OrdinalIgnoreCase);

    public static bool Detected { get; } = HasEnvironmentVariable("CI")
        || HasEnvironmentVariable("GITHUB_ACTION")
        || HasEnvironmentVariable("JENKINS_URL")
        || HasEnvironmentVariable("TEAMCITY_VERSION")
        || HasEnvironmentVariable("GITLAB_CI")
        || HasEnvironmentVariable("GO_SERVER_URL")
        || HasEnvironmentVariable("TRAVIS_BUILD_ID")
        || HasEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER")
        || HasEnvironmentVariable("APPVEYOR")
        || HasEnvironmentVariable("WSL_DISTRO_NAME")
        || HasEnvironmentVariable("BuildRunner", "MyGet")
        || HasEnvironmentVariable("TF_BUILD", "True")
        ;
}
