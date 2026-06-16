namespace Meziantou.Framework.Yamlish.Internals;

internal sealed record YamlishMemberInfo(
    string SerializedName,
    Type MemberType,
    Func<object, object?> GetValue,
    Action<object, object?>? SetValue,
    YamlishIgnoreCondition IgnoreCondition,
    object? DefaultValue,
    bool IsRequired,
    bool IsGetNullable,
    bool IsSetNullable);
