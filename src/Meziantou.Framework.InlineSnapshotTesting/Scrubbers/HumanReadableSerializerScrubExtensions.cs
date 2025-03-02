using System.Globalization;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;
using Json.Path;
using Meziantou.Framework.HumanReadable;
using Meziantou.Framework.HumanReadable.ValueFormatters;

namespace Meziantou.Framework.InlineSnapshotTesting;

public static class HumanReadableSerializerScrubExtensions
{
    public static void ScrubGuid(this HumanReadableSerializerOptions options) => ScrubValue<Guid>(options, (value, index) =>
    {
        if (value == Guid.Empty)
            return "00000000-0000-0000-0000-000000000000";

        index += 1; // Distinct from Guid.Empty

        const string Prefix = "00000000-0000-0000-0000-";
#if NET6_0_OR_GREATER
        Span<char> data = stackalloc char[36];
        Prefix.AsSpan().CopyTo(data);
        _ = index.TryFormat(data[Prefix.Length..], out _, "000000000000", CultureInfo.InvariantCulture);
        return data.ToString();
#else
        return Prefix + index.ToString("000000000000", CultureInfo.InvariantCulture);
#endif
    });

    public static void ScrubValue<T>(this HumanReadableSerializerOptions options) => ScrubValue<T>(options, comparer: null);
    public static void ScrubValue<T>(this HumanReadableSerializerOptions options, IEqualityComparer<T>? comparer) => ScrubValue<T>(options, (value, index) => typeof(T).Name + "_" + index.ToString(CultureInfo.InvariantCulture), comparer);
    public static void ScrubValue<T>(this HumanReadableSerializerOptions options, Func<T, string> scrubber) => options.Converters.Add(new ValueScrubberConverter<T>(scrubber));
    public static void ScrubValue<T>(this HumanReadableSerializerOptions options, Func<T, int, string> scrubber) => ScrubValue(options, scrubber, comparer: null);
    public static void ScrubValue<T>(this HumanReadableSerializerOptions options, Func<T, int, string> scrubber, IEqualityComparer<T>? comparer)
        => options.Converters.Add(new ScrubValueIncrementalConverter<T>((value, index) => scrubber(value, index), comparer ?? EqualityComparer<T>.Default));

    public static void ScrubXmlAttribute(this HumanReadableSerializerOptions options, string xpath, Func<XAttribute, string?> scrubber)
    {
        ScrubXmlAttribute(options, xpath, nsResolver: null, scrubber);
    }

    public static void ScrubXmlAttribute(this HumanReadableSerializerOptions options, string xpath, IXmlNamespaceResolver? nsResolver, Func<XAttribute, string?> scrubber)
    {
        var existingFormatter = options.GetFormatter(ValueFormatter.XmlMediaTypeName);
        options.AddFormatter(ValueFormatter.XmlMediaTypeName, new ScrubXmlAttributeFormatter(xpath, nsResolver, scrubber, existingFormatter));
    }

    public static void ScrubXmlNode(this HumanReadableSerializerOptions options, string xpath, Func<XNode, XNode?> scrubber)
    {
        ScrubXmlNode(options, xpath, nsResolver: null, scrubber);
    }

    public static void ScrubXmlNode(this HumanReadableSerializerOptions options, string xpath, Action<XNode> scrubber)
    {
        ScrubXmlNode(options, xpath, nsResolver: null, node =>
        {
            scrubber(node);
            return node;
        });
    }

    public static void ScrubXmlNode(this HumanReadableSerializerOptions options, string xpath, IXmlNamespaceResolver? nsResolver, Func<XNode, XNode?> scrubber)
    {
        var existingFormatter = options.GetFormatter(ValueFormatter.XmlMediaTypeName);
        options.AddFormatter(ValueFormatter.XmlMediaTypeName, new ScrubXmlNodeFormatter(xpath, nsResolver, scrubber, existingFormatter));
    }

