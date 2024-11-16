using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Diagnostics.ContextSnapshot.Internals;

/// <summary>
/// CPU information from output of the `cat /proc/info` command.
/// Linux only.
/// </summary>
internal static class ProcCpuInfoProvider
{
    internal static readonly Lazy<CpuInfo> ProcCpuInfo = new(Load);

    private static CpuInfo? Load()
    {
        if (OperatingSystem.IsLinux())
        {
            var content = ProcessHelper.RunAndReadOutput("cat", "/proc/cpuinfo") ?? "";
            var output = GetCpuSpeed() ?? "";
            content += output;
            return ProcCpuInfoParser.ParseOutput(content);
        }

        return null;
    }

    private static string? GetCpuSpeed()
    {
        try
        {
            var output = ProcessHelper.RunAndReadOutput("/bin/bash", "-c \"lscpu | grep MHz\"")?
                .Split('\n')
                .SelectMany(x => x.Split(':'))
                .ToArray();

            return ParseCpuFrequencies(output);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? ParseCpuFrequencies(string[]? input)
    {
        // Example of output we trying to parse:
        //
        // CPU MHz: 949.154
        // CPU max MHz: 3200,0000
        // CPU min MHz: 800,0000

        if (input is null)
            return null;

        var output = new StringBuilder();
        for (var i = 0; i + 1 < input.Length; i += 2)
        {
            var name = input[i].Trim();
            var value = input[i + 1].Trim();

            if (string.Equals(name, "CPU min MHz", StringComparison.OrdinalIgnoreCase))
            {
                if (Frequency.TryParseMHz(value.Replace(',', '.'), out var minFrequency))
                {
                    output.Append(CultureInfo.InvariantCulture, $"\n{ProcCpuInfoKeyNames.MinFrequency}\t:{minFrequency.ToMHz()}");
                }
            }

            if (string.Equals(name, "CPU max MHz", StringComparison.OrdinalIgnoreCase))
            {
                if (Frequency.TryParseMHz(value.Replace(',', '.'), out var maxFrequency))
                {
                    output.Append(CultureInfo.InvariantCulture, $"\n{ProcCpuInfoKeyNames.MaxFrequency}\t:{maxFrequency.ToMHz()}");
                }
            }
        }

        return output.ToString();
    }
}
