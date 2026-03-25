namespace Meziantou.Framework.DnsFilter;

internal sealed class DnsFilterTagSpec
{
    public IReadOnlyList<string>? IncludedTags { get; init; }
    public IReadOnlyList<string>? ExcludedTags { get; init; }
}
