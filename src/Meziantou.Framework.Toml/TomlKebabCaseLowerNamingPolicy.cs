namespace Meziantou.Framework.Toml;

internal sealed class TomlKebabCaseLowerNamingPolicy : TomlSeparatorNamingPolicy
{
    internal TomlKebabCaseLowerNamingPolicy()
        : base('-', upperCase: false)
    {
    }
}
