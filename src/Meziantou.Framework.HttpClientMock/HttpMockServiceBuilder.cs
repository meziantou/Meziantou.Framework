namespace Meziantou.Framework;

public sealed class HttpMockServiceBuilder
{
    internal MockHttpMessageHandlerBuilder Builder { get; } = new();

    public HttpMockServiceBuilder AddHttpClientMock(HttpClientMock mock)
    {
        Builder.AddMock(mock);
        return this;
    }

    public HttpMockServiceBuilder AddHttpClientMock(string name, HttpClientMock mock)
    {
        Builder.AddMock(name, mock);
        return this;
    }

    public HttpMockServiceBuilder AddHttpClientMock<T>(HttpClientMock mock)
    {
        Builder.AddMock<T>(mock);
        return this;
    }
}
