using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Serialization;

namespace Tomlyn.AotTests;

#pragma warning disable MA0048 // File name must match type name

internal static class Program
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

    private static int Main()
    {
        RootConfig? model;
        try
        {
            model = TomlSerializer.Deserialize(ConfigToml, AotTomlSerializerContext.Default.RootConfig);
        }
        catch (TomlException ex)
        {
            PrintException(ex);
            return 1;
        }

        if (model is null)
        {
            Console.Error.WriteLine("TOML deserialize returned null.");
            return 1;
        }

        var errors = ValidateModel(model);
        errors.AddRange(ValidateTwitterToml());
        if (errors.Count > 0)
        {
            foreach (var error in errors)
            {
                Console.Error.WriteLine(error);
            }
            return 2;
        }

        Console.WriteLine("AOT parse OK.");
        return 0;
    }

    private static void PrintException(TomlException exception)
    {
        Console.Error.WriteLine("TOML parse failed.");
        Console.Error.WriteLine(exception.Message);
        if (exception.Line != 0 && exception.Column != 0)
        {
            Console.Error.WriteLine($"Location: line {exception.Line}, column {exception.Column}");
        }
    }

    private static List<string> ValidateModel(RootConfig model)
    {
        var errors = new List<string>();

        if (model.Server is null)
        {
            errors.Add("server section missing.");
        }
        else
        {
            if (model.Server.TimeoutSeconds != 600)
            {
                errors.Add("server.timeout_seconds mismatch.");
            }

            if (!string.Equals(model.Server.DefaultGroup, "default", StringComparison.Ordinal))
            {
                errors.Add("server.default_group mismatch.");
            }
        }

        if (model.Health is null || model.Health.CooldownSeconds != 60)
        {
            errors.Add("health.cooldown_seconds mismatch.");
        }

        if (model.Groups is null || !model.Groups.TryGetValue("default", out var group))
        {
            errors.Add("groups.default missing.");
        }
        else if (!string.Equals(group.Strategy, "failover", StringComparison.Ordinal))
        {
            errors.Add("groups.default.strategy mismatch.");
        }

        if (model.Platforms is null)
        {
            errors.Add("platforms missing.");
            return errors;
        }

        foreach (var expected in ExpectedPlatforms)
        {
            var platform = FindPlatform(model.Platforms, expected.Name);
            if (platform is null)
            {
                errors.Add($"platforms entry '{expected.Name}' missing.");
                continue;
            }

            if (!string.Equals(platform.KeyType, expected.KeyType, StringComparison.Ordinal))
            {
                errors.Add($"platforms entry '{expected.Name}' key_type mismatch.");
            }
        }

        return errors;
    }

    private static List<string> ValidateTwitterToml()
    {
        var errors = new List<string>();
        var twitterPath = Path.Combine(AppContext.BaseDirectory, "twitter.toml");
        if (!File.Exists(twitterPath))
        {
            errors.Add($"twitter.toml missing at {twitterPath}");
            return errors;
        }

        try
        {
            var tomlText = File.ReadAllText(twitterPath);
            var table = TomlSerializer.Deserialize(tomlText, AotTomlSerializerContext.Default.TomlTable);
            if (table is null)
            {
                errors.Add("twitter.toml parse returned null.");
                return errors;
            }
            if (!table.TryGetValue("statuses", out var statuses) || statuses is not TomlTableArray tableArray || tableArray.Count == 0)
            {
                errors.Add("twitter.toml parse missing statuses table array.");
            }
        }
        catch (Exception ex)
        {
            errors.Add($"twitter.toml parse failed: {ex.Message}");
        }

        return errors;
    }

    private static PlatformConfig? FindPlatform(List<PlatformConfig> platforms, string name)
    {
        foreach (var platform in platforms)
        {
            if (string.Equals(platform.Name, name, StringComparison.Ordinal))
            {
                return platform;
            }
        }

        return null;
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
}

[TomlSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[TomlSerializable(typeof(Program.RootConfig))]
[TomlSerializable(typeof(TomlTable))]
internal sealed partial class AotTomlSerializerContext : TomlSerializerContext
{
}
