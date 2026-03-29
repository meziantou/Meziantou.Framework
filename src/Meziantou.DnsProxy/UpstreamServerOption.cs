namespace Meziantou.DnsProxy;

internal sealed class UpstreamServerOption
{
    public string Name { get; set; } = "";

    public string Endpoint { get; set; } = "";

    public string Protocol { get; set; } = "Https";

    public bool UseHttp3 { get; set; } = true;
}
