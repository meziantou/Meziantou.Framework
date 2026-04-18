using System.Text;
using Meziantou.Framework.HumanReadable;
using Meziantou.Framework.HumanReadable.Converters;
using Meziantou.Framework.HumanReadable.ValueFormatters;

namespace Meziantou.Framework.SnapshotTesting;

/// <summary>Serializes objects to human-readable string representations suitable for snapshot testing.</summary>
public sealed class DefaultSnapshotSerializer : ISnapshotSerializer
{
    internal HumanReadableSerializerOptions Options { get; }

    internal static DefaultSnapshotSerializer DefaultInstance { get; } = new(CreateDefaultOptions());

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

    public DefaultSnapshotSerializer(HumanReadableSerializerOptions? options = null)
    {
        Options = options ?? CreateDefaultOptions();
    }

    public DefaultSnapshotSerializer(Action<HumanReadableSerializerOptions>? configure)
    {
        var options = CreateDefaultOptions();
        configure?.Invoke(options);
        Options = options;
    }

    public IReadOnlyList<SnapshotData> Serialize(SnapshotType type, object? value)
    {
        if (value is byte[] bytes)
        {
            return [new SnapshotData(GetBinaryExtension(type), bytes)];
        }

        if (value is Stream stream)
        {
            return [new SnapshotData(GetBinaryExtension(type), ReadStream(stream))];
        }

        return [new SnapshotData("txt", Encoding.UTF8.GetBytes(HumanReadableSerializer.Serialize(value, Options)))];
    }

    private static string? GetBinaryExtension(SnapshotType type)
    {
        if (string.IsNullOrWhiteSpace(type.Type))
            return null;

        return type.Type;
    }

    private static byte[] ReadStream(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (stream.CanSeek)
        {
            var originalPosition = stream.Position;
            stream.Position = 0;

            try
            {
                using var memoryStream = new MemoryStream();
                stream.CopyTo(memoryStream);
                return memoryStream.ToArray();
            }
            finally
            {
                stream.Position = originalPosition;
            }
        }

        using var streamContent = new MemoryStream();
        stream.CopyTo(streamContent);
        return streamContent.ToArray();
    }
}

