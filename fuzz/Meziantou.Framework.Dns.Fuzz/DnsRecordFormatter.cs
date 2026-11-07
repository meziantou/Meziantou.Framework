using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Reflection;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.DnsFuzz;

/// <summary>
/// Formats the data of a record of either library as text, so that two decoders can be compared. The server and the
/// client name the properties of their records the same way (<c>DnsMxRecordData.Exchange</c>, <c>DnsMxRecord.Exchange</c>),
/// so the properties declared by the record type are formatted in name order; the domain names go through a function,
/// because the libraries do not represent them the same way.
/// </summary>
internal static class DnsRecordFormatter
{
    // The properties that hold a domain name, by record type name without the "Data" suffix of the server types
    private static readonly HashSet<string> NameProperties = new(StringComparer.Ordinal)
    {
        "DnsCnameRecord.CanonicalName",
        "DnsDnameRecord.Target",
        "DnsMxRecord.Exchange",
        "DnsNaptrRecord.Replacement",
        "DnsNsRecord.NameServer",
        "DnsNsecRecord.NextDomainName",
        "DnsPtrRecord.DomainName",
        "DnsRpRecord.Mailbox",
        "DnsRpRecord.TxtDomainName",
        "DnsRrsigRecord.SignerName",
        "DnsSoaRecord.PrimaryNameServer",
        "DnsSoaRecord.ResponsibleMailbox",
        "DnsSrvRecord.Target",
        "DnsSvcbRecord.TargetName",
    };

    // The client's OPT record also exposes the fields of its TTL and CLASS, which the server keeps in the record itself
    private static readonly HashSet<string> IgnoredProperties = new(StringComparer.Ordinal)
    {
        "DnsOptRecord.UdpPayloadSize",
        "DnsOptRecord.ExtendedRCode",
        "DnsOptRecord.EdnsVersion",
        "DnsOptRecord.DnssecOk",
    };

    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> PropertyCache = new();

    /// <summary>Formats the properties declared by the type of <paramref name="data"/>.</summary>
    /// <param name="data">A record (client) or a record data (server).</param>
    /// <param name="formatName">Formats a domain name.</param>
    /// <param name="sortTypeBitMaps">Formats the NSEC and NSEC3 type bit maps as sorted sets, which is how the encoders write them.</param>
    public static string Format(object? data, Func<string, string> formatName, bool sortTypeBitMaps)
    {
        if (data is null)
            return "null";

        var type = data.GetType();
        var typeName = type.Name.EndsWith("Data", StringComparison.Ordinal) ? type.Name[..^"Data".Length] : type.Name;
        var builder = new StringBuilder();
        builder.Append(typeName).Append('{');
        var first = true;
        foreach (var property in GetProperties(type))
        {
            var key = typeName + "." + property.Name;
            if (IgnoredProperties.Contains(key))
                continue;

            if (!first)
            {
                builder.Append(", ");
            }

            first = false;
            builder.Append(property.Name).Append('=');
            var value = property.GetValue(data);
            if (NameProperties.Contains(key))
            {
                builder.Append(FuzzText.Escape(formatName((string)value!)));
            }
            else if (sortTypeBitMaps && property.Name is "TypeBitMaps")
            {
                var types = ((IEnumerable)value!).Cast<object>().Select(item => Convert.ToUInt16(item, CultureInfo.InvariantCulture)).Distinct().Order();
                builder.Append('[').AppendJoin(',', types).Append(']');
            }
            else
            {
                AppendValue(builder, value);
            }
        }

        builder.Append('}');
        return builder.ToString();
    }

    private static void AppendValue(StringBuilder builder, object? value)
    {
        switch (value)
        {
            case null:
                builder.Append("null");
                break;

            case string text:
                builder.Append(FuzzText.Escape(text));
                break;

            case byte[] bytes:
                builder.Append("0x").Append(Convert.ToHexString(bytes));
                break;

            case Enum:
                builder.Append(Convert.ToUInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture));
                break;

            case IPAddress address:
                builder.Append(address);
                break;

            case IFormattable formattable:
                builder.Append(formattable.ToString(format: null, CultureInfo.InvariantCulture));
                break;

            case bool boolean:
                builder.Append(boolean ? "true" : "false");
                break;

            case IEnumerable items:
                builder.Append('[');
                var first = true;
                foreach (var item in items)
                {
                    if (!first)
                    {
                        builder.Append(',');
                    }

                    first = false;
                    AppendValue(builder, item);
                }

                builder.Append(']');
                break;

            default:
                // EDNS options and SvcParams: their properties
                builder.Append('{');
                var firstProperty = true;
                foreach (var property in GetProperties(value.GetType()))
                {
                    if (!firstProperty)
                    {
                        builder.Append(", ");
                    }

                    firstProperty = false;
                    builder.Append(property.Name).Append('=');
                    AppendValue(builder, property.GetValue(value));
                }

                builder.Append('}');
                break;
        }
    }

    private static PropertyInfo[] GetProperties(Type type)
    {
        return PropertyCache.GetOrAdd(type, type => [.. type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).OrderBy(property => property.Name, StringComparer.Ordinal)]);
    }
}
