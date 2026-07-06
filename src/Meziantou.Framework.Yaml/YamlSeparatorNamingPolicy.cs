namespace Meziantou.Framework.Yaml;

internal abstract class YamlSeparatorNamingPolicy : YamlNamingPolicy
{
    private readonly char _separator;
    private readonly bool _upperCase;

    protected YamlSeparatorNamingPolicy(char separator, bool upperCase)
    {
        _separator = separator;
        _upperCase = upperCase;
    }

    public override string ConvertName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var builder = new StringBuilder(name.Length + 8);

        for (var i = 0; i < name.Length; i++)
        {
            var current = name[i];
            if (char.IsUpper(current))
            {
                var previousIsLowerOrDigit = i > 0 && (char.IsLower(name[i - 1]) || char.IsDigit(name[i - 1]));
                var nextIsLower = i + 1 < name.Length && char.IsLower(name[i + 1]);
                if (builder.Length > 0 && (previousIsLowerOrDigit || nextIsLower))
                {
                    builder.Append(_separator);
                }

                builder.Append(_upperCase ? current : char.ToLowerInvariant(current));
            }
            else
            {
                builder.Append(_upperCase ? char.ToUpperInvariant(current) : current);
            }
        }

        return builder.ToString();
    }
}
