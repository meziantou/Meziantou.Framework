---
title: Handling rejections
description: The exceptions a rejected request throws, the warnings it logs and the metric it increments.
---

## Exceptions

A request rejected when its connection is opened throws an `HttpRequestException` whose `InnerException` is a
`ServerSideRequestForgeryException`:

<!-- ref:../../../tests/Meziantou.Framework.Http.ServerSideRequestForgery.Tests/ServerSideRequestForgeryConnectPipelineTests.cs;region=ServerSideRequestForgery_CatchRejection;format=md-fenced-code-block;dedent=true -->
```csharp
ServerSideRequestForgeryException? rejection = null;
try
{
    // 127.0.0.1 is in the default UnsafeIpNetworks
    using var response = await httpClient.GetAsync(new Uri("https://127.0.0.1/"), cancellationToken);
}
catch (HttpRequestException ex) when (ex.InnerException is ServerSideRequestForgeryException ssrf)
{
    rejection = ssrf;
}

Assert.NotNull(rejection);
```
<!-- endref -->

A request that may use HTTP/3 is rejected by `ServerSideRequestForgeryClientHandler` before it reaches the
`SocketsHttpHandler`, so its `ServerSideRequestForgeryException` is not wrapped:

<!-- ref:../../../tests/Meziantou.Framework.Http.ServerSideRequestForgery.Tests/ServerSideRequestForgeryClientHandlerTests.cs;region=ServerSideRequestForgery_RejectHttp3;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new ServerSideRequestForgeryOptions();
var handler = new ServerSideRequestForgeryClientHandler(new SocketsHttpHandler { UseProxy = false }, options);
using var httpClient = new HttpClient(handler, disposeHandler: true);

using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("https://example.com/"))
{
    Version = HttpVersion.Version30,
};

// Rejected before the request reaches SocketsHttpHandler, so the exception is not wrapped
await Assert.ThrowsAsync<ServerSideRequestForgeryException>(() => httpClient.SendAsync(request));
```
<!-- endref -->

Catch both when the URL comes from a user, and report the URL as not allowed. Other `HttpRequestException`s, such as
a host that does not exist or does not answer, are ordinary network errors.

## Reasons

| Reason | Exception message |
| --- | --- |
| `unsafe_scheme` | The URI scheme '…' is not allowed. |
| `all_resolved_addresses_unsafe` | No safe IP addresses were found after validation. |
| `mixed_addresses_disallowed` | The hostname resolved to a mix of safe and unsafe IP addresses. |
| `resolution_strategy_failure` | The message of the strategy, such as "No address found for address family '…'." |
| `selected_address_not_validated` | The resolution strategy selected an address that was not part of the validated safe set. |
| `host_mismatch` | The host resolved for the connection does not match the request URI authority. |
| `proxy_connection` | The connection targets a proxy. … |

The reason is the tag of the metric below; the HTTP/3 rejection has none.

## Logging

Set `Logger` to log every rejection as a warning:

<!-- ref:../../../tests/Meziantou.Framework.Http.ServerSideRequestForgery.Tests/ServerSideRequestForgeryConnectPipelineTests.cs;region=ServerSideRequestForgery_Logging;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new ServerSideRequestForgeryOptions
{
    // Every rejection is logged as a warning
    Logger = logger,
};
```
<!-- endref -->

A rejected request to `https://127.0.0.1/?token=secret` logs "Rejected request to https://127.0.0.1:443. All resolved
IP addresses were unsafe." The messages give the origin of the request (scheme, host and port) only: the path, the query
string and credentials in the URL are never logged. The HTTP/3 rejection is not logged.

## Metrics

Each rejection adds one to the `meziantou.framework.http.server_side_request_forgery.rejected_requests.total` counter of
the `Meziantou.Framework.Http.ServerSideRequestForgery` meter, with a `reason` tag from the table above. Any metrics
collector that subscribes to the meter by name receives it; this one uses a `MeterListener`:

<!-- ref:../../../tests/Meziantou.Framework.Http.ServerSideRequestForgery.Tests/ServerSideRequestForgeryConnectPipelineTests.cs;region=ServerSideRequestForgery_Metrics;format=md-fenced-code-block;dedent=true -->
```csharp
var reasons = new ConcurrentQueue<string?>();
using var listener = new MeterListener();
listener.InstrumentPublished = (instrument, meterListener) =>
{
    if (instrument.Meter.Name is "Meziantou.Framework.Http.ServerSideRequestForgery")
    {
        meterListener.EnableMeasurementEvents(instrument);
    }
};
listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
{
    foreach (var tag in tags)
    {
        if (tag.Key is "reason")
        {
            reasons.Enqueue(tag.Value as string);
        }
    }
});
listener.Start();

var options = new ServerSideRequestForgeryOptions();
using var httpClient = new HttpClient(new ServerSideRequestForgeryClientHandler(new SocketsHttpHandler { UseProxy = false }, options), disposeHandler: true);
await Assert.ThrowsAsync<HttpRequestException>(() => httpClient.GetAsync(new Uri("https://127.0.0.1/")));

Assert.Contains("all_resolved_addresses_unsafe", reasons);
```
<!-- endref -->

A connection that fails after its address was validated is not a rejection: it is not logged or counted.
