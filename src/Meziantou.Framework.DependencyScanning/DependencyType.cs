namespace Meziantou.Framework.DependencyScanning
{
    public enum DependencyType
    {
        Unknown,
        NuGet,
        Npm,
        PyPi,
        DockerImage,
        GitSubmodule,
        DotNetSdk,
        GitHubActions,
    }
}
