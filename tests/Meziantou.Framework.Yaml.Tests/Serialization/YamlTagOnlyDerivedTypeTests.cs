using Meziantou.Framework.Yaml.Serialization;

namespace Meziantou.Framework.Yaml.Tests.Serialization;
public class YamlTagOnlyDerivedTypeTests
{
    // ---- Model types: tag-only entries (no discriminator) via attributes ----

    [YamlPolymorphic(DiscriminatorStyle = YamlTypeDiscriminatorStyle.Tag)]
    [YamlDerivedType(typeof(AttrCheckin), Tag = "!checkin")]
    [YamlDerivedType(typeof(AttrDns), Tag = "!dns")]
    [YamlDerivedType(typeof(AttrHttp), Tag = "!http")]
    private class AttrMonitor
    {
        public string Interval { get; set; } = string.Empty;
    }

    private sealed class AttrCheckin : AttrMonitor
    {
        public string Endpoint { get; set; } = string.Empty;
    }

    private sealed class AttrDns : AttrMonitor
    {
        public string Host { get; set; } = string.Empty;
    }

    private sealed class AttrHttp : AttrMonitor
    {
        public string Url { get; set; } = string.Empty;
    }

    // ---- Model types: runtime tag-only entries ----

    private class RuntimeMonitor
    {
        public string Interval { get; set; } = string.Empty;
    }

    private sealed class RuntimeCheckin : RuntimeMonitor
    {
        public string Endpoint { get; set; } = string.Empty;
    }

    private sealed class RuntimeDns : RuntimeMonitor
    {
        public string Host { get; set; } = string.Empty;
    }

    private sealed class RuntimeHttp : RuntimeMonitor
    {
        public string Url { get; set; } = string.Empty;
    }

    // ---- Attribute-based: tag-only entries should NOT set default ----

    [Fact]
    public void TagOnlyAttribute_NoTagDeserializesAsBaseType()
    {
        // All derived types have tags but no discriminators.
        // An untagged mapping should deserialize as the base type, not the first entry.
        var yaml = "Interval: 00:00:10\n";
        var value = YamlSerializer.Deserialize<AttrMonitor>(yaml);

        Assert.NotNull(value);
        Assert.IsType<AttrMonitor>(value);
        Assert.IsNotAssignableTo<AttrCheckin>(value, message: "Should not be AttrCheckin — tag-only entries must not become default");
        Assert.IsNotAssignableTo<AttrDns>(value);
        Assert.IsNotAssignableTo<AttrHttp>(value);
        Assert.Equal("00:00:10", value.Interval);
    }

    [Fact]
    public void TagOnlyAttribute_TaggedDeserializesAsDerivedType()
    {
        var yaml = "!dns\nHost: google.com\nInterval: 00:01:00\n";
        var value = YamlSerializer.Deserialize<AttrMonitor>(yaml);

        Assert.NotNull(value);
        Assert.IsType<AttrDns>(value);
        Assert.Equal("google.com", ((AttrDns)value).Host);
    }

    [Fact]
    public void TagOnlyAttribute_AllTagsWork()
    {
        var checkinYaml = "!checkin\nEndpoint: /health\nInterval: 00:00:30\n";
        var dnsYaml = "!dns\nHost: dns.google\nInterval: 00:01:00\n";
        var httpYaml = "!http\nUrl: https://example.com\nInterval: 00:05:00\n";

        var checkin = YamlSerializer.Deserialize<AttrMonitor>(checkinYaml);
        var dns = YamlSerializer.Deserialize<AttrMonitor>(dnsYaml);
        var http = YamlSerializer.Deserialize<AttrMonitor>(httpYaml);

        var attrCheckin = Assert.IsType<AttrCheckin>(checkin);
        var attrDns = Assert.IsType<AttrDns>(dns);
        var attrHttp = Assert.IsType<AttrHttp>(http);
        Assert.Equal("/health", attrCheckin.Endpoint);
        Assert.Equal("dns.google", attrDns.Host);
        Assert.Equal("https://example.com", attrHttp.Url);
    }

