using System.Runtime.Versioning;

namespace Meziantou.Framework.Diagnostics.Samples;

// The examples of src/Meziantou.Framework.Diagnostics.MemoryDump/readme.md. They write dumps of the current process,
// which take hundreds of megabytes, so they are compiled but not run. The tests of MemoryDump cover the same calls.
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
[SupportedOSPlatform("windows8.1")]
public static class ReadmeExamples
{
    public static async Task Usage(CancellationToken cancellationToken)
    {
        #region MemoryDump_Usage
        // Full dump (default)
        MemoryDump.Write("app.dmp");

        // Smaller dump containing the managed heap
        await MemoryDump.WriteAsync("app.dmp", MemoryDumpType.WithHeap, cancellationToken);
        #endregion
    }

    public static void DumpOnThreadPoolStarvation(TimeSpan queueDelay, string dumpDirectory)
    {
        #region MemoryDump_DumpOnProblem
        if (queueDelay > TimeSpan.FromSeconds(5))
        {
            MemoryDump.Write(Path.Combine(dumpDirectory, $"starvation-{DateTime.UtcNow:yyyyMMddHHmmss}.dmp"));
        }
        #endregion
    }
}
