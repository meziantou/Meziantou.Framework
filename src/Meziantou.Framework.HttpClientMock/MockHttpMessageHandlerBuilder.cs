using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;

namespace Meziantou.Framework;

internal sealed class MockHttpMessageHandlerBuilder : HttpMessageHandlerBuilder, IDisposable
{
    private readonly Dictionary<string, HttpMessageHandler> _handlers = new(StringComparer.Ordinal);
    private HttpMessageHandler? _primaryHandler;
    private HttpMessageHandler? _ownedPrimaryHandler;

    public bool ThrowOnUnknownHttpClient { get; set; }

    public void AddMock(HttpClientMock mock)
    {
        ArgumentNullException.ThrowIfNull(mock);

        _handlers[""] = mock.CreateHttpMessageHandler();
    }

    public void AddMock(string name, HttpClientMock mock)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(mock);

        _handlers[name] = mock.CreateHttpMessageHandler();
    }

    public void AddMock<T>(HttpClientMock mock)
    {
        ArgumentNullException.ThrowIfNull(mock);

        // Must match the name computed by AddHttpClient<T>(), which is not typeof(T).Name for generic types
        _handlers[TypeNameHelper.GetTypeDisplayName(typeof(T), fullName: false)] = mock.CreateHttpMessageHandler();
    }

    public override string Name { get; set; } = "";

    public override HttpMessageHandler PrimaryHandler
    {
        // Only created when no mock matches, so the common case doesn't allocate a real handler
        get => _primaryHandler ??= _ownedPrimaryHandler = new HttpClientHandler();
        set => _primaryHandler = value;
    }

    public override IList<DelegatingHandler> AdditionalHandlers { get; } = new List<DelegatingHandler>();

    public override HttpMessageHandler Build()
    {
        if (_handlers.TryGetValue(Name, out var handler))
            return CreateHandlerPipeline(handler, AdditionalHandlers);

        if (ThrowOnUnknownHttpClient)
            throw new InvalidOperationException($"No HttpClientMock is registered for the HttpClient '{FormatName(Name)}', so the request would be sent to the network. Register a mock for it using AddHttpClientMock, or stop calling ThrowOnUnknownHttpClient to allow real HTTP requests. Registered mocks: {FormatRegisteredNames()}");

        return CreateHandlerPipeline(PrimaryHandler, AdditionalHandlers);
    }

    private string FormatRegisteredNames()
    {
        if (_handlers.Count == 0)
            return "<none>";

        return string.Join(", ", _handlers.Keys.Order(StringComparer.Ordinal).Select(FormatName));
    }

    private static string FormatName(string name) => name.Length == 0 ? "<default>" : name;

    public void Dispose()
    {
        foreach (var handler in _handlers.Values)
        {
            handler.Dispose();
        }

        _ownedPrimaryHandler?.Dispose();
    }
}