    public static void ScrubJsonValue(this HumanReadableSerializerOptions options, string jsonPath, Func<JsonNode, string?> scrubber)
    {
        var existingFormatter = options.GetFormatter(ValueFormatter.JsonMediaTypeName);
        options.AddFormatter(ValueFormatter.JsonMediaTypeName, new ScrubJsonFormatter(jsonPath, node => scrubber(node) is { } result ? JsonValue.Create(result) : null, existingFormatter));
    }

    public static void ScrubJsonValue(this HumanReadableSerializerOptions options, string jsonPath, Func<JsonNode, JsonNode?> scrubber)
    {
        var existingFormatter = options.GetFormatter(ValueFormatter.JsonMediaTypeName);
        options.AddFormatter(ValueFormatter.JsonMediaTypeName, new ScrubJsonFormatter(jsonPath, scrubber, existingFormatter));
    }

    public static void UseRelativeTimeSpan(this HumanReadableSerializerOptions options, TimeSpan origin) => options.Converters.Add(new RelativeTimeSpanConverter(origin));
    public static void UseRelativeDateTime(this HumanReadableSerializerOptions options, DateTime origin) => options.Converters.Add(new RelativeDateTimeConverter(origin));
    public static void UseRelativeDateTimeOffset(this HumanReadableSerializerOptions options, DateTimeOffset origin) => options.Converters.Add(new RelativeDateTimeOffsetConverter(origin));

    private sealed class ValueScrubberConverter<T>(Func<T, string> scrubber) : HumanReadableConverter<T>
    {
        protected override void WriteValue(HumanReadableTextWriter writer, T value, HumanReadableSerializerOptions options)
        {
            writer.WriteValue(scrubber(value));
        }
    }

    private sealed class RelativeTimeSpanConverter(TimeSpan origin) : HumanReadableConverter<TimeSpan>
    {
        protected override void WriteValue(HumanReadableTextWriter writer, TimeSpan value, HumanReadableSerializerOptions options)
        {
            WriteValueCore(writer, value - origin);
        }

        internal static void WriteValueCore(HumanReadableTextWriter writer, TimeSpan value)
        {
            writer.WriteValue(value.ToString(format: null, CultureInfo.InvariantCulture));
        }
    }

    private sealed class RelativeDateTimeConverter(DateTime origin) : HumanReadableConverter<DateTime>
    {
        protected override void WriteValue(HumanReadableTextWriter writer, DateTime value, HumanReadableSerializerOptions options)
        {
            var diff = value - origin;
            RelativeTimeSpanConverter.WriteValueCore(writer, diff);
        }
    }

    private sealed class RelativeDateTimeOffsetConverter(DateTimeOffset origin) : HumanReadableConverter<DateTimeOffset>
    {
        protected override void WriteValue(HumanReadableTextWriter writer, DateTimeOffset value, HumanReadableSerializerOptions options)
        {
            var diff = value - origin;
            RelativeTimeSpanConverter.WriteValueCore(writer, diff);
        }
    }

    private sealed class ScrubValueIncrementalConverter<T>(Func<T, int, string> formatValue, IEqualityComparer<T> comparer) : HumanReadableConverter<T>
    {
        private readonly string _uniqueName = Guid.NewGuid().ToString();

        protected override void WriteValue(HumanReadableTextWriter writer, T value, HumanReadableSerializerOptions options)
        {
            var dict = options.GetOrSetSerializationData(_uniqueName, () => new Dictionary<T, string>(comparer));
            if (!dict.TryGetValue(value, out var scrubbedValue))
            {
                scrubbedValue = formatValue(value, dict.Count);
                dict.Add(value, scrubbedValue);
            }

            writer.WriteValue(scrubbedValue);
        }
    }

    private sealed class ScrubJsonFormatter : ValueFormatter
    {
        private static readonly JsonFormatter DefaultJsonFormatter = new();

        private readonly JsonPath _path;
        private readonly Func<JsonNode, JsonNode?> _scrubber;
        private readonly ValueFormatter _innerFormatter;

        public ScrubJsonFormatter(string jsonPath, Func<JsonNode, JsonNode?> scrubber, ValueFormatter? innerFormatter)
        {
            _path = JsonPath.Parse(jsonPath);
            _scrubber = scrubber ?? throw new ArgumentNullException(nameof(scrubber));
            _innerFormatter = innerFormatter ?? DefaultJsonFormatter;
        }

