namespace Meziantou.Framework.Toml;

internal sealed class TomlPascalCaseNamingPolicy : TomlNamingPolicy
{
    public override string ConvertName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (string.IsNullOrEmpty(name) || !char.IsLower(name[0]))
        {
            return name;
        }

        var chars = name.ToCharArray();
        chars[0] = char.ToUpperInvariant(chars[0]);
        return new string(chars);
    }
}
