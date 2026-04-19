using Meziantou.Framework.HumanReadable;
using Meziantou.Framework.HumanReadable.Converters;
using Meziantou.Framework.HumanReadable.ValueFormatters;

namespace Meziantou.Framework.SnapshotTesting;

/// <summary>Serializes objects to human-readable string representations suitable for snapshot testing.</summary>
internal sealed class HumanReadableSnapshotSerializer : ISnapshotSerializer
{
    internal HumanReadableSerializerOptions Options { get; }

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
        Options = options ?? CreateDefaultOptions();
    }

    public HumanReadableSnapshotSerializer(Action<HumanReadableSerializerOptions>? configure)
    {
        var options = CreateDefaultOptions();
        configure?.Invoke(options);
        Options = options;
    }

    public SerializedSnapshot Serialize(SnapshotType type, object? value)
    {
        return new SerializedSnapshot([new SnapshotData(type.FileExtension, Encoding.UTF8.GetBytes(HumanReadableSerializer.Serialize(value, Options)))]);
    }

    public bool CanSerialize(SnapshotType type, object? value) => true;
}

