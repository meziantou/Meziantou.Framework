using System.Text.Json.Nodes;
using Meziantou.Framework.HumanReadable.ValueFormatters;
using Meziantou.Framework.InlineSnapshotTesting.Serialization;

namespace Meziantou.Framework.InlineSnapshotTesting.Samples;

public sealed class Json_reorder_properties
{
    [Fact]
    public void Demo()
    {
        var settings = InlineSnapshotSettings.Default with { };
        settings.SnapshotSerializer = new HumanReadableSnapshotSerializer(options =>
        {
            options.AddJsonFormatter(new JsonFormatterOptions
            {
                WriteIndented = true,
                OrderProperties = true,
            });
        });

        var value = JsonNode.Parse("""{"foo":"bar","answer":42}""");

        // Json content is automatically indented
        InlineSnapshot.WithSettings(settings).Validate(value,
            """
            {
              "answer": 42,
              "foo": "bar"
            }
            """);
    }
}