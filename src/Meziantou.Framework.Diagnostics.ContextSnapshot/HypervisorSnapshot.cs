using System.Management;
using Meziantou.Framework.Diagnostics.ContextSnapshot.Internals;

namespace Meziantou.Framework.Diagnostics.ContextSnapshot;

/// <summary>Represents a snapshot of the hypervisor when running in a virtualized environment (Windows only).</summary>
public sealed class HypervisorSnapshot
{
    private HypervisorSnapshot(string hypervisor)
    {
        Hypervisor = hypervisor;
    }

    public string Hypervisor { get; }

    internal static HypervisorSnapshot? Get()
    {
        var hypervisor = Utils.SafeGet(FindHypervisor);
        if (hypervisor is null)
            return null;

        return new(hypervisor);
    }

    private static string? FindHypervisor()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        try
        {
            using var searcher = new ManagementObjectSearcher("Select * from Win32_ComputerSystem");
            using var items = searcher.Get();
            foreach (var item in items)
            {
                var manufacturer = item["Manufacturer"]?.ToString();
                var model = item["Model"]?.ToString();

                if (ContainsIgnoreCase(manufacturer, "microsoft") && ContainsIgnoreCase(model, "virtual"))
                    return "Hyper-V";

                if (ContainsIgnoreCase(model, "virtualbox"))
                    return "VirtualBox";

                if (ContainsIgnoreCase(model, "vmware"))
                    return "VMWare";

                static bool ContainsIgnoreCase(string? value, string substring) => value is not null && value.Contains(substring, StringComparison.OrdinalIgnoreCase);
            }
        }
        catch
        {
        }

        return null;
    }
}
