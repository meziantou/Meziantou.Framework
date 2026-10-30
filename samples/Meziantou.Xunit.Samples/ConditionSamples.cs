// The examples of the documentation (docs/Xunit). This project only compiles them: the test classes are nested in
// this class so that each one can be read on its own.
using Meziantou.Xunit;
using Xunit;

namespace MyProject.Tests;

public static class ConditionSamples
{
    #region RunIf_ClassAndMethod
    [RunIf(TestOperatingSystems.Windows | TestOperatingSystems.MacOS)]
    public sealed class DesktopTests
    {
        [Fact]
        public void Runs_on_windows_or_macos()
        {
        }

        // The attributes of the class and of the method both apply
        [Fact]
        [SkipIf(ContinuousIntegration = ContinuousIntegrationEnvironments.GitHubActions)]
        public void Runs_on_windows_or_macos_except_on_github_actions()
        {
        }
    }
    #endregion

    #region RunIf_SeveralConditions
    public sealed class FormattingTests
    {
        // Runs only when every condition matches: on Linux, with the culture data available
        [Fact]
        [RunIf(TestOperatingSystems.Linux, TestGlobalizationMode.NotInvariant)]
        public void Runs_on_linux_with_culture_data()
        {
        }

        // Skipped only when every condition matches: on GitHub Actions, in invariant globalization mode
        [Fact]
        [SkipIf(ContinuousIntegration = ContinuousIntegrationEnvironments.GitHubActions, GlobalizationMode = TestGlobalizationMode.Invariant)]
        public void Skipped_on_github_actions_in_invariant_mode()
        {
        }
    }
    #endregion

    #region ConditionalTestAttributeBase_CustomAttribute
    // A condition you use in many places, under a name of your own
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
    public sealed class RunOnLinuxCIAttribute : ConditionalTestAttributeBase
    {
        public RunOnLinuxCIAttribute()
            : base(TestOperatingSystems.Linux)
        {
            ContinuousIntegration = ContinuousIntegrationEnvironments.GitHubActions;
        }

        // false: the test runs when the conditions match, like RunIf; true: it is skipped, like SkipIf
        protected override bool InvertCondition => false;
    }

    public sealed class DeploymentTests
    {
        [Fact]
        [RunOnLinuxCI]
        public void Publishes_the_container_image()
        {
        }
    }
    #endregion

    #region TestEnvironment_IsOnGitHubActions
    public sealed class ImportTests
    {
        [Fact]
        public async Task Imports_the_file_within_the_timeout()
        {
            // Shared CI runners are slower than a developer machine
            var timeout = TestEnvironment.IsOnGitHubActions() ? TimeSpan.FromMinutes(2) : TimeSpan.FromSeconds(30);
            using var cancellationTokenSource = new CancellationTokenSource(timeout);

            await File.WriteAllTextAsync(Path.GetTempFileName(), "content", cancellationTokenSource.Token);
        }
    }
    #endregion
}
