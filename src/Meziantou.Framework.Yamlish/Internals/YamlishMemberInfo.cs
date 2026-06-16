namespace Meziantou.Framework.Yamlish.Internals;

internal sealed record YamlishMemberInfo(
    string SerializedName,
    Type MemberType,
    Func<object, object?> GetValue,
    Action<object, object?>? SetValue,
    YamlishIgnoreCondition IgnoreCondition,
    YamlishSequenceStyle SequenceStyle,
    YamlishScalarStyle ScalarStyle,
    YamlishScalarChomping ScalarChomping,
    object? DefaultValue,
    bool IsRequired,
    bool IsGetNullable,
    bool IsSetNullable);
