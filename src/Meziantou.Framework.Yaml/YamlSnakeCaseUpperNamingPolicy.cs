namespace Meziantou.Framework.Yaml;

internal sealed class YamlSnakeCaseUpperNamingPolicy : YamlSeparatorNamingPolicy
{
    internal YamlSnakeCaseUpperNamingPolicy()
        : base('_', upperCase: true)
    {
    }
}
