namespace Meziantou.Framework.Diagnostics;

/// <summary>Specifies the content of a memory dump created by <see cref="MemoryDump"/>.</summary>
public enum MemoryDumpType
{
    /// <summary>A small dump containing the module lists, thread lists, exception information, and all stacks.</summary>
    Normal,

    /// <summary>A dump containing the module lists, thread lists, all stacks, exception information, handle information, and all memory except for mapped images.</summary>
    WithHeap,

    /// <summary>A dump similar to <see cref="Normal"/> with personally identifiable information, such as paths and passwords, removed.</summary>
    Triage,

    /// <summary>A dump containing all the memory of the process, including the module images.</summary>
    Full,
}
