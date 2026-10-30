---
title: Writing a handler
description: Read the query and the client, build the response with typed records, set the response code, and handle errors and cancellation.
---

The handler is a [DnsRequestDelegate](cref:Meziantou.Framework.DnsServer.Handler.DnsRequestDelegate): it receives a [DnsRequestContext](cref:Meziantou.Framework.DnsServer.Handler.DnsRequestContext) and a `CancellationToken`, and returns a
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
| `Query` | The decoded query: [Questions](cref:Meziantou.Framework.DnsServer.Protocol.DnsMessage.Questions), [Id](cref:Meziantou.Framework.DnsServer.Protocol.DnsMessage.Id), [OpCode](cref:Meziantou.Framework.DnsServer.Protocol.DnsMessage.OpCode), [RecursionDesired](cref:Meziantou.Framework.DnsServer.Protocol.DnsMessage.RecursionDesired), [CheckingDisabled](cref:Meziantou.Framework.DnsServer.Protocol.DnsMessage.CheckingDisabled), [EdnsOptions](cref:Meziantou.Framework.DnsServer.Protocol.DnsMessage.EdnsOptions) (`null` without EDNS; [DnssecOk](cref:Meziantou.Framework.DnsServer.Protocol.DnsEdnsOptions.DnssecOk), [UdpPayloadSize](cref:Meziantou.Framework.DnsServer.Protocol.DnsEdnsOptions.UdpPayloadSize)) |
| [Protocol](cref:Meziantou.Framework.DnsServer.Handler.DnsRequestContext.Protocol) | [Udp](cref:Meziantou.Framework.DnsServer.Handler.DnsServerProtocol.Udp), [Tcp](cref:Meziantou.Framework.DnsServer.Handler.DnsServerProtocol.Tcp), [Tls](cref:Meziantou.Framework.DnsServer.Handler.DnsServerProtocol.Tls), [Https](cref:Meziantou.Framework.DnsServer.Handler.DnsServerProtocol.Https) or [Quic](cref:Meziantou.Framework.DnsServer.Handler.DnsServerProtocol.Quic) |
| [RemoteEndPoint](cref:Meziantou.Framework.DnsServer.Handler.DnsRequestContext.RemoteEndPoint) | The address and port of the client. For DNS over HTTPS, the remote address of the HTTP connection (`HttpContext.Connection`): behind a reverse proxy, the address of the proxy, unless the forwarded headers middleware replaces it. |
| [CreateResponse()](cref:Meziantou.Framework.DnsServer.Handler.DnsRequestContext.CreateResponse) | A response with the ID, the opcode, the RD flag and the questions of the query, [NoError](cref:Meziantou.Framework.DnsServer.Protocol.DnsResponseCode.NoError), and an OPT record when the query has one |

A query almost always has one question, but the protocol allows any number: check `Questions.Count` rather than read
`Questions[0]` blindly.

## Build the response

Start from [CreateResponse()](cref:Meziantou.Framework.DnsServer.Handler.DnsRequestContext.CreateResponse), then fill the sections:

- [Answers](cref:Meziantou.Framework.DnsServer.Protocol.DnsMessage.Answers): the records that answer the question.
- [Authorities](cref:Meziantou.Framework.DnsServer.Protocol.DnsMessage.Authorities): the SOA record of a negative answer, or the NS records of a delegation. See
  [Serving a zone](04-ServingAZone.md).
- [AdditionalRecords](cref:Meziantou.Framework.DnsServer.Protocol.DnsMessage.AdditionalRecords): extra records, such as the addresses of the name servers.

