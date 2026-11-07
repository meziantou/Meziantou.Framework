---
title: Errors and timeouts
description: The exceptions of a query, the timeout and cancellation, how responses are checked, and tracing with OpenTelemetry.
---

## Exceptions

A DNS error code (NXDOMAIN, SERVFAIL, REFUSED, ...) is a response, not an exception: see
[Response codes](03-Querying.md#response-codes). [QueryAsync](cref:Meziantou.Framework.DnsClient.DnsClient.QueryAsync), [ReverseLookupAsync](cref:Meziantou.Framework.DnsClient.DnsClient.ReverseLookupAsync) and [SendAsync](cref:Meziantou.Framework.DnsClient.DnsClient.SendAsync) throw when there
is no usable response:

| Exception | Cause |
| --- | --- |
| `TimeoutException` | No response within [DnsClientOptions.Timeout](cref:Meziantou.Framework.DnsClient.DnsClientOptions.Timeout). |
| `OperationCanceledException` | The `CancellationToken` you passed was canceled. |
| [DnsProtocolException](cref:Meziantou.Framework.DnsClient.DnsProtocolException) | The response is malformed, or does not answer the query that was sent. |
| `SocketException`, `IOException` | The transport failed: connection refused, reset, unreachable network (UDP, TCP, TLS, QUIC). |
| `HttpRequestException` | The DNS over HTTPS request failed, including an HTTP error status. |
| `AuthenticationException` | The TLS handshake failed, for instance because the certificate does not match the host name (DNS over TLS). |
| `ObjectDisposedException` | The client was disposed. |

## Timeout and cancellation

[Timeout](cref:Meziantou.Framework.DnsClient.DnsClientOptions.Timeout) (5 seconds by default) bounds each call, including the retry over TCP of a truncated answer and the queries
of [local DNSSEC validation](04-Dnssec.md#validate-locally). When it elapses, the call throws `TimeoutException`; when
your token is canceled, it throws `OperationCanceledException`, so you can tell them apart:

<!-- ref:../../../tests/Meziantou.Framework.DnsClient.Tests/DnsClientIntegrationTests.cs;region=DnsClient_Timeout;format=md-fenced-code-block;dedent=true -->
```csharp
// The server never answers queries for slow.example.com
using var client = new DnsClient(serverAddress, DnsClientProtocol.Udp, new DnsClientOptions
{
    Timeout = TimeSpan.FromSeconds(1),
});

await Assert.ThrowsAsync<TimeoutException>(() => client.QueryAsync("slow.example.com", DnsQueryType.A));
```
<!-- endref -->

`Timeout.InfiniteTimeSpan` disables the timeout; a zero or negative value throws `ArgumentOutOfRangeException` from the
constructor. The client does not retry a query that timed out: catch `TimeoutException` to retry, or to try another
server.

## Response checks

Every response is checked against the query it answers: the transaction ID, the QR (response) flag, the opcode and the
question must match, or the call throws [DnsProtocolException](cref:Meziantou.Framework.DnsClient.DnsProtocolException). Over UDP, the socket is also connected to the server,
so the operating system drops datagrams that come from another address. A stray or forged datagram cannot be taken for
the answer.

These checks do not authenticate the server. Use an encrypted transport ([Protocols](02-Protocols.md)) or
[DNSSEC](04-Dnssec.md) when the answer must be trusted.

## Tracing

The client emits an `Activity` named `dns.query` for each query, from the `ActivitySource` named
`Meziantou.Framework.DnsClient`. Add the source to your OpenTelemetry configuration to trace the queries. The activity
has the tags:

| Tag | Value |
| --- | --- |
| `dns.question.name`, `dns.question.type`, `dns.question.class` | The question |
| `network.transport` | The transport |
| `dns.response.code` | The response code |
| `dns.dnssec.validation_status` | The result of local DNSSEC validation, when enabled |

The status of the activity is `Error` for an exception or a response code other than [NoError](cref:Meziantou.Framework.DnsClient.Response.DnsResponseCode.NoError).
