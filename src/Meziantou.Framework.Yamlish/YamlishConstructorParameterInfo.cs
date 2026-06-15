namespace Meziantou.Framework.Yamlish;

internal sealed record YamlishConstructorParameterInfo(string SerializedName, Type ParameterType, bool IsOptional, object? DefaultValue);
