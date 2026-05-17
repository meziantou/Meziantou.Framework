namespace Meziantou.Framework.Http.ServerSideRequestForgery;

public static class SocketHttpHandlerServerSideRequestForgeryExtensions
{
    public static SocketsHttpHandler ConfigureSsrf(this SocketsHttpHandler handler, ServerSideRequestForgeryOptions options)
    {
        return ConfigureSsrf(handler, options, SystemDnsIpAddressResolver.Instance);
    }

    internal static SocketsHttpHandler ConfigureSsrf(this SocketsHttpHandler handler, ServerSideRequestForgeryOptions options, IDnsIpAddressResolver dnsIpAddressResolver)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(options);

        ServerSideRequestForgeryConnectPipeline.Configure(handler, options, dnsIpAddressResolver);
        return handler;
    }
}
