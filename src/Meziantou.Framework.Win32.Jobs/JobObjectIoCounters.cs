using System.Runtime.InteropServices;

namespace Meziantou.Framework.Win32;

/// <summary>Contains I/O accounting information for a job object.</summary>
[StructLayout(LayoutKind.Sequential)]
public sealed record JobObjectIoCounters
{
    /// <summary>The number of read operations performed.</summary>
    public required ulong ReadOperationCount { get; init; }

    /// <summary>The number of write operations performed.</summary>
    public required ulong WriteOperationCount { get; init; }

    /// <summary>The number of I/O operations performed, other than read and write operations.</summary>
    public required ulong OtherOperationCount { get; init; }

    /// <summary>The number of bytes read.</summary>
    public required ulong ReadTransferCount { get; init; }

    /// <summary>The number of bytes written.</summary>
    public required ulong WriteTransferCount { get; init; }

    /// <summary>The number of bytes transferred during operations other than read and write operations.</summary>
    public required ulong OtherTransferCount { get; init; }
}