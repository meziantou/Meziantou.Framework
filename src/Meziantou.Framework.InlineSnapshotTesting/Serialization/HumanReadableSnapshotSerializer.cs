using System.Net;
using Meziantou.Framework.HumanReadable;

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

        CustomizeHttpRequestMessage(options);
        CustomizeHttpResponseMessage(options);
        return options;

        static void CustomizeHttpRequestMessage(HumanReadableSerializerOptions options)
        {
            options.AddAttribute(typeof(HttpRequestMessage), nameof(HttpRequestMessage.Method), new HumanReadablePropertyOrderAttribute(0));
            options.AddAttribute(typeof(HttpRequestMessage), nameof(HttpRequestMessage.RequestUri), new HumanReadablePropertyOrderAttribute(1));
            options.AddAttribute(typeof(HttpRequestMessage), nameof(HttpRequestMessage.Version), new HumanReadablePropertyOrderAttribute(2));
#if NET5_0_OR_GREATER
            options.AddAttribute(typeof(HttpRequestMessage), nameof(HttpRequestMessage.VersionPolicy), new HumanReadablePropertyOrderAttribute(3));
#endif
            options.AddAttribute(typeof(HttpRequestMessage), nameof(HttpRequestMessage.Headers), new HumanReadablePropertyOrderAttribute(4));
            options.AddAttribute(typeof(HttpRequestMessage), nameof(HttpRequestMessage.Content), new HumanReadablePropertyOrderAttribute(5));

            options.AddAttribute(typeof(HttpRequestMessage), nameof(HttpRequestMessage.Version), new HumanReadableDefaultValueAttribute(HttpVersion.Version11));
            options.AddAttribute(typeof(HttpRequestMessage), nameof(HttpRequestMessage.Headers), new HumanReadableIgnoreAttribute() { Condition = HumanReadableIgnoreCondition.WhenWritingEmptyCollection });

#pragma warning disable CS0618 // Type or member is obsolete
            options.AddAttribute(typeof(HttpRequestMessage), nameof(HttpRequestMessage.Properties), new HumanReadableIgnoreAttribute() { Condition = HumanReadableIgnoreCondition.WhenWritingEmptyCollection });
#pragma warning restore CS0618

#if NET5_0_OR_GREATER
            options.AddAttribute(typeof(HttpRequestMessage), nameof(HttpRequestMessage.Options), new HumanReadableIgnoreAttribute() { Condition = HumanReadableIgnoreCondition.WhenWritingEmptyCollection });
#endif
        }

        static void CustomizeHttpResponseMessage(HumanReadableSerializerOptions options)
        {
            options.AddAttribute(typeof(HttpResponseMessage), nameof(HttpResponseMessage.RequestMessage), new HumanReadableIgnoreAttribute());
            options.AddAttribute(typeof(HttpResponseMessage), nameof(HttpResponseMessage.IsSuccessStatusCode), new HumanReadableIgnoreAttribute());
            options.AddAttribute(typeof(HttpResponseMessage), nameof(HttpResponseMessage.ReasonPhrase), new HumanReadableIgnoreAttribute());
            options.AddAttribute(typeof(HttpResponseMessage), nameof(HttpResponseMessage.Headers), new HumanReadableIgnoreAttribute() { Condition = HumanReadableIgnoreCondition.WhenWritingEmptyCollection });
#if NETCOREAPP3_1_OR_GREATER
            options.AddAttribute(typeof(HttpResponseMessage), nameof(HttpResponseMessage.TrailingHeaders), new HumanReadableIgnoreAttribute() { Condition = HumanReadableIgnoreCondition.WhenWritingEmptyCollection });
#endif
            options.AddAttribute(typeof(HttpResponseMessage), nameof(HttpResponseMessage.Version), new HumanReadableDefaultValueAttribute(HttpVersion.Version11));

            options.AddAttribute(typeof(HttpResponseMessage), nameof(HttpResponseMessage.StatusCode), new HumanReadablePropertyOrderAttribute(0));
            options.AddAttribute(typeof(HttpResponseMessage), nameof(HttpResponseMessage.Version), new HumanReadablePropertyOrderAttribute(1));
            options.AddAttribute(typeof(HttpResponseMessage), nameof(HttpResponseMessage.Headers), new HumanReadablePropertyOrderAttribute(2));
#if NETCOREAPP3_1_OR_GREATER
            options.AddAttribute(typeof(HttpResponseMessage), nameof(HttpResponseMessage.TrailingHeaders), new HumanReadablePropertyOrderAttribute(3));
#endif
            options.AddAttribute(typeof(HttpResponseMessage), nameof(HttpResponseMessage.Content), new HumanReadablePropertyOrderAttribute(4));
        }
    }

    public HumanReadableSnapshotSerializer(HumanReadableSerializerOptions? options = null)
    {
        _options = options;
    }

    public override string Serialize(object? value)
    {
        return HumanReadableSerializer.Serialize(value, _options);
    }
}
