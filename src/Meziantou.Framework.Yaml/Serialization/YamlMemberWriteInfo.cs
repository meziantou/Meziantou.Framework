namespace Meziantou.Framework.Yaml.Serialization;

/// <summary>The properties of a member that are used to write it.</summary>
internal readonly record struct YamlMemberWriteInfo(
    string Name,
    Type DeclaringType,
    YamlIgnoreCondition? IgnoreCondition,
    bool DisallowNull,
    YamlSequenceItemStyle BlockSequenceMappingStyle,
    YamlSequenceItemStyle BlockSequenceSequenceStyle,
    ScalarStyle StringStyle);
