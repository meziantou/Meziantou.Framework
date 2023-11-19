namespace Meziantou.Framework.Annotations;

[System.Diagnostics.Conditional("FastEnumToString_Attributes")]
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class FastEnumToStringAttribute : Attribute
{
    public FastEnumToStringAttribute(Type enumType)
    {
        EnumType = enumType;
    }

    public bool IsPublic { get; set; } = true;
    public string? ExtensionMethodNamespace { get; set; }

    public Type EnumType { get; }
}
