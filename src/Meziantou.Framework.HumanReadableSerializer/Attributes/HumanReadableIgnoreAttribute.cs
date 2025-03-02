namespace Meziantou.Framework.HumanReadable;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = true)]
public sealed class HumanReadableIgnoreAttribute : HumanReadableAttribute
{
    /// <summary>
    /// Specifies the condition that must be met before a property or field will be ignored.
    /// </summary>
    /// <remarks>The default value is <see cref="HumanReadableIgnoreCondition.Always"/>.</remarks>
    public HumanReadableIgnoreCondition Condition { get; set; } = HumanReadableIgnoreCondition.Always;

    /// <summary>
    /// Use when <see cref="Condition" /> is <see cref="HumanReadableIgnoreCondition.Custom"/>
    /// </summary>
    public Func<HumanReadableIgnoreData, bool>? CustomCondition { get; set; }

    /// <summary>
    /// Initializes a new instance of <see cref="HumanReadableIgnoreAttribute"/>.
    /// </summary>
    public HumanReadableIgnoreAttribute() { }
}
