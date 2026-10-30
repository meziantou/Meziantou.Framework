---
title: Serving a zone
description: Answer authoritatively for a domain, with NXDOMAIN, NODATA and SOA records, and refuse the names of other zones.
---

The server has no zone files: a zone is the records the handler answers with. This handler serves `example.com` from
a list of records, as an authoritative server does:

<!-- ref:../../../tests/Meziantou.Framework.DnsServer.Tests/DnsServerIntegrationTests.cs;region=DnsServer_Zone;format=md-fenced-code-block;dedent=true -->
```csharp
var soa = new DnsResourceRecord
{
    Name = "example.com",
    Type = DnsQueryType.SOA,
    Class = DnsQueryClass.IN,
    TimeToLive = 3600,
    Data = new DnsSoaRecordData
    {
        PrimaryNameServer = "ns1.example.com",
        ResponsibleMailbox = "hostmaster.example.com",
        Serial = 2026102901,
        Refresh = 7200,
        Retry = 3600,
        Expire = 1209600,
        Minimum = 300, // how long resolvers may cache a negative answer
    },
};

DnsResourceRecord[] records =
[
    soa,
    new() { Name = "example.com", Type = DnsQueryType.MX, Class = DnsQueryClass.IN, TimeToLive = 3600, Data = new DnsMxRecordData { Preference = 10, Exchange = "mail.example.com" } },
    new() { Name = "www.example.com", Type = DnsQueryType.A, Class = DnsQueryClass.IN, TimeToLive = 3600, Data = new DnsARecordData { Address = IPAddress.Parse("192.0.2.10") } },
    new() { Name = "www.example.com", Type = DnsQueryType.AAAA, Class = DnsQueryClass.IN, TimeToLive = 3600, Data = new DnsAaaaRecordData { Address = IPAddress.Parse("2001:db8::10") } },
    new() { Name = "mail.example.com", Type = DnsQueryType.A, Class = DnsQueryClass.IN, TimeToLive = 3600, Data = new DnsARecordData { Address = IPAddress.Parse("192.0.2.25") } },
];

DnsRequestDelegate handler = (context, cancellationToken) =>
{
    var response = context.CreateResponse();
    if (context.Query.Questions is not [var question])
    {
        response.ResponseCode = DnsResponseCode.FormError;
        return ValueTask.FromResult(response);
    }

    var name = question.Name.TrimEnd('.');
    if (!string.Equals(name, "example.com", StringComparison.OrdinalIgnoreCase) && !name.EndsWith(".example.com", StringComparison.OrdinalIgnoreCase))
    {
        // Not a name of this zone, and this server does not resolve other names
        response.ResponseCode = DnsResponseCode.Refused;
        return ValueTask.FromResult(response);
    }

    response.IsAuthoritative = true;
    var recordsOfName = records.Where(record => string.Equals(record.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
    foreach (var record in recordsOfName.Where(record => record.Type == question.Type))
    {
        response.Answers.Add(record);
    }

    if (response.Answers.Count is 0)
    {
        // NXDOMAIN when the name does not exist, NODATA (no error, no answer) when it only has other types
        response.ResponseCode = recordsOfName.Count is 0 ? DnsResponseCode.NameError : DnsResponseCode.NoError;
        response.Authorities.Add(soa);
    }

    return ValueTask.FromResult(response);
};
```
<!-- endref -->

Register it with `app.MapDnsHandler(handler)`, as in [Getting started](../02-GettingStarted.md#answer-dns-queries).

## What an authoritative answer contains

- **The AA flag** (`IsAuthoritative = true`) on the answers of the zone: resolvers then know that the answer comes from
  the zone itself.
- **The records of the question**, with their TTL: resolvers cache them for that long.
- **NXDOMAIN** ([NameError](cref:Meziantou.Framework.DnsServer.Protocol.DnsResponseCode.NameError)) when the name does not exist, and **NODATA** ([NoError](cref:Meziantou.Framework.DnsServer.Protocol.DnsResponseCode.NoError) without answers) when the name
  exists with other record types. Resolvers treat them differently: NXDOMAIN also means "no record of any type, and no
  subdomain".
- **The SOA record in [Authorities](cref:Meziantou.Framework.DnsServer.Protocol.DnsMessage.Authorities)** with every negative answer. Resolvers cache a negative answer for the lower of
  the TTL of the SOA record and its [Minimum](cref:Meziantou.Framework.DnsServer.Protocol.Records.DnsSoaRecordData.Minimum) field (RFC 2308). Without it, they may not cache it, or cache it for a
  default duration.
- **[Refused](cref:Meziantou.Framework.DnsServer.Protocol.DnsResponseCode.Refused)** for names outside the zone, when the server does not resolve other names.

Names are compared without case, as DNS requires (`WWW.example.com` is `www.example.com`), and a trailing dot can
appear in a name: trim it before comparing.

## Go further

- **CNAME**: when a name has a CNAME record, answer every question type with the CNAME, followed by the records of the
  target when it is in the zone.
- **Wildcards**: to answer `*.example.com`, look up the name, then the wildcard of its parent when it does not exist,
  and answer with the queried name.
- **Delegations**: for a subdomain served by other servers, answer with its NS records in [Authorities](cref:Meziantou.Framework.DnsServer.Protocol.DnsMessage.Authorities), without the AA
  flag, and their addresses in [AdditionalRecords](cref:Meziantou.Framework.DnsServer.Protocol.DnsMessage.AdditionalRecords).
- **Other names**: to resolve names outside the zone, forward the query to another server with a [DnsClient](cref:Meziantou.Framework.DnsClient.DnsClient), and
  copy its response. `Meziantou.DnsProxy` does this, with a cache. See [DnsProxy](../06-DnsProxy/01-Overview.md).
- **DNS over HTTPS caching**: the `Cache-Control` header of DNS over HTTPS responses comes from the TTLs and the SOA
  minimum, so the same records also drive HTTP caches. See [DNS over HTTPS](02-Listeners.md#dns-over-https).