    [Fact]
    public void TagOnlyAttribute_DictionaryWithMixedTagsAndUntagged()
    {
        var yaml = """
            google: !http
              Url: https://google.com
              Interval: 00:05:00
            router: !dns
              Host: 192.168.1.1
              Interval: 00:01:00
            base:
              Interval: 00:00:10
            """;

        var dict = YamlSerializer.Deserialize<Dictionary<string, AttrMonitor>>(yaml);

        Assert.NotNull(dict);
        Assert.HasCount(3, dict);
        Assert.IsType<AttrHttp>(dict["google"]);
        Assert.IsType<AttrDns>(dict["router"]);
        Assert.IsType<AttrMonitor>(dict["base"]);
        Assert.IsNotAssignableTo<AttrCheckin>(dict["base"], message: "Untagged entries must not resolve to first tag-only entry");
    }

    // ---- Runtime: tag-only entries should NOT set default ----

    [Fact]
    public void TagOnlyRuntime_NoTagDeserializesAsBaseType()
    {
        var options = new YamlSerializerOptions
        {
            PolymorphismOptions = new YamlPolymorphismOptions
            {
                DiscriminatorStyle = YamlTypeDiscriminatorStyle.Tag,
                DerivedTypeMappings =
                {
                    [typeof(RuntimeMonitor)] = new List<YamlDerivedType>
                    {
                        new YamlDerivedType(typeof(RuntimeCheckin)) { Tag = "!checkin" },
                        new YamlDerivedType(typeof(RuntimeDns)) { Tag = "!dns" },
                        new YamlDerivedType(typeof(RuntimeHttp)) { Tag = "!http" },
                    },
                },
            },
        };

        var yaml = "Interval: 00:00:10\n";
        var value = YamlSerializer.Deserialize<RuntimeMonitor>(yaml, options);

        Assert.NotNull(value);
        Assert.IsType<RuntimeMonitor>(value);
        Assert.IsNotAssignableTo<RuntimeCheckin>(value, message: "Should not be RuntimeCheckin — tag-only entries must not become default");
        Assert.IsNotAssignableTo<RuntimeDns>(value);
        Assert.IsNotAssignableTo<RuntimeHttp>(value);
        Assert.Equal("00:00:10", value.Interval);
    }

    [Fact]
    public void TagOnlyRuntime_TaggedDeserializesAsDerivedType()
    {
        var options = new YamlSerializerOptions
        {
            PolymorphismOptions = new YamlPolymorphismOptions
            {
                DiscriminatorStyle = YamlTypeDiscriminatorStyle.Tag,
                DerivedTypeMappings =
                {
                    [typeof(RuntimeMonitor)] = new List<YamlDerivedType>
                    {
                        new YamlDerivedType(typeof(RuntimeCheckin)) { Tag = "!checkin" },
                        new YamlDerivedType(typeof(RuntimeHttp)) { Tag = "!http" },
                    },
                },
            },
        };

        var yaml = "!http\nUrl: https://example.com\nInterval: 00:05:00\n";
        var value = YamlSerializer.Deserialize<RuntimeMonitor>(yaml, options);

        Assert.NotNull(value);
        Assert.IsType<RuntimeHttp>(value);
        Assert.Equal("https://example.com", ((RuntimeHttp)value).Url);
    }

