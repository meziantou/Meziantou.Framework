using Microsoft.AspNetCore.HttpOverrides;

namespace Meziantou.AspNetCore.ServiceDefaults;

public sealed class MeziantouForwardedHeadersConfiguration
{
    public ForwardedHeaders ForwardedHeaders { get; set; } = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
}
