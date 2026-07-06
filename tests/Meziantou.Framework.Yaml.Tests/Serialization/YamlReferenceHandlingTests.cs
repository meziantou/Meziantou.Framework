namespace Meziantou.Framework.Yaml.Tests.Serialization;
public class YamlReferenceHandlingTests
{
    private sealed class Node
    {
        public string Name { get; set; } = string.Empty;

        public Node? Next { get; set; }
    }

    private sealed class Container
    {
        public Node? A { get; set; }

        public Node? B { get; set; }
    }

    [Fact]
    public void SerializePreservesSelfReferenceForObjects()
    {
        var node = new Node { Name = "root" };
        node.Next = node;

        var yaml = YamlSerializer.Serialize(
            node,
            new YamlSerializerOptions { ReferenceHandling = YamlReferenceHandling.Preserve });

        Assert.Contains("&id001", yaml);
        Assert.Contains("Next: *id001", yaml);
    }

    [Fact]
    public void DeserializePreservesSelfReferenceForObjects()
    {
        var yaml = "&id001\nName: root\nNext: *id001\n";

        var node = YamlSerializer.Deserialize<Node>(
            yaml,
            new YamlSerializerOptions { ReferenceHandling = YamlReferenceHandling.Preserve });

        Assert.NotNull(node);
        Assert.True(ReferenceEquals(node, node.Next));
        Assert.Equal("root", node.Name);
    }

    [Fact]
    public void SerializePreservesSharedReferences()
    {
        var node = new Node { Name = "shared" };
        var container = new Container { A = node, B = node };

        var yaml = YamlSerializer.Serialize(
            container,
            new YamlSerializerOptions { ReferenceHandling = YamlReferenceHandling.Preserve });

        var anchorStart = yaml.IndexOf("A: &", StringComparison.Ordinal);
        Assert.True(anchorStart >= 0, "Expected 'A' to be anchored.");
        anchorStart += "A: &".Length;

        var anchorEnd = yaml.IndexOf('\n', anchorStart, StringComparison.Ordinal);
        Assert.True(anchorEnd > anchorStart, "Expected an anchor name after 'A: &'.");

        var anchor = yaml.Substring(anchorStart, anchorEnd - anchorStart).Trim();
        Assert.True(anchor.Length > 0);

        Assert.Contains($"B: *{anchor}", yaml);
    }

    [Fact]
    public void DeserializePreservesSharedReferences()
    {
        var yaml = "A: &id001\n  Name: shared\nB: *id001\n";

        var container = YamlSerializer.Deserialize<Container>(
            yaml,
            new YamlSerializerOptions { ReferenceHandling = YamlReferenceHandling.Preserve });

        Assert.NotNull(container);
        Assert.NotNull(container.A);
        Assert.NotNull(container.B);
        Assert.True(ReferenceEquals(container.A, container.B));
        Assert.Equal("shared", container.A.Name);
    }

    [Fact]
    public void DeserializeAndSerializePreservesSelfReferenceForListsOfObject()
    {
        var yaml = "&id001\n- *id001\n";
        var options = new YamlSerializerOptions { ReferenceHandling = YamlReferenceHandling.Preserve };

        var list = YamlSerializer.Deserialize<List<object?>>(yaml, options);

        Assert.NotNull(list);
        Assert.Single(list);
        Assert.True(ReferenceEquals(list, list[0]));

        var roundTrip = YamlSerializer.Serialize(list, options);
        Assert.Contains("&id001", roundTrip);
        Assert.Contains("*id001", roundTrip);
    }

    [Fact]
    public void DeserializeAndSerializePreservesSelfReferenceForDictionariesOfObject()
    {
        var yaml = "&id001\nself: *id001\n";
        var options = new YamlSerializerOptions { ReferenceHandling = YamlReferenceHandling.Preserve };

        var dict = YamlSerializer.Deserialize<Dictionary<string, object?>>(yaml, options);

        Assert.NotNull(dict);
        Assert.True(dict.TryGetValue("self", out var self));
        Assert.True(ReferenceEquals(dict, self));

        var roundTrip = YamlSerializer.Serialize(dict, options);
        Assert.Contains("&id001", roundTrip);
        Assert.Contains("self: *id001", roundTrip);
    }
}
