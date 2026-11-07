namespace Meziantou.Framework.Yaml.Serialization.Converters;

/// <summary>Formats an enum value with the names used by the enum converter.</summary>
internal interface IYamlEnumNameFormatter
{
    string FormatName(object value);
}
