namespace Meziantou.Xunit;

/// <summary>
/// Runs the test only when every condition defined by the attribute matches; otherwise the test is skipped.
/// </summary>
/// <remarks>
/// The attribute can be applied to a test method and to its test class. When both define a condition, the test runs only when both match.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RunIfAttribute : ConditionalTestAttributeBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RunIfAttribute"/> class. At least one condition must be set using the properties of the attribute.
    /// </summary>
    public RunIfAttribute()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RunIfAttribute"/> class.
    /// </summary>
    /// <param name="operatingSystem">The operating systems the test runs on.</param>
    public RunIfAttribute(TestOperatingSystems operatingSystem)
        : base(operatingSystem)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RunIfAttribute"/> class.
    /// </summary>
    /// <param name="globalizationMode">The globalization mode the test runs in.</param>
    public RunIfAttribute(TestGlobalizationMode globalizationMode)
        : base(globalizationMode)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RunIfAttribute"/> class.
    /// </summary>
    /// <param name="windowsGroup">The Windows group membership the test requires.</param>
    public RunIfAttribute(WindowsGroups windowsGroup)
        : base(windowsGroup)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RunIfAttribute"/> class.
    /// </summary>
    /// <param name="operatingSystem">The operating systems the test runs on.</param>
    /// <param name="globalizationMode">The globalization mode the test runs in.</param>
    public RunIfAttribute(TestOperatingSystems operatingSystem, TestGlobalizationMode globalizationMode)
        : base(operatingSystem, globalizationMode)
    {
    }

    /// <inheritdoc />
    protected override bool InvertCondition => false;
}
