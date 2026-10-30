---
title: Getting started
description: Install the packages, query a DNS server, and answer DNS queries from an ASP.NET Core application.
---

## Install the packages

Add the package you need to a project:

```bash
dotnet add package Meziantou.Framework.DnsClient
dotnet add package Meziantou.Framework.DnsServer
dotnet add package Meziantou.Framework.DnsFilter
```

To run the DNS proxy instead, install the tool. See [Running the proxy](06-DnsProxy/02-RunningTheProxy.md).

## Query a DNS server

Create a `DnsClient` with the address of the server and the transport, then call `QueryAsync` with a name and a record
type. `serverAddress` is the address of the server, such as `"192.0.2.53"` or `"127.0.0.1:5053"`:

<!-- ref:../../tests/Meziantou.Framework.DnsClient.Tests/DnsClientIntegrationTests.cs;region=DnsClient_GettingStarted;format=md-fenced-code-block;dedent=true -->
```csharp
using var client = new DnsClient(serverAddress, DnsClientProtocol.Udp);
var response = await client.QueryAsync("www.example.com", DnsQueryType.A);

Assert.Equal(DnsResponseCode.NoError, response.Header.ResponseCode);
var addresses = response.Answers.OfType<DnsARecord>().Select(record => record.Address).ToList();
Assert.Equal([IPAddress.Parse("192.0.2.10")], addresses);
```
<!-- endref -->

- The types are in the [Meziantou.Framework.DnsClient](cref:Meziantou.Framework.DnsClient), [Meziantou.Framework.DnsClient.Query](cref:Meziantou.Framework.DnsClient.Query),
  [Meziantou.Framework.DnsClient.Response](cref:Meziantou.Framework.DnsClient.Response) and [Meziantou.Framework.DnsClient.Response.Records](cref:Meziantou.Framework.DnsClient.Response.Records) namespaces.
- `Answers` holds typed records: `DnsARecord`, `DnsMxRecord`, `DnsTxtRecord`, ... See
  [Querying](03-DnsClient/03-Querying.md).
- `DnsClient` is disposable. Reuse one instance for many queries: it resolves the address of the server once, and over
  DNS over HTTPS it keeps its HTTP connections open.
- The other transports take a host name or a URL. See [Protocols](03-DnsClient/02-Protocols.md).

## Answer DNS queries

The server runs in an ASP.NET Core application. `AddDnsServer` adds the listeners, and `MapDnsHandler` registers the
callback that answers every query. Here, `port` is the port to listen on (53 is the standard one):

<!-- ref:../../tests/Meziantou.Framework.DnsServer.Tests/DnsServerIntegrationTests.cs;region=DnsServer_GettingStarted;format=md-fenced-code-block;dedent=true -->
```csharp
var builder = WebApplication.CreateBuilder();
builder.AddDnsServer(options =>
{
    options.AddUdpListener(port, IPAddress.Loopback);
    options.AddTcpListener(port, IPAddress.Loopback);
});

var app = builder.Build();
app.MapDnsHandler((context, cancellationToken) =>
{
    var response = context.CreateResponse();
    foreach (var question in context.Query.Questions)
    {
        if (question.Type is DnsQueryType.A && string.Equals(question.Name, "www.example.com", StringComparison.OrdinalIgnoreCase))
        {
            response.Answers.Add(new DnsResourceRecord
            {
                Name = question.Name,
                Type = DnsQueryType.A,
                Class = DnsQueryClass.IN,
                TimeToLive = 300,
                Data = new DnsARecordData { Address = IPAddress.Parse("192.0.2.10") },
            });
        }
    }

    return ValueTask.FromResult(response);
});
```
<!-- endref -->

- The types are in the [Meziantou.Framework.DnsServer.Hosting](cref:Meziantou.Framework.DnsServer.Hosting), [Meziantou.Framework.DnsServer.Handler](cref:Meziantou.Framework.DnsServer.Handler),
  [Meziantou.Framework.DnsServer.Protocol](cref:Meziantou.Framework.DnsServer.Protocol) and [Meziantou.Framework.DnsServer.Protocol.Records](cref:Meziantou.Framework.DnsServer.Protocol.Records) namespaces.
- `CreateResponse` copies the ID, the question and the flags of the query, so the handler only adds the records.
- Run the application with `app.Run()` or `await app.StartAsync()`. See [DnsServer](04-DnsServer/01-Overview.md).

## Use the client and the server in the same file

Both packages define types with the same names (`DnsQueryType`, `DnsQueryClass`, `DnsResponseCode`, `DnsQuestion`,
`DnsEdnsOptions`, `DnsProtocolException`), in different namespaces. A file that imports both sets of namespaces gets
ambiguous references: import one of them, and refer to the other through an alias, such as
`using ServerProtocol = Meziantou.Framework.DnsServer.Protocol;`.

## Next steps

- [DnsClient](03-DnsClient/01-Overview.md): transports, record types, DNSSEC, errors.
- [DnsServer](04-DnsServer/01-Overview.md): listeners, handlers, serving a zone.
- [DnsFilter](05-DnsFilter/01-Overview.md): block lists and filtering rules.
