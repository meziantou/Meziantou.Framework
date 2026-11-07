namespace Meziantou.Framework.TemporaryContainers.Internals;

/// <summary>An image as <c>container image list --format json</c> reports it. The labels of an image are in the configuration of each of its platform variants.</summary>
internal sealed class AppleImageDto
{
    public string? Id { get; set; }
    public AppleImageConfigurationDto? Configuration { get; set; }
    public List<AppleImageVariantDto>? Variants { get; set; }
}
