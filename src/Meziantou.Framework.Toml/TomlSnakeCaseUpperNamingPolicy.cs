namespace Meziantou.Framework.Toml;

internal sealed class TomlSnakeCaseUpperNamingPolicy : TomlSeparatorNamingPolicy
{
    internal TomlSnakeCaseUpperNamingPolicy()
        : base('_', upperCase: true)
    {
    }
}
