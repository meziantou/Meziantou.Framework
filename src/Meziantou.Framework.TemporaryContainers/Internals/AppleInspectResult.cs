namespace Meziantou.Framework.TemporaryContainers.Internals;

internal sealed class AppleInspectResult
{
    public string? Status { get; set; }
    public List<AppleNetworkDto>? Networks { get; set; }
    public AppleConfigurationDto? Configuration { get; set; }
}
