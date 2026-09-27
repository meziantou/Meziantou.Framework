namespace Meziantou.Framework.Toml;

internal sealed class TomlKebabCaseUpperNamingPolicy : TomlSeparatorNamingPolicy
{
    internal TomlKebabCaseUpperNamingPolicy()
        : base('-', upperCase: true)
    {
    }
}
