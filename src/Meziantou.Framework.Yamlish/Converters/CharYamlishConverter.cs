namespace Meziantou.Framework.Yamlish.Converters;

internal sealed class CharYamlishConverter : ScalarYamlishConverter<char>
{
    protected override char Parse(string value) => value.Length is 1 ? value[0] : throw new FormatException($"Cannot convert '{value}' to '{typeof(char)}'.");

    protected override string Format(char value) => value.ToString();
}
