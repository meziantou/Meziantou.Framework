namespace Meziantou.Framework.Diagnostics.ContextSnapshot.Internals;

/// <summary>
/// CPU information from output of the `wmic cpu get Name, NumberOfCores, NumberOfLogicalProcessors /Format:List` command.
/// Windows only.
/// </summary>
internal static class WmicCpuInfoProvider
{
    internal static readonly Lazy<CpuInfo> WmicCpuInfo = new(Load);

    private const string DefaultWmicPath = @"C:\Windows\System32\wbem\WMIC.exe";

    private static CpuInfo? Load()
    {
        if (OperatingSystem.IsWindows())
        {
            const string ArgList = $"{WmicCpuInfoKeyNames.Name}, {WmicCpuInfoKeyNames.NumberOfCores}, " +
                                   $"{WmicCpuInfoKeyNames.NumberOfLogicalProcessors}, {WmicCpuInfoKeyNames.MaxClockSpeed}";
            var wmicPath = File.Exists(DefaultWmicPath) ? DefaultWmicPath : "wmic";
            var content = ProcessHelper.RunAndReadOutput(wmicPath, $"cpu get {ArgList} /Format:List");
            return WmicCpuInfoParser.ParseOutput(content);
        }

        return null;
    }
}
