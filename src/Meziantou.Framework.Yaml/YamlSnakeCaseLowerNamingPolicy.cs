namespace Meziantou.Framework.Yaml;

internal sealed class YamlSnakeCaseLowerNamingPolicy : YamlSeparatorNamingPolicy
{
    internal YamlSnakeCaseLowerNamingPolicy()
        : base('_', upperCase: false)
    {
    }
}
