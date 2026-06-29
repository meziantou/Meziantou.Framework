using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Meziantou.Framework.DnsServer.Protocol;
using Meziantou.Framework.DnsServer.Protocol.Records;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meziantou.DnsProxy.Proxy;

internal sealed class CustomDnsRecordProvider
{
    private const uint CustomRecordTimeToLive = 60;
    private readonly IReadOnlyList<CustomDnsRecord> _records;

    public CustomDnsRecordProvider(IOptions<DnsProxyOptions> options, ILogger<CustomDnsRecordProvider> logger)
    {
        _records = CreateRecords(options.Value.CustomRecords, logger);
    }

    public bool TryApply(DnsQuestion question, DnsMessage response)
    {
        var normalizedQuestionName = NormalizeDomain(question.Name);
        var matchingRecords = _records
            .Where(record => record.Domain.Equals(normalizedQuestionName, StringComparison.OrdinalIgnoreCase) && (question.Type == DnsQueryType.ANY || record.Type == question.Type))
            .ToArray();

        if (matchingRecords.Length == 0)
        {
            return false;
        }

        response.ResponseCode = DnsResponseCode.NoError;
        foreach (var record in matchingRecords)
        {
            response.Answers.Add(new DnsResourceRecord
            {
                Name = question.Name,
                Type = record.Type,
                Class = DnsQueryClass.IN,
                TimeToLive = CustomRecordTimeToLive,
                Data = record.Data,
            });
        }

        return true;
    }

    private static List<CustomDnsRecord> CreateRecords(IEnumerable<CustomDnsRecordOption> options, ILogger logger)
    {
        var records = new List<CustomDnsRecord>();
        foreach (var option in options)
        {
            var domain = NormalizeDomain(option.Domain);
            if (string.IsNullOrWhiteSpace(domain))
            {
                logger.LogWarning("Skipping custom DNS record with an empty domain");
                continue;
            }

            if (!Enum.TryParse<DnsQueryType>(option.Type, ignoreCase: true, out var type))
            {
                logger.LogWarning("Skipping custom DNS record for domain {Domain} because type {Type} is not supported", option.Domain, option.Type);
                continue;
            }

            foreach (var value in EnumerateValues(option))
            {
                if (!TryCreateRecordData(type, value, out var data))
                {
                    logger.LogWarning("Skipping invalid custom DNS record {Domain} {Type} {Value}", option.Domain, option.Type, value);
                    continue;
                }

                records.Add(new CustomDnsRecord(domain, type, data));
            }
        }

        return records;
    }

    private static IEnumerable<string> EnumerateValues(CustomDnsRecordOption option)
    {
        if (!string.IsNullOrWhiteSpace(option.Value))
        {
            yield return option.Value;
        }

        foreach (var value in option.Values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                yield return value;
            }
        }
    }

    private static bool TryCreateRecordData(DnsQueryType type, string value, [NotNullWhen(true)] out DnsResourceRecordData? data)
    {
        data = type switch
        {
            DnsQueryType.A when IPAddress.TryParse(value, out var address) && address.AddressFamily == AddressFamily.InterNetwork
                => new DnsARecordData { Address = address },
            DnsQueryType.AAAA when IPAddress.TryParse(value, out var address) && address.AddressFamily == AddressFamily.InterNetworkV6
                => new DnsAaaaRecordData { Address = address },
            DnsQueryType.CNAME
                => new DnsCnameRecordData { CanonicalName = value },
            DnsQueryType.PTR
                => new DnsPtrRecordData { DomainName = value },
            DnsQueryType.NS
                => new DnsNsRecordData { NameServer = value },
            DnsQueryType.TXT
                => new DnsTxtRecordData { Text = [value] },
            _ => TryCreateStructuredRecordData(type, value),
        };

        return data is not null;
    }

    private static DnsResourceRecordData? TryCreateStructuredRecordData(DnsQueryType type, string value)
    {
        return type switch
        {
            DnsQueryType.MX when TrySplit(value, 2, out var parts) && ushort.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var preference)
                => new DnsMxRecordData { Preference = preference, Exchange = parts[1] },
            DnsQueryType.SRV when TrySplit(value, 4, out var parts)
                && ushort.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var priority)
                && ushort.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var weight)
                && ushort.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var port)
                => new DnsSrvRecordData { Priority = priority, Weight = weight, Port = port, Target = parts[3] },
            DnsQueryType.CAA when TrySplit(value, 3, out var parts) && byte.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var flags)
                => new DnsCaaRecordData { Flags = flags, Tag = parts[1], Value = parts[2] },
            _ => null,
        };
    }

    private static bool TrySplit(string value, int count, [NotNullWhen(true)] out string[]? parts)
    {
        parts = value.Split((char[]?)null, count, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == count;
    }

    private static string NormalizeDomain(string domain)
    {
        return domain.Trim().TrimEnd('.');
    }

    private sealed record CustomDnsRecord(string Domain, DnsQueryType Type, DnsResourceRecordData Data);
}
