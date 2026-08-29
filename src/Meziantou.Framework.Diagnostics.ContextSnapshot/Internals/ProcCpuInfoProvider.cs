namespace Meziantou.Framework.Diagnostics.ContextSnapshot.Internals;

/// <summary>
/// CPU information from the <c>/proc/cpuinfo</c> and <c>/sys/devices/system/cpu</c> pseudo-files.
/// Linux only.
/// </summary>
internal static class ProcCpuInfoProvider
{
    internal static readonly Lazy<CpuInfo?> ProcCpuInfo = new(Load);

    private static CpuInfo? Load()
    {
        if (OperatingSystem.IsLinux())
        {
            var content = ReadFile("/proc/cpuinfo") ?? "";
            content += GetCpuFrequencies();
            return ProcCpuInfoParser.ParseOutput(content);
        }

        return null;
    }

    private static string? ReadFile(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string GetCpuFrequencies()
    {
        // cpuinfo_min_freq / cpuinfo_max_freq are expressed in kHz. They are the same values lscpu reports
        // as "CPU min MHz" / "CPU max MHz", read directly so no shell or util-linux is required.
        var output = new StringBuilder();
        AppendFrequency(output, ProcCpuInfoKeyNames.MinFrequency, "/sys/devices/system/cpu/cpu0/cpufreq/cpuinfo_min_freq");
        AppendFrequency(output, ProcCpuInfoKeyNames.MaxFrequency, "/sys/devices/system/cpu/cpu0/cpufreq/cpuinfo_max_freq");
        return output.ToString();

        static void AppendFrequency(StringBuilder output, string keyName, string path)
        {
            var value = ReadFile(path);
            if (value is null)
                return;

            if (!long.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var kiloHertz) || kiloHertz <= 0)
                return;

            var frequency = new Frequency(kiloHertz, FrequencyUnit.KHz);
            output.Append(CultureInfo.InvariantCulture, $"\n{keyName}\t:{frequency.ToMHz()}");
        }
    }
}
