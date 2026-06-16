using System.Text.Json;

namespace Meziantou.Framework.Yamlish;

/// <summary>Determines how Yamlish property names are converted during serialization and deserialization.</summary>
public abstract class YamlishNamingPolicy
{
    /// <summary>Gets a naming policy that converts property names to camel case.</summary>
    public static YamlishNamingPolicy CamelCase { get; } = new JsonNamingPolicyAdapter(JsonNamingPolicy.CamelCase);

    /// <summary>Gets a naming policy that converts property names to lower snake case.</summary>
    public static YamlishNamingPolicy SnakeCaseLower { get; } = new JsonNamingPolicyAdapter(JsonNamingPolicy.SnakeCaseLower);

    /// <summary>Gets a naming policy that converts property names to upper snake case.</summary>
    public static YamlishNamingPolicy SnakeCaseUpper { get; } = new JsonNamingPolicyAdapter(JsonNamingPolicy.SnakeCaseUpper);

    /// <summary>Gets a naming policy that converts property names to lower kebab case.</summary>
    public static YamlishNamingPolicy KebabCaseLower { get; } = new JsonNamingPolicyAdapter(JsonNamingPolicy.KebabCaseLower);

    /// <summary>Gets a naming policy that converts property names to upper kebab case.</summary>
    public static YamlishNamingPolicy KebabCaseUpper { get; } = new JsonNamingPolicyAdapter(JsonNamingPolicy.KebabCaseUpper);

    /// <summary>Gets a naming policy that converts property names to Pascal case.</summary>
    public static YamlishNamingPolicy PascalCase { get; } = new PascalCaseNamingPolicy();

    /// <summary>Converts the specified property name according to the naming policy.</summary>
    /// <param name="name">The property name to convert.</param>
    /// <returns>The converted property name.</returns>
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
