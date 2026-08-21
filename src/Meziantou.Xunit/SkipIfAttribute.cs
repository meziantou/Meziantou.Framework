namespace Meziantou.Xunit;

/// <summary>
/// Skips the test when every condition defined by the attribute matches; otherwise the test runs.
/// </summary>
/// <remarks>
/// The attribute can be applied to a test method and to its test class. When both define a condition, the test is skipped when either of them matches.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class SkipIfAttribute : ConditionalTestAttributeBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SkipIfAttribute"/> class. At least one condition must be set using the properties of the attribute.
    /// </summary>
    public SkipIfAttribute()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SkipIfAttribute"/> class.
    /// </summary>
    /// <param name="operatingSystem">The operating systems the test is skipped on.</param>
    public SkipIfAttribute(TestOperatingSystems operatingSystem)
        : base(operatingSystem)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SkipIfAttribute"/> class.
    /// </summary>
    /// <param name="globalizationMode">The globalization mode the test is skipped in.</param>
    public SkipIfAttribute(TestGlobalizationMode globalizationMode)
        : base(globalizationMode)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SkipIfAttribute"/> class.
    /// </summary>
    /// <param name="windowsGroup">The Windows group membership that causes the test to be skipped.</param>
    public SkipIfAttribute(WindowsGroups windowsGroup)
        : base(windowsGroup)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SkipIfAttribute"/> class.
    /// </summary>
    /// <param name="operatingSystem">The operating systems the test is skipped on.</param>
    /// <param name="globalizationMode">The globalization mode the test is skipped in.</param>
    public SkipIfAttribute(TestOperatingSystems operatingSystem, TestGlobalizationMode globalizationMode)
        : base(operatingSystem, globalizationMode)
    {
    }

    /// <inheritdoc />
    protected override bool InvertCondition => true;
}
