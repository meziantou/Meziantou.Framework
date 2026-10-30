---
title: Querying
description: Query a record type, read typed records, handle NXDOMAIN, look up an IP address, and send custom query messages.
---

The examples query a server that holds a small `example.com` zone. `client` is a `DnsClient`; see
[Protocols](02-Protocols.md) to create one.

## Read typed records

`QueryAsync` takes a name and a `DnsQueryType`, and returns a `DnsResponseMessage`. Each record of `Answers` has the
type of its data: filter with `OfType`:

<!-- ref:../../../tests/Meziantou.Framework.DnsClient.Tests/DnsClientIntegrationTests.cs;region=DnsClient_ReadRecords;format=md-fenced-code-block;dedent=true -->
```csharp
var mxResponse = await client.QueryAsync("example.com", DnsQueryType.MX);
var mailServer = Assert.Single(mxResponse.Answers.OfType<DnsMxRecord>());
Assert.Equal((ushort)10, mailServer.Preference);
Assert.Equal("mail.example.com", mailServer.Exchange);
Assert.Equal(300u, mailServer.TimeToLive);

var txtResponse = await client.QueryAsync("example.com", DnsQueryType.TXT);
var txt = Assert.Single(txtResponse.Answers.OfType<DnsTxtRecord>());
Assert.Equal(["v=spf1 -all"], txt.Text);
```
<!-- endref -->

Every record has a `Name`, a `RecordType`, a `RecordClass`, a `TimeToLive` (in seconds) and a `DataLength`. A
`QueryAsync` overload takes a `DnsQueryClass` for the classes other than `IN`.

### Typed record classes

The records are in the [Meziantou.Framework.DnsClient.Response.Records](cref:Meziantou.Framework.DnsClient.Response.Records) namespace:

| Record types | Class |
| --- | --- |
| A, AAAA | `DnsARecord`, `DnsAaaaRecord` (`Address`) |
| CNAME, DNAME, NS, PTR | `DnsCnameRecord`, `DnsDnameRecord`, `DnsNsRecord`, `DnsPtrRecord` |
| MX, SRV, URI | `DnsMxRecord`, `DnsSrvRecord`, `DnsUriRecord` |
| TXT | `DnsTxtRecord` (`Text`: the character strings of the record) |
| SOA | `DnsSoaRecord` |
| CAA, TLSA, SSHFP | `DnsCaaRecord`, `DnsTlsaRecord`, `DnsSshfpRecord` |
| SVCB, HTTPS | `DnsSvcbRecord` (`Priority`, `TargetName`, `Parameters`) |
| NAPTR, LOC, HINFO, RP | `DnsNaptrRecord`, `DnsLocRecord`, `DnsHinfoRecord`, `DnsRpRecord` |
| DNSKEY, DS, RRSIG, NSEC, NSEC3, NSEC3PARAM | `DnsDnskeyRecord`, `DnsDsRecord`, `DnsRrsigRecord`, `DnsNsecRecord`, `DnsNsec3Record`, `DnsNsec3ParamRecord` |
| OPT (in `AdditionalRecords`) | `DnsOptRecord` |
| Any other type | `DnsUnknownRecord` (`Data`: the raw RDATA) |

A `switch` on the record handles several types at once:

<!-- ref:../../../tests/Meziantou.Framework.DnsClient.Tests/DnsClientIntegrationTests.cs;region=DnsClient_SwitchOnRecords;format=md-fenced-code-block;dedent=true -->
```csharp
var response = await client.QueryAsync("example.com", DnsQueryType.ANY);

var lines = new List<string>();
foreach (var record in response.Answers)
{
    lines.Add(record switch
    {
        DnsARecord a => $"A {a.Address}",
        DnsMxRecord mx => $"MX {mx.Preference} {mx.Exchange}",
        DnsTxtRecord txt => $"TXT {string.Join(" ", txt.Text)}",
        DnsUnknownRecord unknown => $"{unknown.RecordType} ({unknown.Data.Length} bytes)",
        _ => record.RecordType.ToString(),
    });
}

Assert.Equal(["A 192.0.2.1", "MX 10 mail.example.com", "TXT v=spf1 -all"], lines);
```
<!-- endref -->

## Get the IP addresses of a name

A and AAAA are separate queries. `GetIPAddresses` (and `GetIPv4Addresses`, `GetIPv6Addresses`) collects the addresses
of a list of records, in order:

<!-- ref:../../../tests/Meziantou.Framework.DnsClient.Tests/DnsClientIntegrationTests.cs;region=DnsClient_GetIPAddresses;format=md-fenced-code-block;dedent=true -->
```csharp
var ipv4 = await client.QueryAsync("www.example.com", DnsQueryType.A);
var ipv6 = await client.QueryAsync("www.example.com", DnsQueryType.AAAA);

var addresses = ipv4.Answers.Concat(ipv6.Answers).GetIPAddresses().ToList();
Assert.Equal([IPAddress.Parse("192.0.2.10"), IPAddress.Parse("2001:db8::10")], addresses);
```
<!-- endref -->

