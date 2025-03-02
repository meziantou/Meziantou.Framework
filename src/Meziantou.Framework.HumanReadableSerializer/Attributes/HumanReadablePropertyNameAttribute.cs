namespace Meziantou.Framework.HumanReadable;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class HumanReadablePropertyNameAttribute : HumanReadableAttribute
{
    public HumanReadablePropertyNameAttribute(string name) => Name = name;

    public string Name { get; }
}
