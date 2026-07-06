namespace Meziantou.Framework.Yaml;

internal sealed class YamlKebabCaseUpperNamingPolicy : YamlSeparatorNamingPolicy
{
    internal YamlKebabCaseUpperNamingPolicy()
        : base('-', upperCase: true)
    {
    }
}
