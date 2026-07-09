using System.Text.Json;

namespace Meziantou.Framework.TemporaryContainers.Internals;

internal sealed class AppleInspectResult
{
    public string? Id { get; set; }
    public JsonElement Status { get; set; }
    public List<AppleNetworkDto>? Networks { get; set; }
    public AppleConfigurationDto? Configuration { get; set; }
}
