using System;
using Xunit;

namespace TestUtilities
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class RunIfTheoryAttribute : TheoryAttribute
    {
        public RunIfTheoryAttribute(FactOperatingSystem operatingSystems, bool enableOnGitHubActions = true, FactInvariantGlobalizationMode globalizationMode = FactInvariantGlobalizationMode.Any)
        {
            OperatingSystems = operatingSystems;
            EnableOnGitHubActions = enableOnGitHubActions;
            GlobalizationMode = globalizationMode;

            Skip = RunIfFactAttribute.GetSkipReason(operatingSystems, enableOnGitHubActions, globalizationMode);
        }

        public FactOperatingSystem OperatingSystems { get; }
        public bool EnableOnGitHubActions { get; }
        public FactInvariantGlobalizationMode GlobalizationMode { get; }
    }
}
