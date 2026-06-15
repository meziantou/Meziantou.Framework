namespace Meziantou.Framework.Yamlish;

internal sealed record YamlishMemberInfo(string SerializedName, Type MemberType, Func<object, object?> GetValue, Action<object, object?>? SetValue, YamlishIgnoreCondition IgnoreCondition, object? DefaultValue);