The methods only read A and AAAA records: they do not follow a CNAME. When the server returns a CNAME chain, the
addresses are the A or AAAA records of the chain that are in the response.

## Response codes

A response that says "no such name" or "server failure" is a normal response, not an exception. Check
`Header.ResponseCode`:

<!-- ref:../../../tests/Meziantou.Framework.DnsClient.Tests/DnsClientIntegrationTests.cs;region=DnsClient_NameError;format=md-fenced-code-block;dedent=true -->
```csharp
var response = await client.QueryAsync("missing.example.com", DnsQueryType.A);

// NXDOMAIN is an answer, not an exception
Assert.Equal(DnsResponseCode.NameError, response.Header.ResponseCode);
Assert.Empty(response.Answers);

// The SOA record of the zone says how long the absence may be cached
var soa = Assert.Single(response.Authorities.OfType<DnsSoaRecord>());
Assert.Equal(60u, soa.Minimum);
```
<!-- endref -->

| `DnsResponseCode` | Meaning |
| --- | --- |
| `NoError` | The query succeeded. With no answers, the name exists but has no record of that type (NODATA). |
| `NameError` | NXDOMAIN: the name does not exist. |
| `ServerFailure` | The server could not answer, for instance because an upstream server failed. |
| `Refused` | The server does not answer this query, for instance because it does not serve this zone or this client. |
| `FormError`, `NotImplemented` | The server did not understand the query. |
| `BadVersion` and the other values above 15 | Extended codes, carried in the OPT record (EDNS). |

The `Header` also holds the flags of the response: `IsAuthoritative` (the answer comes from the zone itself),
`IsTruncated`, `RecursionAvailable`, `AuthenticatedData` (see [DNSSEC](04-Dnssec.md)), and the record counts.
`Authorities` and `AdditionalRecords` hold the other sections of the response.

## Look up the name of an IP address

`ReverseLookupAsync` queries the PTR record of an IPv4 or IPv6 address, under `in-addr.arpa` or `ip6.arpa`:

<!-- ref:../../../tests/Meziantou.Framework.DnsClient.Tests/DnsClientIntegrationTests.cs;region=DnsClient_LocalReverseLookup;format=md-fenced-code-block;dedent=true -->
```csharp
// Queries the PTR record of 10.2.0.192.in-addr.arpa
var response = await client.ReverseLookupAsync(IPAddress.Parse("192.0.2.10"));

var ptr = Assert.Single(response.Answers.OfType<DnsPtrRecord>());
Assert.Equal("www.example.com", ptr.DomainName);
```
<!-- endref -->

## Internationalized domain names

Names with non-ASCII characters are converted to their punycode form (IDNA) before they are sent, so you can pass
them as they are written. The records of the response carry the punycode form:

<!-- ref:../../../tests/Meziantou.Framework.DnsClient.Tests/DnsClientIntegrationTests.cs;region=DnsClient_LocalIdn;format=md-fenced-code-block;dedent=true -->
```csharp
// Sent as xn--bcher-kva.example.com
var response = await client.QueryAsync("bücher.example.com", DnsQueryType.A);

var record = Assert.Single(response.Answers.OfType<DnsARecord>());
Assert.Equal("xn--bcher-kva.example.com", record.Name);
```
<!-- endref -->

## Send a custom query message

`SendAsync` sends a `DnsQueryMessage` you build, to set the flags yourself:

<!-- ref:../../../tests/Meziantou.Framework.DnsClient.Tests/DnsClientIntegrationTests.cs;region=DnsClient_SendAsync;format=md-fenced-code-block;dedent=true -->
```csharp
var query = new DnsQueryMessage
{
    RecursionDesired = false,
    EdnsOptions = new DnsEdnsOptions { UdpPayloadSize = 1400 },
};
query.Questions.Add(new DnsQuestion("www.example.com", DnsQueryType.AAAA));

var response = await client.SendAsync(query);

var record = Assert.Single(response.Answers.OfType<DnsAaaaRecord>());
Assert.Equal(IPAddress.Parse("2001:db8::10"), record.Address);
Assert.False(response.Header.RecursionDesired);
```
<!-- endref -->

- `RecursionDesired` is `true` by default; set it to `false` to query an authoritative server without recursion.
- `CheckingDisabled` sets the CD flag, which asks a validating resolver to return the answer even when DNSSEC
  validation fails.
- `EdnsOptions` sets the OPT record. When it is `null`, the client adds one with its options, unless `EnableEdns` is
  `false`.
- `Id` is the transaction ID. When it is `null`, the client picks a random one.
- The client does not modify the message: it applies its options to a copy, so you can send the same message several
  times or with several clients.
