namespace Meziantou.Xunit;

public sealed class RunIfAttribute : ConditionalTestAttributeBase
{
    public RunIfAttribute()
    {
    }

    public RunIfAttribute(TestOperatingSystems operatingSystem)
        : base(operatingSystem)
    {
    }

    public RunIfAttribute(TestGlobalizationMode globalizationMode)
        : base(globalizationMode)
    {
    }

    public RunIfAttribute(WindowsGroups windowsGroup)
        : base(windowsGroup)
    {
    }

    public RunIfAttribute(TestOperatingSystems operatingSystem, TestGlobalizationMode globalizationMode)
        : base(operatingSystem, globalizationMode)
    {
    }

    protected override bool InvertCondition => false;
}
