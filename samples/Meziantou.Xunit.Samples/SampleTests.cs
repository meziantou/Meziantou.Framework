// The readme example (src/Meziantou.Xunit/readme.md) is a test class. This project only compiles it, to keep the
// documentation in sync with the API; the attributes are exercised by the test projects of the repository.
#region SampleTests
using Meziantou.Xunit;
using Xunit;

namespace MyProject.Tests;

public sealed class SampleTests
{
    [Fact]
    [RunIf(TestOperatingSystems.Windows | TestOperatingSystems.Linux)]
    public void Runs_on_windows_or_linux()
    {
    }

    [Fact]
    [RunIf(globalizationMode: TestGlobalizationMode.NotInvariant)]
    public void Runs_only_when_invariant_globalization_is_disabled()
    {
    }

    [Fact]
    [SkipIf(WindowsGroups.Administrator)]
    public void Skipped_when_running_as_administrator()
    {
    }

    [Fact]
    [RunIf(ContinuousIntegration = ContinuousIntegrationEnvironments.GitHubActions)]
    public void Runs_only_on_github_actions()
    {
    }
}
#endregion
