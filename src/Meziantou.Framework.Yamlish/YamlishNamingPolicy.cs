namespace Meziantou.Framework.Yamlish;

public abstract class YamlishNamingPolicy
{
    public static YamlishNamingPolicy CamelCase { get; } = new CamelCaseNamingPolicy();

    public static YamlishNamingPolicy SnakeCaseLower { get; } = new SnakeCaseLowerNamingPolicy();

    public abstract string ConvertName(string name);

    private sealed class CamelCaseNamingPolicy : YamlishNamingPolicy
    {
        public override string ConvertName(string name)
        {
            ArgumentException.ThrowIfNullOrEmpty(name);
            if (char.IsLower(name[0]))
                return name;

            return char.ToLowerInvariant(name[0]) + name[1..];
        }
    }

    private sealed class SnakeCaseLowerNamingPolicy : YamlishNamingPolicy
    {
        public override string ConvertName(string name)
        {
            ArgumentException.ThrowIfNullOrEmpty(name);
            var builder = new StringBuilder(name.Length + 4);
            for (var i = 0; i < name.Length; i++)
            {
                var character = name[i];
                if (char.IsUpper(character))
                {
                    if (i > 0 && (char.IsLower(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1]))))
                        builder.Append('_');

                    builder.Append(char.ToLowerInvariant(character));
                }
                else
                {
                    builder.Append(character);
                }
            }

            return builder.ToString();
        }
    }
}
