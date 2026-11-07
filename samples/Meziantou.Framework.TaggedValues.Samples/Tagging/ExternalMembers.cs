using System.Diagnostics;

#region TaggedValues_AssemblyAttribute
[assembly: ValueTag(typeof(Process), nameof(Process.Id), "ProcessId")]
#endregion

namespace Sample.Tagging;

public static class ExternalMembers
{
    #region TaggedValues_AssemblyAttributeUsage
    public static void Stop([ValueTag("ProcessId")] int processId) { }

    public static void M(Process process, [ValueTag("ThreadId")] int threadId)
    {
        Stop(process.Id); // ok, Process.Id is a ProcessId
        Stop(threadId);   // MFTV0002
    }
    #endregion
}
