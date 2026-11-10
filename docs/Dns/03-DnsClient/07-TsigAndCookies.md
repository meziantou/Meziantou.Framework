---
title: TSIG and cookies
description: Sign the queries and authenticate the responses with a TSIG key, and protect the queries with DNS cookies.
---

## Sign with a TSIG key

A TSIG key (RFC 8945) is a secret shared with the server. Set [TsigKey](cref:Meziantou.Framework.DnsClient.DnsClientOptions.TsigKey) to sign every query of the client, and to
reject any response that is not signed with the same key. Servers often require it for [zone transfers](06-ZoneTransfers.md):

<!-- ref:../../../tests/Meziantou.Framework.DnsClient.Tests/DnsClientIntegrationTests.cs;region=DnsClient_Tsig;format=md-fenced-code-block;dedent=true -->
```csharp
// The name, algorithm and secret of the key configured on the server
var key = new DnsTsigKey("transfer-key", DnsTsigAlgorithm.HmacSha256, Convert.FromBase64String("xnRdh6uLqXIbHHeRPrsytA3ZCoYbpSUjYWBRftgDE/s="));
using var client = new DnsClient(serverAddress, DnsClientProtocol.Tcp, new DnsClientOptions
{
    TsigKey = key,
});

// Every query is signed, and every response is verified: an unsigned or forged response throws
var recordCount = 0;
await foreach (var message in client.TransferZoneAsync("example.com"))
{
    Assert.Equal(DnsResponseCode.NoError, message.Header.ResponseCode);
    recordCount += message.Answers.Count;
}

Assert.Equal(5, recordCount);
```
<!-- endref -->

- A [DnsTsigKey](cref:Meziantou.Framework.DnsClient.DnsTsigKey) has the name of the key on the server, an algorithm and the secret. Key files store the secret in base64:
  decode it with `Convert.FromBase64String`.
- The algorithms are [HmacSha256](cref:Meziantou.Framework.DnsClient.DnsTsigAlgorithm.HmacSha256) (the one every server supports), [HmacSha384](cref:Meziantou.Framework.DnsClient.DnsTsigAlgorithm.HmacSha384), [HmacSha512](cref:Meziantou.Framework.DnsClient.DnsTsigAlgorithm.HmacSha512), and, for old servers,
  [HmacSha1](cref:Meziantou.Framework.DnsClient.DnsTsigAlgorithm.HmacSha1) and [HmacMd5](cref:Meziantou.Framework.DnsClient.DnsTsigAlgorithm.HmacMd5).
- The signatures carry the time. A response signed further from the clock of [TimeProvider](cref:Meziantou.Framework.DnsClient.DnsClientOptions.TimeProvider) than
  [Fudge](cref:Meziantou.Framework.DnsClient.DnsTsigKey.Fudge) (5 minutes by default) is rejected, and the server applies the same check to the queries: keep the clocks in sync.

### Verification

Every query is signed: [QueryAsync](cref:Meziantou.Framework.DnsClient.DnsClient.QueryAsync), [SendAsync](cref:Meziantou.Framework.DnsClient.DnsClient.SendAsync), [ReverseLookupAsync](cref:Meziantou.Framework.DnsClient.DnsClient.ReverseLookupAsync), [TransferZoneAsync](cref:Meziantou.Framework.DnsClient.DnsClient.TransferZoneAsync), and the queries of
local DNSSEC validation. The response is returned only when its signature is valid:

- A response that is not signed, signed with another key or algorithm, whose signature does not match, or signed too
  far from the current time, throws [DnsProtocolException](cref:Meziantou.Framework.DnsClient.DnsProtocolException).
- In a zone transfer, the server may leave up to 99 messages in a row unsigned: the next signature covers them. The first
  and the last messages must be signed.
- When the server cannot verify the query, the response code is the TSIG error: [BadKey](cref:Meziantou.Framework.DnsClient.Response.DnsResponseCode.BadKey) (the server does not know the
  key), [BadSignature](cref:Meziantou.Framework.DnsClient.Response.DnsResponseCode.BadSignature) (the secret differs) or [BadTime](cref:Meziantou.Framework.DnsClient.Response.DnsResponseCode.BadTime) (the clocks differ by more than the fudge). Such a response
  cannot be authenticated and carries no records. [BadSignature](cref:Meziantou.Framework.DnsClient.Response.DnsResponseCode.BadSignature) has the value 16 of [BadVersion](cref:Meziantou.Framework.DnsClient.Response.DnsResponseCode.BadVersion): compare the
  values, not the names.
- The signature of a response is the [DnsTsigRecord](cref:Meziantou.Framework.DnsClient.Response.Records.DnsTsigRecord), the last of its [AdditionalRecords](cref:Meziantou.Framework.DnsClient.Response.DnsResponseMessage.AdditionalRecords).

## Use DNS cookies

DNS cookies (RFC 7873) make it harder for an attacker who does not see the traffic to forge responses, and let the
server recognize the client. Set [EnableCookies](cref:Meziantou.Framework.DnsClient.DnsClientOptions.EnableCookies):

<!-- ref:../../../tests/Meziantou.Framework.DnsClient.Tests/DnsClientIntegrationTests.cs;region=DnsClient_Cookies;format=md-fenced-code-block;dedent=true -->
```csharp
using var client = new DnsClient(serverAddress, DnsClientProtocol.Tcp, new DnsClientOptions
{
    EnableCookies = true,
});

// The first query sends a client cookie; the next ones also return the cookie the server sent back
var response = await client.QueryAsync("www.example.com", DnsQueryType.A);
Assert.Equal(DnsResponseCode.NoError, response.Header.ResponseCode);
response = await client.QueryAsync("www.example.com", DnsQueryType.A);
Assert.Equal(DnsResponseCode.NoError, response.Header.ResponseCode);
```
<!-- endref -->

- Each [DnsClient](cref:Meziantou.Framework.DnsClient.DnsClient) has its own random client cookie. The server cookie of the last response is sent with the next queries.
- A response with a cookie that is not the client cookie throws [DnsProtocolException](cref:Meziantou.Framework.DnsClient.DnsProtocolException). A server without cookies
  answers as usual.
- A [BadCookie](cref:Meziantou.Framework.DnsClient.Response.DnsResponseCode.BadCookie) response is sent again once, with the server cookie it returned. If the server answers
  [BadCookie](cref:Meziantou.Framework.DnsClient.Response.DnsResponseCode.BadCookie) again, that response is returned.
- Cookies need EDNS: creating a client with `EnableEdns = false` and `EnableCookies = true` throws `ArgumentException`.
- Cookies do not authenticate the answer: use TSIG, an encrypted transport or [DNSSEC](04-Dnssec.md) when it must be
  trusted.
