using System.Text.Json;

namespace Meziantou.AspNetCore.ServiceDefaults;

public sealed class MeziantouServiceDefaultsOptions
{
    internal bool MapCalled { get; set; }

    public MeziantouHttpsConfiguration Https { get; } = new();
    public MeziantouOpenApiConfiguration OpenApi { get; } = new();
    public MeziantouOpenTelemetryConfiguration OpenTelemetry { get; } = new();
    public Action<JsonSerializerOptions>? ConfigureJsonOptions { get; set; }
    public MeziantouAntiForgeryConfiguration AntiForgery { get; } = new();
    public MeziantouStaticAssetsConfiguration StaticAssets { get; } = new();
    public MeziantouForwardedHeadersConfiguration ForwardedHeaders { get; } = new();
}
