using System.Reflection;
using System.Security.Principal;
using Xunit.v3;

namespace Meziantou.Xunit;

public abstract class ConditionalTestAttributeBase : BeforeAfterTestAttribute
{
    protected ConditionalTestAttributeBase()
    {
    }

    protected ConditionalTestAttributeBase(TestOperatingSystems operatingSystem)
    {
        OperatingSystem = operatingSystem;
    }

    protected ConditionalTestAttributeBase(TestGlobalizationMode globalizationMode)
    {
        GlobalizationMode = globalizationMode;
    }

    protected ConditionalTestAttributeBase(WindowsGroups windowsGroup)
    {
        WindowsGroup = windowsGroup;
    }

    protected ConditionalTestAttributeBase(TestOperatingSystems operatingSystem, TestGlobalizationMode globalizationMode)
    {
        OperatingSystem = operatingSystem;
        GlobalizationMode = globalizationMode;
    }

    public TestOperatingSystems OperatingSystem { get; set; }
    public TestGlobalizationMode GlobalizationMode { get; set; } = TestGlobalizationMode.Any;
    public ContinuousIntegrationEnvironments ContinuousIntegration { get; set; }
    public WindowsGroups WindowsGroup { get; set; } = WindowsGroups.Any;

    protected abstract bool InvertCondition { get; }

    public override void Before(MethodInfo methodUnderTest, IXunitTest test)
    {
        var evaluation = EvaluateConditions(OperatingSystem, GlobalizationMode, ContinuousIntegration, WindowsGroup);
        if (!evaluation.HasCondition)
            return;

        var shouldSkip = InvertCondition ? evaluation.IsMatch : !evaluation.IsMatch;
        if (!shouldSkip)
            return;

        var reason = InvertCondition
            ? "Skip due to matching condition: " + evaluation.MatchDescription
            : evaluation.FailureReason ?? "Condition is not met";

        throw new InvalidOperationException("$XunitDynamicSkip$" + reason);
    }

    private static ConditionEvaluation EvaluateConditions(TestOperatingSystems operatingSystem, TestGlobalizationMode globalizationMode, ContinuousIntegrationEnvironments continuousIntegration, WindowsGroups windowsGroup)
    {
        var hasCondition = operatingSystem != TestOperatingSystems.None ||
                           globalizationMode != TestGlobalizationMode.Any ||
                           continuousIntegration != ContinuousIntegrationEnvironments.None ||
                           windowsGroup != WindowsGroups.Any;

        if (!hasCondition)
            return new ConditionEvaluation(HasCondition: false, IsMatch: true, MatchDescription: string.Empty, FailureReason: null);

        if (globalizationMode != TestGlobalizationMode.Any)
        {
            var isEnabled = TestEnvironment.IsGlobalizationInvariant();
            if (globalizationMode is TestGlobalizationMode.Enabled && !isEnabled)
            {
                return new ConditionEvaluation(HasCondition: true, IsMatch: false, MatchDescription: string.Empty, FailureReason: "Run only in invariant globalization mode");
            }

            if (globalizationMode is TestGlobalizationMode.Disabled && isEnabled)
            {
                return new ConditionEvaluation(HasCondition: true, IsMatch: false, MatchDescription: string.Empty, FailureReason: "Run only in non-invariant globalization mode");
            }
        }

        if (operatingSystem != TestOperatingSystems.None && !IsMatchingOperatingSystem(operatingSystem))
            return new ConditionEvaluation(HasCondition: true, IsMatch: false, MatchDescription: string.Empty, FailureReason: "Run only on " + operatingSystem);

        if (continuousIntegration != ContinuousIntegrationEnvironments.None && !TestEnvironment.IsOnContinuousIntegration(continuousIntegration))
            return new ConditionEvaluation(HasCondition: true, IsMatch: false, MatchDescription: string.Empty, FailureReason: "Run only on " + continuousIntegration);

        if (windowsGroup != WindowsGroups.Any)
        {
            if (!global::System.OperatingSystem.IsWindows())
                return new ConditionEvaluation(HasCondition: true, IsMatch: false, MatchDescription: string.Empty, FailureReason: "Run only on Windows");

            if (!IsMatchingWindowsGroup(windowsGroup))
                return new ConditionEvaluation(HasCondition: true, IsMatch: false, MatchDescription: string.Empty, FailureReason: "Run only when current user is in Windows group " + windowsGroup);
        }

        var matchDescription = GetMatchDescription(operatingSystem, globalizationMode, continuousIntegration, windowsGroup);
        return new ConditionEvaluation(HasCondition: true, IsMatch: true, MatchDescription: matchDescription, FailureReason: null);
    }

    private static string GetMatchDescription(TestOperatingSystems operatingSystem, TestGlobalizationMode globalizationMode, ContinuousIntegrationEnvironments continuousIntegration, WindowsGroups windowsGroup)
    {
        var conditions = new List<string>(capacity: 4);
        if (operatingSystem != TestOperatingSystems.None)
            conditions.Add(nameof(OperatingSystem) + " = " + operatingSystem);

        if (globalizationMode != TestGlobalizationMode.Any)
            conditions.Add(nameof(GlobalizationMode) + " = " + globalizationMode);

        if (continuousIntegration != ContinuousIntegrationEnvironments.None)
            conditions.Add(nameof(ContinuousIntegration) + " = " + continuousIntegration);

        if (windowsGroup != WindowsGroups.Any)
            conditions.Add(nameof(WindowsGroup) + " = " + windowsGroup);

        return string.Join(", ", conditions);
    }

    private static bool IsMatchingOperatingSystem(TestOperatingSystems operatingSystem)
    {
        if (operatingSystem.HasFlag(TestOperatingSystems.Windows) && global::System.OperatingSystem.IsWindows())
            return true;

        if (operatingSystem.HasFlag(TestOperatingSystems.Linux) && global::System.OperatingSystem.IsLinux())
            return true;

        if (operatingSystem.HasFlag(TestOperatingSystems.MacOS) && global::System.OperatingSystem.IsMacOS())
            return true;

        return false;
    }

    private static bool IsMatchingWindowsGroup(WindowsGroups windowsGroup)
    {
        if (!global::System.OperatingSystem.IsWindows())
            return false;

        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return windowsGroup switch
        {
            WindowsGroups.User => principal.IsInRole(WindowsBuiltInRole.User),
            WindowsGroups.Administrator => principal.IsInRole(WindowsBuiltInRole.Administrator),
            _ => false,
        };
    }

    private readonly record struct ConditionEvaluation(bool HasCondition, bool IsMatch, string MatchDescription, string? FailureReason);
}
