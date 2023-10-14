using System.Collections;
using System.Collections.Immutable;

namespace Meziantou.Framework.Diagnostics.ContextSnapshot;

public sealed class ContextSnapshotBuilder
{
    private readonly Dictionary<string, object?> _contextSnapshot = new(StringComparer.Ordinal);

    public ContextSnapshotBuilder AddValue(string key, object? value)
    {
        _contextSnapshot[key] = value;
        return this;
    }

    public IReadOnlyDictionary<string, object?> BuildSnapshot()
    {
        return _contextSnapshot.OrderBy(kvp => kvp.Key, StringComparer.Ordinal).ToImmutableSortedDictionary();
    }

    public ContextSnapshotBuilder AddDefault()
    {
        AddEnvironmentVariables(EnvironmentVariableTarget.Process);
        AddEnvironmentVariables(EnvironmentVariableTarget.User);
        AddEnvironmentVariables(EnvironmentVariableTarget.Machine);
        AddGarbageCollector();
        AddDrives();
        AddSpecialFolderPaths();
        AddMachineName();
        AddConsole();
        AddAssemblyLoadContexts();
        AddThreadPool();
        AddCulture();
        AddCurrentDirectory();
        AddNewLine();
        AddUser();
        AddOperatingSystem();
        AddLocalTimeZone();
        AddHypervisor();
        AddPowerManagement();
        AddSecurityProviders();
        AddProcessorCount();
        AddCpu();
        AddCurrentProcess();
        return this;
    }

    public ContextSnapshotBuilder AddCurrentProcess() => AddValue("Process", new CurrentProcessSnapshot());
    public ContextSnapshotBuilder AddCpu() => AddValue("CPU", CpuSnapshot.Get());
    public ContextSnapshotBuilder AddProcessorCount() => AddValue("ProcessorCount", Environment.ProcessorCount);
    public ContextSnapshotBuilder AddSecurityProviders() => AddValue("Antivirus", new SecurityProvidersSnapshot());
    public ContextSnapshotBuilder AddPowerManagement() => AddValue("PowerManagement", PowerManagementSnapshot.Get());
    public ContextSnapshotBuilder AddHypervisor() => AddValue("Hypervisor", HypervisorSnapshot.Get());
    public ContextSnapshotBuilder AddLocalTimeZone() => AddValue("LocalTimeZone", TimeZoneSnapshot.Get());
    public ContextSnapshotBuilder AddOperatingSystem() => AddValue("OperatingSystem", new OperatingSystemSnapshot());
    public ContextSnapshotBuilder AddDotnetRuntime() => AddValue("Dotnet", new DotnetRuntimeSnapshot());
    public ContextSnapshotBuilder AddUser() => AddValue("User", new UserSnapshot());
    public ContextSnapshotBuilder AddCurrentDirectory() => AddValue("CurrentDirectory", Environment.CurrentDirectory);
    public ContextSnapshotBuilder AddNewLine() => AddValue("NewLine", Environment.NewLine);
    public ContextSnapshotBuilder AddMachineName() => AddValue("MachineName", Environment.MachineName);
    public ContextSnapshotBuilder AddGarbageCollector() => AddValue("GC", new GarbageCollectorSnapshot());
    public ContextSnapshotBuilder AddDrives() => AddValue("Drives", DriveSnapshot.Get());
    public ContextSnapshotBuilder AddSpecialFolderPaths() => AddValue("SpecialFolders", new SpecialFolderSnapshot());
    public ContextSnapshotBuilder AddConsole() => AddValue("Console", new ConsoleSnapshot());
    public ContextSnapshotBuilder AddAssemblyLoadContexts() => AddValue("AssemblyLoadContexts", AssemblyLoadContextSnapshot.Get());
    public ContextSnapshotBuilder AddThreadPool() => AddValue("ThreadPool", new ThreadPoolSnapshot());
    public ContextSnapshotBuilder AddCulture() => AddValue("Culture", new CultureSnapshot());

    public void AddEnvironmentVariables(EnvironmentVariableTarget target = EnvironmentVariableTarget.Process)
    {
        AddValue("EnvironmentVariables." + target, GetEnvironmentVariables(target));

        static ImmutableSortedDictionary<string, object> GetEnvironmentVariables(EnvironmentVariableTarget target)
        {
            return Environment.GetEnvironmentVariables(target).Cast<DictionaryEntry>().Select(item => Parse(item)).ToImmutableSortedDictionary();

            static KeyValuePair<string, object> Parse(DictionaryEntry entry)
            {
                var key = (string)entry.Key;
                if (string.Equals(key, "PATH", StringComparison.OrdinalIgnoreCase))
                    return KeyValuePair.Create<string, object>(key, ((string)entry.Value).Split(';').ToImmutableArray());

                return KeyValuePair.Create<string, object>(key, entry.Value);
            }
        }
    }
}
