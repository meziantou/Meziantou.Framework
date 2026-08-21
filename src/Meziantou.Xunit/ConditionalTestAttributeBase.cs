using System.Reflection;
using System.Security.Principal;
using Xunit.v3;

namespace Meziantou.Xunit;

/// <summary>
/// Base class for attributes that run or skip a test depending on the environment the test suite is running in.
/// </summary>
/// <remarks>
/// <para>
/// Conditions are combined with a logical AND: the attribute matches only when every condition it defines matches.
/// </para>
/// <para>
/// Conditions are evaluated by <see cref="Before(MethodInfo, IXunitTest)"/>, which xUnit calls after the test class instance
/// has been created. Class fixtures, the test class constructor and <c>IAsyncLifetime.InitializeAsync</c> therefore run even
/// when the test ends up being skipped.
/// </para>
/// </remarks>
public abstract class ConditionalTestAttributeBase : BeforeAfterTestAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ConditionalTestAttributeBase"/> class.
    /// </summary>
    protected ConditionalTestAttributeBase()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ConditionalTestAttributeBase"/> class.
    /// </summary>
    /// <param name="operatingSystem">The operating systems the condition matches.</param>
    protected ConditionalTestAttributeBase(TestOperatingSystems operatingSystem)
    {
        OperatingSystem = operatingSystem;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ConditionalTestAttributeBase"/> class.
    /// </summary>
    /// <param name="globalizationMode">The globalization mode the condition matches.</param>
    protected ConditionalTestAttributeBase(TestGlobalizationMode globalizationMode)
    {
        GlobalizationMode = globalizationMode;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ConditionalTestAttributeBase"/> class.
    /// </summary>
    /// <param name="windowsGroup">The Windows group membership the condition matches.</param>
    protected ConditionalTestAttributeBase(WindowsGroups windowsGroup)
    {
        WindowsGroup = windowsGroup;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ConditionalTestAttributeBase"/> class.
    /// </summary>
    /// <param name="operatingSystem">The operating systems the condition matches.</param>
    /// <param name="globalizationMode">The globalization mode the condition matches.</param>
    protected ConditionalTestAttributeBase(TestOperatingSystems operatingSystem, TestGlobalizationMode globalizationMode)
    {
        OperatingSystem = operatingSystem;
        GlobalizationMode = globalizationMode;
    }

    /// <summary>
    /// Gets or sets the operating systems the condition matches. Defaults to <see cref="TestOperatingSystems.None"/>, which does not take part in the condition.
    /// </summary>
    public TestOperatingSystems OperatingSystem { get; set; }

    /// <summary>
    /// Gets or sets the globalization mode the condition matches. Defaults to <see cref="TestGlobalizationMode.Any"/>, which does not take part in the condition.
    /// </summary>
    public TestGlobalizationMode GlobalizationMode { get; set; } = TestGlobalizationMode.Any;

    /// <summary>
    /// Gets or sets the continuous integration environments the condition matches. Defaults to <see cref="ContinuousIntegrationEnvironments.None"/>, which does not take part in the condition.
    /// </summary>
    public ContinuousIntegrationEnvironments ContinuousIntegration { get; set; }

    /// <summary>
    /// Gets or sets the Windows group membership the condition matches. Defaults to <see cref="WindowsGroups.Any"/>, which does not take part in the condition.
    /// </summary>
    public WindowsGroups WindowsGroup { get; set; } = WindowsGroups.Any;

    /// <summary>
    /// Gets a value indicating whether the test is skipped when the condition matches (<see langword="true"/>) or when it does not match (<see langword="false"/>).
    /// </summary>
    protected abstract bool InvertCondition { get; }

    private bool HasCondition => OperatingSystem is not TestOperatingSystems.None ||
                                 GlobalizationMode is not TestGlobalizationMode.Any ||
                                 ContinuousIntegration is not ContinuousIntegrationEnvironments.None ||
                                 WindowsGroup is not WindowsGroups.Any;

    /// <summary>
    /// Evaluates the condition and skips the test when it is not satisfied.
    /// </summary>
    /// <param name="methodUnderTest">The method under test.</param>
    /// <param name="test">The test that is about to run.</param>
    /// <exception cref="InvalidOperationException">The attribute does not define any condition.</exception>
    public override void Before(MethodInfo methodUnderTest, IXunitTest test)
    {
        if (!HasCondition)
        {
            var typeName = GetType().Name;
            var attributeName = typeName.EndsWith("Attribute", StringComparison.Ordinal)
                ? typeName[..^"Attribute".Length]
                : typeName;

            throw new InvalidOperationException($"[{attributeName}] does not define any condition, so it has no effect. Set at least one of {nameof(OperatingSystem)}, {nameof(GlobalizationMode)}, {nameof(ContinuousIntegration)} or {nameof(WindowsGroup)}.");
        }

        var evaluation = EvaluateConditions();
        var shouldSkip = InvertCondition ? evaluation.IsMatch : !evaluation.IsMatch;
        if (!shouldSkip)
            return;

        var reason = InvertCondition
            ? "Skip due to matching condition: " + evaluation.MatchDescription
            : evaluation.FailureReason ?? "Condition is not met";

        throw new InvalidOperationException(DynamicSkipToken.Value + reason);
    }

    private ConditionEvaluation EvaluateConditions()
    {
        if (GlobalizationMode is not TestGlobalizationMode.Any)
        {
            var isInvariant = TestEnvironment.IsGlobalizationInvariant();
            if (GlobalizationMode is TestGlobalizationMode.Invariant && !isInvariant)
                return new ConditionEvaluation(IsMatch: false, MatchDescription: string.Empty, FailureReason: "Run only in invariant globalization mode");

            if (GlobalizationMode is TestGlobalizationMode.NotInvariant && isInvariant)
                return new ConditionEvaluation(IsMatch: false, MatchDescription: string.Empty, FailureReason: "Run only in non-invariant globalization mode");
        }

        if (OperatingSystem is not TestOperatingSystems.None && !IsMatchingOperatingSystem(OperatingSystem))
            return new ConditionEvaluation(IsMatch: false, MatchDescription: string.Empty, FailureReason: "Run only on " + OperatingSystem);

        if (ContinuousIntegration is not ContinuousIntegrationEnvironments.None && !TestEnvironment.IsOnContinuousIntegration(ContinuousIntegration))
            return new ConditionEvaluation(IsMatch: false, MatchDescription: string.Empty, FailureReason: "Run only on " + ContinuousIntegration);

        if (WindowsGroup is not WindowsGroups.Any)
        {
            if (!global::System.OperatingSystem.IsWindows())
                return new ConditionEvaluation(IsMatch: false, MatchDescription: string.Empty, FailureReason: "Run only on Windows");

            if (!IsMatchingWindowsGroup(WindowsGroup))
            {
                var failureReason = WindowsGroup switch
                {
                    WindowsGroups.User => "Run only when the current user is a standard user that is not elevated as an administrator",
                    WindowsGroups.Administrator => "Run only when the current user is elevated as an administrator",
                    _ => "Run only when current user is in Windows group " + WindowsGroup,
                };

                return new ConditionEvaluation(IsMatch: false, MatchDescription: string.Empty, FailureReason: failureReason);
            }
        }

        var matchDescription = InvertCondition ? GetMatchDescription() : string.Empty;
        return new ConditionEvaluation(IsMatch: true, MatchDescription: matchDescription, FailureReason: null);
    }

    private string GetMatchDescription()
    {
        var conditions = new List<string>(capacity: 4);
        if (OperatingSystem is not TestOperatingSystems.None)
            conditions.Add(nameof(OperatingSystem) + " = " + OperatingSystem);

        if (GlobalizationMode is not TestGlobalizationMode.Any)
            conditions.Add(nameof(GlobalizationMode) + " = " + GlobalizationMode);

        if (ContinuousIntegration is not ContinuousIntegrationEnvironments.None)
            conditions.Add(nameof(ContinuousIntegration) + " = " + ContinuousIntegration);

        if (WindowsGroup is not WindowsGroups.Any)
            conditions.Add(nameof(WindowsGroup) + " = " + WindowsGroup);

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
        // Also required by the platform compatibility analyzer for WindowsIdentity
        if (!global::System.OperatingSystem.IsWindows())
            return false;

        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return windowsGroup switch
        {
            // Administrators are members of the built-in Users group, so they must be excluded explicitly
            WindowsGroups.User => principal.IsInRole(WindowsBuiltInRole.User) && !principal.IsInRole(WindowsBuiltInRole.Administrator),
            WindowsGroups.Administrator => principal.IsInRole(WindowsBuiltInRole.Administrator),
            _ => false,
        };
    }

    private readonly record struct ConditionEvaluation(bool IsMatch, string MatchDescription, string? FailureReason);
}
