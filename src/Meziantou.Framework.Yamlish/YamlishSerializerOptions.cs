namespace Meziantou.Framework.Yamlish;

public sealed class YamlishSerializerOptions
{
    public YamlishNamingPolicy? PropertyNamingPolicy { get; set; }

    public StringComparer PropertyNameComparer { get; set; } = StringComparer.OrdinalIgnoreCase;

    public int IndentSize { get; set; } = 2;

    public int MaxDepth { get; set; } = 64;

    public bool IgnoreNullValues { get; set; } = true;

    public bool IncludeFields { get; set; }
}
