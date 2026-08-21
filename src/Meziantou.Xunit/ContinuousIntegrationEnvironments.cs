namespace Meziantou.Xunit;

/// <summary>
/// Describes the continuous integration environments a test requires.
/// </summary>
[Flags]
public enum ContinuousIntegrationEnvironments
{
    /// <summary>
    /// The continuous integration environment is not part of the condition.
    /// </summary>
    None = 0,

    /// <summary>
    /// GitHub Actions.
    /// </summary>
    GitHubActions = 1,
}