    [Fact]
    public void TagOnlyRuntime_DictionaryWithMixedTagsAndUntagged()
    {
        var options = new YamlSerializerOptions
        {
            PolymorphismOptions = new YamlPolymorphismOptions
            {
                DiscriminatorStyle = YamlTypeDiscriminatorStyle.Tag,
                DerivedTypeMappings =
                {
                    [typeof(RuntimeMonitor)] = new List<YamlDerivedType>
                    {
                        new YamlDerivedType(typeof(RuntimeCheckin)) { Tag = "!checkin" },
                        new YamlDerivedType(typeof(RuntimeDns)) { Tag = "!dns" },
                    },
                },
            },
        };

        var yaml = """
            health: !checkin
              Endpoint: /ping
              Interval: 00:00:30
            plain:
              Interval: 00:00:10
            """;

        var dict = YamlSerializer.Deserialize<Dictionary<string, RuntimeMonitor>>(yaml, options);

        Assert.NotNull(dict);
        Assert.HasCount(2, dict);
        Assert.IsType<RuntimeCheckin>(dict["health"]);
        Assert.IsType<RuntimeMonitor>(dict["plain"]);
        Assert.IsNotAssignableTo<RuntimeCheckin>(dict["plain"]);
    }

    // ---- Explicit default still works when a separate no-tag no-discriminator entry exists ----

    [Fact]
    public void ExplicitDefaultDerivedTypeWithTagOnlyEntries()
    {
        var options = new YamlSerializerOptions
        {
            PolymorphismOptions = new YamlPolymorphismOptions
            {
                DiscriminatorStyle = YamlTypeDiscriminatorStyle.Tag,
                DerivedTypeMappings =
                {
                    [typeof(RuntimeMonitor)] = new List<YamlDerivedType>
                    {
                        new YamlDerivedType(typeof(RuntimeCheckin)) { Tag = "!checkin" },
                        new YamlDerivedType(typeof(RuntimeDns)) { Tag = "!dns" },
                        new YamlDerivedType(typeof(RuntimeHttp)),  // no tag, no discriminator → explicit default
                    },
                },
            },
        };

        // Untagged entries should resolve to RuntimeHttp (the explicit default)
        var yaml = "Url: https://fallback.com\nInterval: 00:01:00\n";
        var value = YamlSerializer.Deserialize<RuntimeMonitor>(yaml, options);

        Assert.NotNull(value);
        Assert.IsType<RuntimeHttp>(value);
        Assert.Equal("https://fallback.com", ((RuntimeHttp)value).Url);
    }

    // ---- Serialization roundtrip for tag-only entries ----

    [Fact]
    public void TagOnlyAttribute_RoundtripSerialization()
    {
        AttrMonitor monitor = new AttrDns { Host = "google.com", Interval = "00:01:00" };
        var yaml = YamlSerializer.Serialize(monitor, typeof(AttrMonitor));

        Assert.Contains("!dns", yaml);
        Assert.Contains("Host: google.com", yaml);

        var deserialized = YamlSerializer.Deserialize<AttrMonitor>(yaml);
        var attrDns = Assert.IsType<AttrDns>(deserialized);
        Assert.Equal("google.com", attrDns.Host);
    }

    [Fact]
    public void TagOnlyRuntime_RoundtripSerialization()
    {
        var options = new YamlSerializerOptions
        {
            PolymorphismOptions = new YamlPolymorphismOptions
            {
                DiscriminatorStyle = YamlTypeDiscriminatorStyle.Tag,
                DerivedTypeMappings =
                {
                    [typeof(RuntimeMonitor)] = new List<YamlDerivedType>
                    {
                        new YamlDerivedType(typeof(RuntimeCheckin)) { Tag = "!checkin" },
                        new YamlDerivedType(typeof(RuntimeDns)) { Tag = "!dns" },
                    },
                },
            },
        };

        RuntimeMonitor monitor = new RuntimeDns { Host = "dns.google", Interval = "00:01:00" };
        var yaml = YamlSerializer.Serialize(monitor, typeof(RuntimeMonitor), options);

        Assert.Contains("!dns", yaml);
        Assert.Contains("Host: dns.google", yaml);

        var deserialized = YamlSerializer.Deserialize<RuntimeMonitor>(yaml, options);
        var runtimeDns = Assert.IsType<RuntimeDns>(deserialized);
        Assert.Equal("dns.google", runtimeDns.Host);
    }
}
