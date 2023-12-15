using System.Collections.Concurrent;
using Microsoft.Extensions.Http;

namespace Meziantou.Framework;

internal sealed class MockHttpMessageHandlerBuilder : HttpMessageHandlerBuilder, IDisposable
{
    private readonly ConcurrentDictionary<string, HttpMessageHandler> _handlers = new(StringComparer.Ordinal);

    public void AddMock(HttpClientMock mock)
    {
        _handlers[""] = mock.CreateHttpMessageHandler();
    }

    public void AddMock(string name, HttpClientMock mock)
    {
        ArgumentNullException.ThrowIfNull(name);

        _handlers[name] = mock.CreateHttpMessageHandler();
    }

    public void AddMock<T>(HttpClientMock mock)
    {
        _handlers[typeof(T).Name] = mock.CreateHttpMessageHandler();
    }

    public override string Name { get; set; }
    public override HttpMessageHandler PrimaryHandler { get; set; } = new HttpClientHandler();
    public override IList<DelegatingHandler> AdditionalHandlers { get; } = new List<DelegatingHandler>();

    public override HttpMessageHandler Build()
    {
        if (Name is not null && _handlers.TryGetValue(Name, out var handler))
            return CreateHandlerPipeline(handler, AdditionalHandlers);

        return CreateHandlerPipeline(PrimaryHandler, AdditionalHandlers);
    }

    public void Dispose()
    {
        foreach (var handler in _handlers.Values)
        {
            handler.Dispose();
        }
    }
}
