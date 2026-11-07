using System.Runtime.Versioning;

namespace Meziantou.Framework.Unix.ControlGroups.Samples;

// Examples of docs/UnixControlGroups that depend on the hardware of the machine (block devices, CPUs, memory nodes),
// so they are compiled but not run. The other examples of the documentation are tests of ControlGroups.Tests.
[SupportedOSPlatform("linux")]
public static class DocumentationExamples
{
    public static void IoLimits()
    {
        #region CGroup2_IoLimits
        var parent = CGroup2.Root.CreateOrGetChild("myapp");
        parent.SetControllers("io");
        var worker = parent.CreateOrGetChild("worker");

        // A relative weight (1 to 10000, 100 by default), for every device or for one device
        worker.SetDefaultIoWeight(200);
        worker.SetIoWeight(major: 8, minor: 0, weight: 500); // 8:0 is /dev/sda

        // Bandwidth and IOPS limits of a device: 10 MB/s read, 5 MB/s write, 1000 write operations per second
        worker.SetIoMax(major: 8, minor: 0, readBytesPerSecond: 10 * 1024 * 1024, writeBytesPerSecond: 5 * 1024 * 1024, writeIopsPerSecond: 1000);

        // Remove the limits of the device
        worker.RemoveIoMax(major: 8, minor: 0);
        #endregion
    }

    public static void Cpuset()
    {
        #region CGroup2_Cpuset
        var parent = CGroup2.Root.CreateOrGetChild("myapp");
        parent.SetControllers("cpuset");
        var worker = parent.CreateOrGetChild("worker");

        // Run on CPUs 2 and 3, and allocate memory on NUMA node 0
        worker.SetCpusetCpus(2, 3);
        worker.SetCpusetMems(0);

        // The same with the kernel list format
        worker.SetCpusetCpusRaw("2-3");

        // The CPUs the cgroup can actually use, which the parent can restrict further
        var effectiveCpus = worker.GetCpusetCpusEffective().GetValueOrDefault([]);
        Console.WriteLine($"Effective CPUs: {string.Join(", ", effectiveCpus)}");
        #endregion
    }
}
