namespace Meziantou.Framework.Toml;

internal sealed class TomlSnakeCaseLowerNamingPolicy : TomlSeparatorNamingPolicy
{
    internal TomlSnakeCaseLowerNamingPolicy()
        : base('_', upperCase: false)
    {
    }
}