        public override void Format(HumanReadableTextWriter writer, string? value, HumanReadableSerializerOptions options)
        {
            var node = JsonNode.Parse(value);
            var result = _path.Evaluate(node);
            foreach (var match in result.Matches)
            {
                var scrubValue = _scrubber(match.Value);
                ReplaceWith(match.Value, scrubValue);
            }

            var json = node.ToJsonString();
            _innerFormatter.Format(writer, json, options);
        }

        private static void ReplaceWith(JsonNode oldNode, JsonNode? newNode)
        {
            switch (oldNode.Parent)
            {
                case JsonObject jsonObject:
                    if (newNode is null)
                    {
                        var propertyName = oldNode.GetPropertyName();
                        jsonObject.Remove(propertyName);
                    }
                    else
                    {
                        var propertyName = oldNode.GetPropertyName();
                        jsonObject[propertyName] = newNode;
                    }

                    break;

                case JsonArray jsonArray:
                    if (newNode is null)
                    {
                        var index = oldNode.GetElementIndex();
                        jsonArray.RemoveAt(index);
                    }
                    else
                    {
                        var index = oldNode.GetElementIndex();
                        jsonArray[index] = newNode;
                    }

                    break;
            }
        }
    }

    private sealed class ScrubXmlAttributeFormatter : ValueFormatter
    {
        private static readonly XmlFormatter DefaultXmlFormatter = new();

        private readonly string _xpath;
        private readonly IXmlNamespaceResolver? _nsResolver;
        private readonly Func<XAttribute, string?> _scrubber;
        private readonly ValueFormatter _innerFormatter;

        public ScrubXmlAttributeFormatter(string xpath, IXmlNamespaceResolver? nsResolver, Func<XAttribute, string?> scrubber, ValueFormatter? innerFormatter)
        {
            _xpath = xpath;
            _nsResolver = nsResolver;
            _scrubber = scrubber;
            _innerFormatter = innerFormatter ?? DefaultXmlFormatter;
        }

        public override void Format(HumanReadableTextWriter writer, string? value, HumanReadableSerializerOptions options)
        {
            var document = XDocument.Parse(value);
            var result = document.XPathEvaluate(_xpath, _nsResolver);
            var navigator = (IEnumerable<object>)result;
            foreach (var item in navigator)
            {
                if (item is XAttribute attribute)
                {
                    var newValue = _scrubber(attribute);
                    if (newValue is null)
                    {
                        attribute.Remove();
                    }
                    else
                    {
                        attribute.SetValue(newValue);
                    }
                }
            }

            var xml = document.ToString();
            _innerFormatter.Format(writer, xml, options);
        }
    }

    private sealed class ScrubXmlNodeFormatter : ValueFormatter
    {
        private static readonly XmlFormatter DefaultXmlFormatter = new();

        private readonly string _xpath;
        private readonly IXmlNamespaceResolver? _nsResolver;
        private readonly Func<XNode, XNode?> _scrubber;
        private readonly ValueFormatter _innerFormatter;

        public ScrubXmlNodeFormatter(string xpath, IXmlNamespaceResolver? nsResolver, Func<XNode, XNode?> scrubber, ValueFormatter? innerFormatter)
        {
            _xpath = xpath;
            _nsResolver = nsResolver;
            _scrubber = scrubber;
            _innerFormatter = innerFormatter ?? DefaultXmlFormatter;
        }

        public override void Format(HumanReadableTextWriter writer, string? value, HumanReadableSerializerOptions options)
        {
            var document = XDocument.Parse(value);
            var result = document.XPathEvaluate(_xpath, _nsResolver);
            var navigator = (IEnumerable<object>)result;
            foreach (var item in navigator)
            {
                if (item is XNode node)
                {
                    var newValue = _scrubber(node);
                    if (newValue is null)
                    {
                        node.Remove();
                    }
                    else
                    {
                        node.AddBeforeSelf(newValue);
                        node.Remove();
                    }
                }
            }

            var xml = document.ToString();
            _innerFormatter.Format(writer, xml, options);
        }
    }
}
