namespace Meziantou.Framework;

/// <summary>
/// A builder for registering <see cref="HttpClientMock"/> instances with dependency injection.
/// </summary>
public sealed class HttpMockServiceBuilder
{
    internal MockHttpMessageHandlerBuilder Builder { get; } = new();

    /// <summary>Adds an <see cref="HttpClientMock"/> as the default HTTP client.</summary>
    /// <param name="mock">The mock to use for the default HTTP client.</param>
    /// <returns>The builder for chaining additional calls.</returns>
    public HttpMockServiceBuilder AddHttpClientMock(HttpClientMock mock)
    {
        Builder.AddMock(mock);
        return this;
    }

    /// <summary>Adds a named <see cref="HttpClientMock"/> for a specific HTTP client.</summary>
    /// <param name="name">The name of the HTTP client to mock.</param>
    /// <param name="mock">The mock to use for the named HTTP client.</param>
    /// <returns>The builder for chaining additional calls.</returns>
    public HttpMockServiceBuilder AddHttpClientMock(string name, HttpClientMock mock)
    {
        Builder.AddMock(name, mock);
        return this;
    }

    /// <summary>Adds a typed <see cref="HttpClientMock"/> for a specific HTTP client type.</summary>
    /// <typeparam name="T">The type of the HTTP client to mock.</typeparam>
    /// <param name="mock">The mock to use for the typed HTTP client.</param>
    /// <returns>The builder for chaining additional calls.</returns>
    public HttpMockServiceBuilder AddHttpClientMock<T>(HttpClientMock mock)
    {
        Builder.AddMock<T>(mock);
        return this;
    }
}
