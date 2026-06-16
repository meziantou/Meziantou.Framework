namespace Meziantou.Framework.Yamlish.Internals;

internal sealed record YamlishConstructorParameterInfo(string SerializedName, Type ParameterType, bool IsOptional, object? DefaultValue, bool IsNullable);
