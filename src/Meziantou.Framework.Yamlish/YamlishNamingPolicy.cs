using System.Text.Json;

namespace Meziantou.Framework.Yamlish;

public abstract class YamlishNamingPolicy
{
    public static YamlishNamingPolicy CamelCase { get; } = new JsonNamingPolicyAdapter(JsonNamingPolicy.CamelCase);

    public static YamlishNamingPolicy SnakeCaseLower { get; } = new JsonNamingPolicyAdapter(JsonNamingPolicy.SnakeCaseLower);

    public static YamlishNamingPolicy SnakeCaseUpper { get; } = new JsonNamingPolicyAdapter(JsonNamingPolicy.SnakeCaseUpper);

    public static YamlishNamingPolicy KebabCaseLower { get; } = new JsonNamingPolicyAdapter(JsonNamingPolicy.KebabCaseLower);

    public static YamlishNamingPolicy KebabCaseUpper { get; } = new JsonNamingPolicyAdapter(JsonNamingPolicy.KebabCaseUpper);

    public static YamlishNamingPolicy PascalCase { get; } = new PascalCaseNamingPolicy();

    public abstract string ConvertName(string name);

    private sealed class JsonNamingPolicyAdapter(JsonNamingPolicy policy) : YamlishNamingPolicy
    {
        public override string ConvertName(string name) => policy.ConvertName(name);
    }

    private sealed class PascalCaseNamingPolicy : YamlishNamingPolicy
    {
        public override string ConvertName(string name)
        {
            ArgumentException.ThrowIfNullOrEmpty(name);
            if (char.IsUpper(name[0]))
                return name;

            return char.ToUpperInvariant(name[0]) + name[1..];
        }
    }
}