Each record is a [DnsResourceRecord](cref:Meziantou.Framework.DnsServer.Protocol.DnsResourceRecord) with a [Name](cref:Meziantou.Framework.DnsServer.Protocol.DnsResourceRecord.Name), a [Type](cref:Meziantou.Framework.DnsServer.Protocol.DnsResourceRecord.Type), a [Class](cref:Meziantou.Framework.DnsServer.Protocol.DnsResourceRecord.Class) ([IN](cref:Meziantou.Framework.DnsServer.Protocol.DnsQueryClass.IN)), a [TimeToLive](cref:Meziantou.Framework.DnsServer.Protocol.DnsResourceRecord.TimeToLive) in seconds, and a
[Data](cref:Meziantou.Framework.DnsServer.Protocol.DnsResourceRecord.Data) object of the matching type, from the [Meziantou.Framework.DnsServer.Protocol.Records](cref:Meziantou.Framework.DnsServer.Protocol.Records) namespace:
[DnsARecordData](cref:Meziantou.Framework.DnsServer.Protocol.Records.DnsARecordData), [DnsAaaaRecordData](cref:Meziantou.Framework.DnsServer.Protocol.Records.DnsAaaaRecordData), [DnsCnameRecordData](cref:Meziantou.Framework.DnsServer.Protocol.Records.DnsCnameRecordData), [DnsMxRecordData](cref:Meziantou.Framework.DnsServer.Protocol.Records.DnsMxRecordData), [DnsTxtRecordData](cref:Meziantou.Framework.DnsServer.Protocol.Records.DnsTxtRecordData), [DnsSoaRecordData](cref:Meziantou.Framework.DnsServer.Protocol.Records.DnsSoaRecordData),
[DnsSrvRecordData](cref:Meziantou.Framework.DnsServer.Protocol.Records.DnsSrvRecordData), [DnsPtrRecordData](cref:Meziantou.Framework.DnsServer.Protocol.Records.DnsPtrRecordData), [DnsNsRecordData](cref:Meziantou.Framework.DnsServer.Protocol.Records.DnsNsRecordData), [DnsCaaRecordData](cref:Meziantou.Framework.DnsServer.Protocol.Records.DnsCaaRecordData), [DnsSvcbRecordData](cref:Meziantou.Framework.DnsServer.Protocol.Records.DnsSvcbRecordData) (SVCB and HTTPS),
the DNSSEC records ([DnsDnskeyRecordData](cref:Meziantou.Framework.DnsServer.Protocol.Records.DnsDnskeyRecordData), [DnsRrsigRecordData](cref:Meziantou.Framework.DnsServer.Protocol.Records.DnsRrsigRecordData), ...), and [DnsUnknownRecordData](cref:Meziantou.Framework.DnsServer.Protocol.Records.DnsUnknownRecordData) for raw data of any
other type.

The flags are properties of [DnsMessage](cref:Meziantou.Framework.DnsServer.Protocol.DnsMessage): set [IsAuthoritative](cref:Meziantou.Framework.DnsServer.Protocol.DnsMessage.IsAuthoritative) for the answers of your own zone, and
[RecursionAvailable](cref:Meziantou.Framework.DnsServer.Protocol.DnsMessage.RecursionAvailable) when the server resolves other names. [AuthenticatedData](cref:Meziantou.Framework.DnsServer.Protocol.DnsMessage.AuthenticatedData) sets the AD flag: only set it when you
validated the DNSSEC signatures of the answer.

### Response codes

Set [ResponseCode](cref:Meziantou.Framework.DnsServer.Protocol.DnsMessage.ResponseCode):

| [DnsResponseCode](cref:Meziantou.Framework.DnsServer.Protocol.DnsResponseCode) | Use it when |
| --- | --- |
| [NoError](cref:Meziantou.Framework.DnsServer.Protocol.DnsResponseCode.NoError) | The name exists. With no answer, it has no record of this type (NODATA). |
| [NameError](cref:Meziantou.Framework.DnsServer.Protocol.DnsResponseCode.NameError) | The name does not exist (NXDOMAIN). |
| [Refused](cref:Meziantou.Framework.DnsServer.Protocol.DnsResponseCode.Refused) | The server does not answer this query: a name outside its zones, or a client it does not serve. |
| [ServerFailure](cref:Meziantou.Framework.DnsServer.Protocol.DnsResponseCode.ServerFailure) | The handler could not answer, for instance because a database or an upstream server failed. |
| [NotImplemented](cref:Meziantou.Framework.DnsServer.Protocol.DnsResponseCode.NotImplemented) | The server does not support the request, such as an opcode other than `Query`. |
| [FormError](cref:Meziantou.Framework.DnsServer.Protocol.DnsResponseCode.FormError) | The query is malformed, for instance it has no question. |

Set extended codes, above 15, such as [BadCookie](cref:Meziantou.Framework.DnsServer.Protocol.DnsResponseCode.BadCookie), the same way: the encoder splits the value between the header and
the OPT record, and adds an OPT record when the response has none.

### Names

Names are ASCII on the wire. Convert internationalized names to their punycode form (`xn--...`) before putting them in
a response: a name with non-ASCII characters throws [DnsProtocolException](cref:Meziantou.Framework.DnsServer.Protocol.DnsProtocolException) when the response is encoded, instead of
being corrupted.

### Truncation

A response too large for the transport is truncated, with the TC flag set, so the client retries over TCP. The handler
does not measure it. See [Limits and hardening](05-LimitsAndHardening.md#response-size).

## Handle errors

When the handler throws, the server logs the exception and sends no DNS response: over UDP, TCP, TLS and QUIC, the
client waits until it times out, then usually retries, and over DNS over HTTPS the HTTP request fails. Catch the errors
you expect and answer [ServerFailure](cref:Meziantou.Framework.DnsServer.Protocol.DnsResponseCode.ServerFailure), so the client knows at once:

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
[MaxConcurrentQueriesPerConnection](cref:Meziantou.Framework.DnsServer.Hosting.DnsServerOptions.MaxConcurrentQueriesPerConnection) queries at once on each TCP, TLS or QUIC connection. On a connection, responses are
sent as they complete, so they can come back in another order than the queries, as RFC 7766 allows. Make the handler
thread-safe.
