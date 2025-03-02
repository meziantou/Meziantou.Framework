using Meziantou.Framework.HumanReadable;
using Meziantou.Framework.HumanReadable.Converters;
using Meziantou.Framework.HumanReadable.ValueFormatters;

namespace Meziantou.Framework.InlineSnapshotTesting.Serialization;

public sealed class HumanReadableSnapshotSerializer : SnapshotSerializer
{
    internal HumanReadableSerializerOptions? Options { get; }

    internal static HumanReadableSnapshotSerializer DefaultInstance { get; } = new(CreateDefaultOptions());

    private static HumanReadableSerializerOptions CreateDefaultOptions()
    {
        var options = new HumanReadableSerializerOptions()
        {
            DefaultIgnoreCondition = HumanReadableIgnoreCondition.WhenWritingDefault,
            IncludeObsoleteMembers = false,
        };

        options.AddHttpConverters(new HumanReadableHttpOptions());
        options.AddJsonFormatter(new() { WriteIndented = true });
        options.AddXmlFormatter(new() { WriteIndented = true, OrderAttributes = true });
        options.AddHtmlFormatter(new() { AttributeQuote = HtmlAttributeQuote.DoubleQuote, OrderAttributes = true });
        return options;
    }

    public HumanReadableSnapshotSerializer(HumanReadableSerializerOptions? options = null)
    {
        Options = options;
    }

    public HumanReadableSnapshotSerializer(Action<HumanReadableSerializerOptions>? configure)
    {
        var options = CreateDefaultOptions();
        configure?.Invoke(options);
        Options = options;
    }

    public override string Serialize(object? value)
    {
        return HumanReadableSerializer.Serialize(value, Options);
    }
}
