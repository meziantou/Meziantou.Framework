---
title: Writing a handler
description: Read the query and the client, build the response with typed records, set the response code, and handle errors and cancellation.
---

The handler is a `DnsRequestDelegate`: it receives a `DnsRequestContext` and a `CancellationToken`, and returns a
`ValueTask<DnsMessage>`, the response.

## Read the query

The context has the query, the transport and the address of the client. This handler answers `whoami.example.com`
with them:

<!-- ref:../../../tests/Meziantou.Framework.DnsServer.Tests/DnsServerIntegrationTests.cs;region=DnsServer_RequestContext;format=md-fenced-code-block;dedent=true -->
```csharp
DnsRequestDelegate handler = (context, cancellationToken) =>
{
    var response = context.CreateResponse();
    foreach (var question in context.Query.Questions)
    {
        if (question.Type is DnsQueryType.TXT && string.Equals(question.Name, "whoami.example.com", StringComparison.OrdinalIgnoreCase))
        {
            response.Answers.Add(new DnsResourceRecord
            {
                Name = question.Name,
                Type = DnsQueryType.TXT,
                Class = DnsQueryClass.IN,
                TimeToLive = 0, // must not be cached: it depends on the client
                Data = new DnsTxtRecordData
                {
                    Text =
                    [
                        $"address={((IPEndPoint)context.RemoteEndPoint).Address}",
                        $"protocol={context.Protocol}",
                        $"dnssec-ok={context.Query.EdnsOptions?.DnssecOk ?? false}",
                    ],
                },
            });
        }
    }

    if (response.Answers.Count is 0)
    {
        response.ResponseCode = DnsResponseCode.NameError;
    }

    return ValueTask.FromResult(response);
};
```
<!-- endref -->

| Member | Contains |
| --- | --- |
| `Query` | The decoded query: `Questions`, `Id`, `OpCode`, `RecursionDesired`, `CheckingDisabled`, `EdnsOptions` (`null` without EDNS; `DnssecOk`, `UdpPayloadSize`) |
| `Protocol` | `Udp`, `Tcp`, `Tls`, `Https` or `Quic` |
| `RemoteEndPoint` | The address and port of the client. For DNS over HTTPS, the remote address of the HTTP connection (`HttpContext.Connection`): behind a reverse proxy, the address of the proxy, unless the forwarded headers middleware replaces it. |
| `CreateResponse()` | A response with the ID, the opcode, the RD flag and the questions of the query, `NoError`, and an OPT record when the query has one |

A query almost always has one question, but the protocol allows any number: check `Questions.Count` rather than read
`Questions[0]` blindly.

## Build the response

Start from `CreateResponse()`, then fill the sections:

- `Answers`: the records that answer the question.
- `Authorities`: the SOA record of a negative answer, or the NS records of a delegation. See
  [Serving a zone](04-ServingAZone.md).
- `AdditionalRecords`: extra records, such as the addresses of the name servers.

Each record is a `DnsResourceRecord` with a `Name`, a `Type`, a `Class` (`IN`), a `TimeToLive` in seconds, and a
`Data` object of the matching type, from the `Meziantou.Framework.DnsServer.Protocol.Records` namespace:
`DnsARecordData`, `DnsAaaaRecordData`, `DnsCnameRecordData`, `DnsMxRecordData`, `DnsTxtRecordData`, `DnsSoaRecordData`,
`DnsSrvRecordData`, `DnsPtrRecordData`, `DnsNsRecordData`, `DnsCaaRecordData`, `DnsSvcbRecordData` (SVCB and HTTPS),
the DNSSEC records (`DnsDnskeyRecordData`, `DnsRrsigRecordData`, ...), and `DnsUnknownRecordData` for raw data of any
other type.

The flags are properties of `DnsMessage`: set `IsAuthoritative` for the answers of your own zone, and
`RecursionAvailable` when the server resolves other names. `AuthenticatedData` sets the AD flag: only set it when you
validated the DNSSEC signatures of the answer.

### Response codes

Set `ResponseCode`:

| `DnsResponseCode` | Use it when |
| --- | --- |
| `NoError` | The name exists. With no answer, it has no record of this type (NODATA). |
| `NameError` | The name does not exist (NXDOMAIN). |
| `Refused` | The server does not answer this query: a name outside its zones, or a client it does not serve. |
| `ServerFailure` | The handler could not answer, for instance because a database or an upstream server failed. |
| `NotImplemented` | The server does not support the request, such as an opcode other than `Query`. |
| `FormError` | The query is malformed, for instance it has no question. |

Set extended codes, above 15, such as `BadCookie`, the same way: the encoder splits the value between the header and
the OPT record, and adds an OPT record when the response has none.

### Names

Names are ASCII on the wire. Convert internationalized names to their punycode form (`xn--...`) before putting them in
a response: a name with non-ASCII characters throws `DnsProtocolException` when the response is encoded, instead of
being corrupted.

### Truncation

A response too large for the transport is truncated, with the TC flag set, so the client retries over TCP. The handler
does not measure it. See [Limits and hardening](05-LimitsAndHardening.md#response-size).

## Handle errors

When the handler throws, the server logs the exception and sends no DNS response: over UDP, TCP, TLS and QUIC, the
client waits until it times out, then usually retries, and over DNS over HTTPS the HTTP request fails. Catch the errors
you expect and answer `ServerFailure`, so the client knows at once:

<!-- ref:../../../tests/Meziantou.Framework.DnsServer.Tests/DnsServerIntegrationTests.cs;region=DnsServer_HandlerErrors;format=md-fenced-code-block;dedent=true -->
```csharp
Func<string, CancellationToken, Task<IPAddress>> lookupAddress = (name, cancellationToken) => throw new InvalidOperationException("The database is not available");

DnsRequestDelegate handler = async (context, cancellationToken) =>
{
    var response = context.CreateResponse();
    try
    {
        foreach (var question in context.Query.Questions.Where(question => question.Type is DnsQueryType.A))
        {
            var address = await lookupAddress(question.Name, cancellationToken);
            response.Answers.Add(new DnsResourceRecord
            {
                Name = question.Name,
                Type = DnsQueryType.A,
                Class = DnsQueryClass.IN,
                TimeToLive = 60,
                Data = new DnsARecordData { Address = address },
            });
        }
    }
    catch (InvalidOperationException)
    {
        // Without this, the exception is logged and the client gets no answer until it times out
        response.Answers.Clear();
        response.ResponseCode = DnsResponseCode.ServerFailure;
    }

    return response;
};
```
<!-- endref -->

## Cancellation and concurrency

The `CancellationToken` is canceled when the application stops; for TCP and DNS over TLS, also when the client closes
the connection, and for DNS over HTTPS when the HTTP request is aborted. Pass it to the asynchronous calls of the
handler.

The handler is called concurrently: for every UDP datagram as it arrives, and for up to
`MaxConcurrentQueriesPerConnection` queries at once on each TCP, TLS or QUIC connection. On a connection, responses are
sent as they complete, so they can come back in another order than the queries, as RFC 7766 allows. Make the handler
thread-safe.
