using Meziantou.Framework.HumanReadable;
using Meziantou.Framework.HumanReadable.Converters;

namespace Meziantou.Framework.InlineSnapshotTesting.Serialization;

public sealed class HumanReadableSnapshotSerializer : SnapshotSerializer
{
    private readonly HumanReadableSerializerOptions? _options;

    internal static HumanReadableSnapshotSerializer Instance { get; } = new(CreateDefaultOptions());

    private static HumanReadableSerializerOptions CreateDefaultOptions()
    {
        var options = new HumanReadableSerializerOptions()
        {
            DefaultIgnoreCondition = HumanReadableIgnoreCondition.WhenWritingDefault,
            PropertyOrder = StringComparer.Ordinal,
        };

        options.AddHttpConverters(new HumanReadableHttpOptions());
        return options;
    }

    public HumanReadableSnapshotSerializer(HumanReadableSerializerOptions? options = null)
    {
        _options = options;
    }

    public HumanReadableSnapshotSerializer(Action<HumanReadableSerializerOptions>? configure)
    {
        var options = CreateDefaultOptions();
        configure?.Invoke(options);
        _options = options;
    }

    public override string Serialize(object? value)
    {
        return HumanReadableSerializer.Serialize(value, _options);
    }
}
