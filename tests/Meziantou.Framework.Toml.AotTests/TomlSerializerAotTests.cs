using System;
using System.Collections.Generic;
using Meziantou.Framework.Toml;
using Meziantou.Framework.Toml.Model;
using Meziantou.Framework.Toml.Serialization;

namespace Meziantou.Framework.Toml.AotTests;

#pragma warning disable MA0048 // File name must match type name

public sealed class TomlSerializerAotTests
{
    private const string ConfigToml = @"[server]
listen = ""http://0.0.0.0:7085""
auth_key = """"
default_group = ""default""
strategy = ""weighted""
timeout_seconds = 600
max_failover = 1
max_request_body_bytes = 10485760

[health]
cooldown_seconds = 60

[groups.default]
strategy = ""failover""
max_failover = 1
timeout_seconds = 600

[[platforms]]
name = ""88code-Codex""
base_url = ""https://www.88code.ai/openai/v1""
api_key = ""XX""
group = ""default""
weight = 1
priority = 0
key_type = ""openai""
enabled = true

[[platforms]]
name = ""PackyCode-Codex""
base_url = ""https://www.packyapi.com/v1""
api_key = ""XX""
group = ""default""
weight = 1
priority = 1
key_type = ""openai""
enabled = true

[[platforms]]
name = ""88code-Claude""
base_url = ""https://www.88code.org/api""
api_key = ""XX""
group = ""claude""
weight = 1
priority = 0
key_type = ""claude""
enabled = true

[[platforms]]
name = ""PackyCode-Claude""
base_url = ""https://www.packyapi.com""
api_key = ""XX""
group = ""claude""
weight = 1
priority = 0
key_type = ""claude""
enabled = true

[[platforms]]
name = ""Privnode-Claude""
base_url = ""https://privnode.com/""
api_key = ""XX""
group = ""claude""
weight = 1
priority = 0
key_type = ""claude""
enabled = true

[[platforms]]
name = ""Cubence-Claude""
base_url = ""https://api.cubence.com""
api_key = ""XX""
group = ""claude""
weight = 1
priority = 0
key_type = ""claude""
enabled = true

[[platforms]]
name = ""Local-Claude""
base_url = ""http://localhost:8045""
api_key = ""XX""
group = ""claude""
weight = 1
priority = 0
key_type = ""claude""
enabled = true
";

    private static readonly (string Name, string KeyType)[] ExpectedPlatforms =
    {
        ("88code-Codex", "openai"),
        ("PackyCode-Codex", "openai"),
        ("88code-Claude", "claude"),
        ("PackyCode-Claude", "claude"),
        ("Privnode-Claude", "claude"),
        ("Cubence-Claude", "claude"),
        ("Local-Claude", "claude"),
    };

    [Fact]
    public void Deserialize_Config()
    {
        var model = TomlSerializer.Deserialize(ConfigToml, AotTomlSerializerContext.Default.RootConfig);
        Assert.NotNull(model);

        Assert.NotNull(model.Server);
        Assert.Equal(600, model.Server.TimeoutSeconds);
        Assert.Equal("default", model.Server.DefaultGroup);

        Assert.NotNull(model.Health);
        Assert.Equal(60, model.Health.CooldownSeconds);

        Assert.True(model.Groups.TryGetValue("default", out var group));
        Assert.Equal("failover", group.Strategy);

        var platforms = new List<(string Name, string KeyType)>();
        foreach (var platform in model.Platforms)
        {
            platforms.Add((platform.Name, platform.KeyType));
        }

        Assert.Equal(ExpectedPlatforms, platforms);
    }

    [Fact]
    public void Deserialize_LargeDocumentAsTomlTable()
    {
        var tomlText = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "twitter.toml"));
        var table = TomlSerializer.Deserialize(tomlText, AotTomlSerializerContext.Default.TomlTable);
        Assert.NotNull(table);

        Assert.True(table.TryGetValue("statuses", out var statuses));
        var tableArray = Assert.IsType<TomlTableArray>(statuses);
        Assert.NotEmpty(tableArray);
    }

    // Generated code creates a class with required members through an [UnsafeAccessor], and reads [TomlStringEnumMemberName]
    // with reflection on the enum fields: both must work once trimmed and compiled ahead of time
    [Fact]
    public void RequiredMembersAndEnumMemberNames()
    {
        var value = TomlSerializer.Deserialize("items = ['x']\nname = 'n'\nlevel = 'very-high'\n", AotTomlSerializerContext.Default.RequiredModel);
        Assert.NotNull(value);

        Assert.Equal(["pre", "x"], value.Items);
        Assert.Equal("keep", value.Unread);
        Assert.Equal("n", value.Name);
        Assert.Equal(Level.VeryHigh, value.Level);

        var toml = TomlSerializer.Serialize(value, AotTomlSerializerContext.Default.RequiredModel);
        Assert.Contains("level = \"very-high\"", toml, StringComparison.Ordinal);
    }

    internal sealed class RootConfig
    {
        public ServerConfig? Server { get; set; }

        public HealthConfig? Health { get; set; }

        public Dictionary<string, GroupConfig> Groups { get; set; } = new();

        public List<PlatformConfig> Platforms { get; set; } = new();
    }

    internal sealed class ServerConfig
    {
        public string Listen { get; set; } = string.Empty;

        public string AuthKey { get; set; } = string.Empty;

        public string DefaultGroup { get; set; } = string.Empty;

        public string Strategy { get; set; } = string.Empty;

        public int TimeoutSeconds { get; set; }

        public int MaxFailover { get; set; }

        public int MaxRequestBodyBytes { get; set; }
    }

    internal sealed class HealthConfig
    {
        public int CooldownSeconds { get; set; }
    }

    internal sealed class GroupConfig
    {
        public string Strategy { get; set; } = string.Empty;

        public int MaxFailover { get; set; }

        public int TimeoutSeconds { get; set; }
    }

    internal sealed class PlatformConfig
    {
        public string Name { get; set; } = string.Empty;

        public string BaseUrl { get; set; } = string.Empty;

        public string ApiKey { get; set; } = string.Empty;

        public string Group { get; set; } = string.Empty;

        public int Weight { get; set; }

        public int Priority { get; set; }

        public string KeyType { get; set; } = string.Empty;

        public bool Enabled { get; set; }
    }

    [TomlObjectCreationHandling(TomlObjectCreationHandling.Populate)]
    internal sealed class RequiredModel
    {
        public required IList<string> Items { get; set; } = ["pre"];

        [TomlIgnore(Condition = TomlIgnoreCondition.WhenReading)]
        public required string Unread { get; set; } = "keep";

        public string Name { get; init; } = string.Empty;

        public Level Level { get; set; }
    }

    [TomlConverter(typeof(TomlStringEnumConverter))]
    internal enum Level
    {
        Low,
        [TomlStringEnumMemberName("very-high")]
        VeryHigh,
    }
}

[TomlSourceGenerationOptions(PropertyNamingPolicy = TomlKnownNamingPolicy.SnakeCaseLower)]
[TomlSerializable(typeof(TomlSerializerAotTests.RootConfig))]
[TomlSerializable(typeof(TomlSerializerAotTests.RequiredModel))]
[TomlSerializable(typeof(TomlTable))]
internal sealed partial class AotTomlSerializerContext : TomlSerializerContext
{
}
