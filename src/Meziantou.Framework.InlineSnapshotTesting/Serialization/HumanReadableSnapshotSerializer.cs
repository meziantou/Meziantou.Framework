using Meziantou.Framework.HumanReadable;
using Meziantou.Framework.HumanReadable.Converters;
using Meziantou.Framework.HumanReadable.ValueFormatters;

namespace Meziantou.Framework.InlineSnapshotTesting.Serialization;

/// <summary>
/// A snapshot serializer that uses the HumanReadableSerializer to create readable string representations of objects.
/// </summary>
public sealed class HumanReadableSnapshotSerializer : SnapshotSerializer
{
    internal HumanReadableSerializerOptions? Options { get; }

    /// <summary>
    /// Gets the default instance of the HumanReadableSnapshotSerializer with preconfigured options.
    /// </summary>
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

    /// <summary>
    /// Initializes a new instance of the <see cref="HumanReadableSnapshotSerializer"/> class with the specified options.
    /// </summary>
    /// <param name="options">The serializer options to use, or <see langword="null"/> to use the default options.</param>
    public HumanReadableSnapshotSerializer(HumanReadableSerializerOptions? options = null)
    {
        Options = options;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HumanReadableSnapshotSerializer"/> class with a configuration callback.
    /// </summary>
    /// <param name="configure">An optional action to configure the default serializer options.</param>
    public HumanReadableSnapshotSerializer(Action<HumanReadableSerializerOptions>? configure)
    {
        var options = CreateDefaultOptions();
        configure?.Invoke(options);
        Options = options;
    }

    /// <inheritdoc/>
    public override string Serialize(object? value)
    {
        return HumanReadableSerializer.Serialize(value, Options);
    }
}
