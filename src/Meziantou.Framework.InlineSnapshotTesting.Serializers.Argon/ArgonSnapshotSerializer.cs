using System.Text;
using Argon;

namespace Meziantou.Framework.InlineSnapshotTesting.Serialization;

public sealed class ArgonSnapshotSerializer : SnapshotSerializer
{
    private static readonly JsonSerializer Serializer = new();

    public override string Serialize(object? value)
    {
        var result = new StringBuilder();
        var textWriter = new StringWriter(result)
        {
            NewLine = "\n",
        };

        using var writer = new JsonTextWriter(textWriter)
        {
            QuoteName = false,
            QuoteValue = false,
            EscapeHandling = EscapeHandling.None,
            Formatting = Formatting.Indented,
        };

        Serializer.Serialize(writer, value);

        return result.ToString();
    }
}
