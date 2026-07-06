namespace Meziantou.Framework.Yaml;

internal sealed class YamlKebabCaseLowerNamingPolicy : YamlSeparatorNamingPolicy
{
    internal YamlKebabCaseLowerNamingPolicy()
        : base('-', upperCase: false)
    {
    }
}
