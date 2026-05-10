namespace Meziantou.Xunit;

public sealed class SkipIfAttribute : ConditionalTestAttributeBase
{
    public SkipIfAttribute()
    {
    }

    public SkipIfAttribute(TestOperatingSystems operatingSystem)
        : base(operatingSystem)
    {
    }

    public SkipIfAttribute(TestGlobalizationMode globalizationMode)
        : base(globalizationMode)
    {
    }

    public SkipIfAttribute(WindowsGroups windowsGroup)
        : base(windowsGroup)
    {
    }

    public SkipIfAttribute(TestOperatingSystems operatingSystem, TestGlobalizationMode globalizationMode)
        : base(operatingSystem, globalizationMode)
    {
    }

    protected override bool InvertCondition => true;
}
