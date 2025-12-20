namespace Meziantou.AspNetCore.ServiceDefaults;

public sealed class MeziantouCachingConfiguration
{
    public bool SetNoCacheWhenMissingCacheHeaders { get; set; } = true;
}
