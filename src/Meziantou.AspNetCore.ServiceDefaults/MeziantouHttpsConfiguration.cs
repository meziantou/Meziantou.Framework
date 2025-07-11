namespace Meziantou.AspNetCore.ServiceDefaults;

public sealed class MeziantouHttpsConfiguration
{
    public bool Enabled { get; set; } = true;
    public bool HstsEnabled { get; set; } = true;
}
