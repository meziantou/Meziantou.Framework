namespace Meziantou.Framework.Diagnostics.ContextSnapshot.Internals;

/// <summary>
/// CPU information from output of the `sysctl -a` command.
/// MacOSX only.
/// </summary>
internal static class SysctlCpuInfoProvider
{
    internal static readonly Lazy<CpuInfo> SysctlCpuInfo = new Lazy<CpuInfo>(Load);

    private static CpuInfo? Load()
    {
        if (OperatingSystem.IsMacOS())
        {
            var content = ProcessHelper.RunAndReadOutput("sysctl", "-a");
            return SysctlCpuInfoParser.ParseOutput(content);
        }
        return null;
    }
}
