using Meziantou.Framework.Yaml.Model;

namespace Meziantou.Framework.JavaScriptEngine.Test262.Runner;

/// <summary>The frontmatter of a test262 file (https://github.com/tc39/test262/blob/main/INTERPRETING.md#metadata).</summary>
internal sealed class Test262Metadata
{
    public IReadOnlyList<string> Includes { get; private init; } = [];

    public IReadOnlyList<string> Flags { get; private init; } = [];

    public IReadOnlyList<string> Features { get; private init; } = [];

    public IReadOnlyList<string> Locales { get; private init; } = [];

    /// <summary>The phase of the expected error (<c>parse</c>, <c>resolution</c> or <c>runtime</c>), or null for a positive test.</summary>
    public string? NegativePhase { get; private init; }

    /// <summary>The name of the constructor of the expected error (e.g. <c>SyntaxError</c>).</summary>
    public string? NegativeType { get; private init; }

    public bool HasFlag(string flag) => Flags.Contains(flag, StringComparer.Ordinal);

    public bool IsModule => HasFlag("module");

    public bool IsRaw => HasFlag("raw");

    public bool IsAsync => HasFlag("async");

    public static Test262Metadata Parse(string source)
    {
        var start = source.IndexOf("/*---", StringComparison.Ordinal);
        if (start < 0)
            return new Test262Metadata();

        var end = source.IndexOf("---*/", start, StringComparison.Ordinal);
        if (end < 0)
            throw new FormatException("The frontmatter is not terminated");

        var yaml = source[(start + 5)..end];
        var stream = YamlStream.Load(new StringReader(yaml));
        if (stream.Count == 0 || stream[0].Contents is not YamlMapping root)
            return new Test262Metadata();

        string? negativePhase = null;
        string? negativeType = null;
        if (Get(root, "negative") is YamlMapping negative)
        {
            negativePhase = GetScalar(negative, "phase");
            negativeType = GetScalar(negative, "type");
        }

        return new Test262Metadata
        {
            Includes = GetList(root, "includes"),
            Flags = GetList(root, "flags"),
            Features = GetList(root, "features"),
            Locales = GetList(root, "locale"),
            NegativePhase = negativePhase,
            NegativeType = negativeType,
        };
    }

    private static YamlElement? Get(YamlMapping mapping, string key)
    {
        foreach (var entry in mapping)
        {
            if (entry.Key is YamlValue { Value: var name } && name == key)
                return entry.Value;
        }

        return null;
    }

    private static string? GetScalar(YamlMapping mapping, string key) => Get(mapping, key) is YamlValue value ? value.Value : null;

    private static string[] GetList(YamlMapping mapping, string key)
    {
        return Get(mapping, key) switch
        {
            YamlSequence sequence => [.. sequence.OfType<YamlValue>().Select(value => value.Value)],
            YamlValue { Value: { Length: > 0 } value } => [value],
            _ => [],
        };
    }
}
