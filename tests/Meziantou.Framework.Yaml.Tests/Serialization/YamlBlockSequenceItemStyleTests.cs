using Meziantou.Framework.Yaml.Serialization;

namespace Meziantou.Framework.Yaml.Tests.Serialization;
public sealed class YamlBlockSequenceItemStyleTests
{
    [Fact]
    public void SequenceMappings_DefaultToCompactStyle()
    {
        var yaml = YamlSerializer.Serialize(CreateConfig(), new YamlSerializerOptions { PropertyNamingPolicy = YamlNamingPolicy.CamelCase });

        Assert.Equal("contexts:\n  - name: default\n    target: localhost\n    credential: localhost-admin\n", yaml);
    }

    [Fact]
    public void SequenceMappings_CanBeExpandedGlobally()
    {
        var yaml = YamlSerializer.Serialize(
            CreateConfig(),
            new YamlSerializerOptions
            {
                PropertyNamingPolicy = YamlNamingPolicy.CamelCase,
                BlockSequenceMappingStyle = YamlSequenceItemStyle.Expanded,
            });

        Assert.Equal("contexts:\n  -\n    name: default\n    target: localhost\n    credential: localhost-admin\n", yaml);
    }

    [Fact]
    public void SequenceMappings_CanBeExpandedForOneMember()
    {
        var yaml = YamlSerializer.Serialize(new MixedStyleConfig(), new YamlSerializerOptions { PropertyNamingPolicy = YamlNamingPolicy.CamelCase });

        Assert.Contains("compact:\n  - name: compact\n    target: localhost\n    credential: token\n", yaml);
        Assert.Contains("expanded:\n  -\n    name: expanded\n    target: localhost\n    credential: token\n", yaml);
    }

    [Fact]
    public void SequenceMappings_CanBeCompactedForOneMemberWhenGlobalDefaultIsExpanded()
    {
        var yaml = YamlSerializer.Serialize(
            new MemberCompactConfig(),
            new YamlSerializerOptions
            {
                PropertyNamingPolicy = YamlNamingPolicy.CamelCase,
                BlockSequenceMappingStyle = YamlSequenceItemStyle.Expanded,
            });

        Assert.Equal("contexts:\n  - name: compact\n    target: localhost\n    credential: token\n", yaml);
    }

    [Fact]
    public void SequenceSequences_CanBeCompactedGlobally()
    {
        var yaml = YamlSerializer.Serialize(
            new RowsConfig { Rows = new List<List<string>> { new() { "a", "b" } } },
            new YamlSerializerOptions
            {
                PropertyNamingPolicy = YamlNamingPolicy.CamelCase,
                BlockSequenceSequenceStyle = YamlSequenceItemStyle.Compact,
            });

        Assert.Equal("rows:\n  - - a\n    - b\n", yaml);
    }

    [Fact]
    public void SequenceSequences_CanBeCompactedForOneMember()
    {
        var yaml = YamlSerializer.Serialize(new MemberCompactRowsConfig(), new YamlSerializerOptions { PropertyNamingPolicy = YamlNamingPolicy.CamelCase });

        Assert.Equal("rows:\n  - - a\n    - b\n", yaml);
    }

    [Fact]
    public void SourceGeneratedMemberOverride_UsesBlockSequenceItemStyleAttribute()
    {
        var context = new BlockSequenceItemStyleYamlContext(new YamlSerializerOptions
        {
            BlockSequenceMappingStyle = YamlSequenceItemStyle.Expanded,
        });
        var value = new GeneratedMemberCompactConfig
        {
            Contexts =
            [
                new GeneratedContextItem { Name = "compact", Target = "localhost", Credential = "token" },
            ],
        };

        var yaml = YamlSerializer.Serialize(value, typeof(GeneratedMemberCompactConfig), context);

        Assert.Equal("Contexts:\n  - Name: compact\n    Target: localhost\n    Credential: token\n", yaml);
    }

    private static Config CreateConfig() => new()
    {
        Contexts = new List<Context>
        {
            new()
            {
                Name = "default",
                Target = "localhost",
                Credential = "localhost-admin",
            },
        },
    };

    private sealed class Config
    {
        public List<Context> Contexts { get; set; } = new();
    }

    private sealed class MixedStyleConfig
    {
        public List<Context> Compact { get; set; } =
        [
            new Context { Name = "compact", Target = "localhost", Credential = "token" },
        ];

        [YamlBlockSequenceItemStyle(YamlSequenceItemStyle.Expanded)]
        public List<Context> Expanded { get; set; } =
        [
            new Context { Name = "expanded", Target = "localhost", Credential = "token" },
        ];
    }

    private sealed class MemberCompactConfig
    {
        [YamlBlockSequenceItemStyle(YamlSequenceItemStyle.Compact)]
        public List<Context> Contexts { get; set; } =
        [
            new Context { Name = "compact", Target = "localhost", Credential = "token" },
        ];
    }

    private sealed class RowsConfig
    {
        public List<List<string>> Rows { get; set; } = new();
    }

    private sealed class MemberCompactRowsConfig
    {
        [YamlBlockSequenceItemStyle(SequenceStyle = YamlSequenceItemStyle.Compact)]
        public List<List<string>> Rows { get; set; } =
        [
            ["a", "b"],
        ];
    }

    private sealed class Context
    {
        public string Name { get; set; } = string.Empty;

        public string Target { get; set; } = string.Empty;

        public string Credential { get; set; } = string.Empty;
    }
}

#pragma warning disable MA0048 // File name must match type name
[YamlSerializable(typeof(GeneratedMemberCompactConfig))]
[YamlSerializable(typeof(GeneratedContextItem))]
internal sealed partial class BlockSequenceItemStyleYamlContext : YamlSerializerContext
{
    public BlockSequenceItemStyleYamlContext(YamlSerializerOptions options)
        : base(options)
    {
    }
}

internal sealed class GeneratedMemberCompactConfig
{
    [YamlBlockSequenceItemStyle(YamlSequenceItemStyle.Compact)]
    public List<GeneratedContextItem> Contexts { get; set; } = new();
}

internal sealed class GeneratedContextItem
{
    public string Name { get; set; } = string.Empty;

    public string Target { get; set; } = string.Empty;

    public string Credential { get; set; } = string.Empty;
}
