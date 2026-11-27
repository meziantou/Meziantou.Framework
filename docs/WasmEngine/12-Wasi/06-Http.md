---
title: HTTP
description: Let WASI 0.3 components send HTTP requests with wasi:http/client, and handle HTTP requests with components of the world wasi:http/service.
---

The components of WASI 0.3 send HTTP requests with `wasi:http/client`, and the components of the world
`wasi:http/service` handle HTTP requests: they export `wasi:http/handler`.

## The requests of the components

A component cannot send requests by default: they fail with `HTTP-request-denied`. Give it a client with
[WasiOptions.HttpClient](cref:Meziantou.Framework.WasmEngine.Wasi.WasiOptions.HttpClient):

- [WasiHostHttpClient](cref:Meziantou.Framework.WasmEngine.Wasi.WasiHostHttpClient) sends the requests over the
  network of the machine, with the rules of a
  [WasiHostHttpClientOptions](cref:Meziantou.Framework.WasmEngine.Wasi.WasiHostHttpClientOptions).
- [WasiInMemoryHttpClient](cref:Meziantou.Framework.WasmEngine.Wasi.WasiInMemoryHttpClient) gives the requests to a
  function or to an [HttpMessageHandler](https://learn.microsoft.com/dotnet/api/system.net.http.httpmessagehandler) of
  the process (such as a mock), without the network:

<!-- ref:../../../tests/Meziantou.Framework.WasmEngine.Wasi.Tests/WasiComponentContextTests.cs;region=WasiHttp_InMemoryClient;format=md-fenced-code-block;dedent=true -->
```csharp
// The requests of the component go to a function of the process, without the network
var client = new WasiInMemoryHttpClient((request, cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
{
    Content = new StringContent("Hello from " + request.RequestUri!.Host),
}));

var wasi = new WasiComponentContext(new WasiOptions { HttpClient = client });
```
<!-- endref -->

Both are [WasiHttpClient](cref:Meziantou.Framework.WasmEngine.Wasi.WasiHttpClient): derive from it to give the requests
to another client. WASI validates the requests before the client sees them (the method, the scheme, the authority, the
path, the headers): the headers of the connection (`connection`, `host`, `transfer-encoding`, `upgrade`...) are
forbidden. The body of a request is read as the client sends it, and the body of the response is given to the
component as the client receives it, with its trailers. A component can send a request that it handles, as a proxy:
the body is sent as it is received. The `first-byte-timeout` and `between-bytes-timeout` of the options of the request
apply with every client.

The rules of a [WasiHostHttpClient](cref:Meziantou.Framework.WasmEngine.Wasi.WasiHostHttpClient) deny everything by
default:

- [RequestCheck](cref:Meziantou.Framework.WasmEngine.Wasi.WasiHostHttpClientOptions.RequestCheck) decides each request
  (its method, its URI, its headers). [AllowHosts](cref:Meziantou.Framework.WasmEngine.Wasi.WasiHostHttpClientOptions.AllowHosts)
  allows the requests to some hosts, and [AllowAll](cref:Meziantou.Framework.WasmEngine.Wasi.WasiHostHttpClientOptions.AllowAll)
  every request.
- [AddressCheck](cref:Meziantou.Framework.WasmEngine.Wasi.WasiHostHttpClientOptions.AddressCheck) decides the IP
  addresses that the client connects to, once the host name is resolved: check them so that a name cannot lead the
  component to the machines of the local network.
- A connection times out after the `connect-timeout` of the options of the request, or after
  [ConnectTimeout](cref:Meziantou.Framework.WasmEngine.Wasi.WasiHostHttpClientOptions.ConnectTimeout).
- The client does not follow the redirections, does not decompress the contents, does not keep cookies and does not
  use a proxy: the component receives the responses of the servers as they are.

.NET does not send the trailers of the requests over HTTP/1.1: the requests of a component that sends trailers are
sent without them. A custom client reads them from the options of the request
([RequestTrailers](cref:Meziantou.Framework.WasmEngine.Wasi.WasiHttpClient.RequestTrailers)).

## Handle the requests with a component

[HandleHttpRequest](cref:Meziantou.Framework.WasmEngine.Wasi.WasiComponentContext.HandleHttpRequest) calls the handler
of a component of the world `wasi:http/service` with an
[HttpRequestMessage](https://learn.microsoft.com/dotnet/api/system.net.http.httprequestmessage), and returns its
response:

<!-- ref:../../../tests/Meziantou.Framework.WasmEngine.Wasi.Tests/WasiComponentContextTests.cs;region=WasiHttp_Service;format=md-fenced-code-block;dedent=true -->
```csharp
using var runtime = new WasmRuntime();

// A component of the world wasi:http/service: it exports wasi:http/handler
var component = runtime.CompileComponent(WasmRuntime.WatToWasm(text));
var wasi = new WasiComponentContext();
using var store = runtime.CreateStore();
var instance = store.InstantiateComponent(component, wasi.DefineImports());

// Each call handles a request on the thread of the store: a server of the host calls it for each request
using var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/items");
using var response = wasi.HandleHttpRequest(instance, request);
Assert.Equal(HttpStatusCode.Created, response.StatusCode);
```
<!-- endref -->

- The call runs on the current thread, as the thread of the store: a server of the host (Kestrel, `HttpListener`)
  handles the requests of a store one at a time, for instance from a queue.
- The body of the response is read before the method returns, with its trailers
  ([TrailingHeaders](https://learn.microsoft.com/dotnet/api/system.net.http.httpresponsemessage.trailingheaders)).
- An error of the handler is a response with the status 500. A trap throws a
  [WasmTrapException](cref:Meziantou.Framework.WasmEngine.WasmTrapException): the store cannot be used anymore.

## What is supported

- `wasi:http/types` (the fields, the requests, the responses, the options of the requests), `wasi:http/client`, and
  the calls of `wasi:http/handler`.
- WASI 0.3 only: the HTTP of WASI 0.2 is not supported.
