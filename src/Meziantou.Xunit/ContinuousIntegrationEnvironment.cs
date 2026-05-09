namespace Meziantou.Xunit;

[Flags]
public enum ContinuousIntegrationEnvironment
{
    None = 0,
    GitHubActions = 1,
}
