using System.Runtime.InteropServices;

namespace Meziantou.Framework.Win32;

/// <summary>Contains basic accounting information for a job object.</summary>
public sealed record JobObjectBasicAccountingInformation
{
    /// <summary>The total amount of user-mode execution time for all active processes associated with the job, as well as all terminated processes no longer associated with the job.</summary>
    public required TimeSpan TotalUserTime { get; init; }

    /// <summary>The total amount of kernel-mode execution time for all active processes associated with the job, as well as all terminated processes no longer associated with the job.</summary>
    public required TimeSpan TotalKernelTime { get; init; }

    /// <summary>
    /// <para>The total amount of user-mode execution time for all active processes associated with the job (as well as all terminated processes no longer associated with the job) since the last call that set a per-job user-mode time limit.</para>
    /// <para>This member is set to 0 on creation of the job, and each time a per-job user-mode time limit is established.</para>
    /// <para><see href="https://learn.microsoft.com/windows/win32/api/winnt/ns-winnt-jobobject_basic_accounting_information#members">Read more on learn.microsoft.com</see>.</para>
    /// </summary>
    public required TimeSpan ThisPeriodTotalUserTime { get; init; }

    /// <summary>
    /// <para>The total amount of kernel-mode execution time for all active processes associated with the job (as well as all terminated processes no longer associated with the job) since the last call that set a per-job kernel-mode time limit.</para>
    /// <para>This member is set to zero on creation of the job, and each time a per-job kernel-mode time limit is established.</para>
    /// <para><see href="https://learn.microsoft.com/windows/win32/api/winnt/ns-winnt-jobobject_basic_accounting_information#members">Read more on learn.microsoft.com</see>.</para>
    /// </summary>
    public required TimeSpan ThisPeriodTotalKernelTime { get; init; }

    /// <summary>The total number of page faults encountered by all active processes associated with the job, as well as all terminated processes no longer associated with the job.</summary>
    public required uint TotalPageFaultCount { get; init; }

    /// <summary>The total number of processes associated with the job during its lifetime, including those that have terminated. For example, when a process is associated with a job, but the association fails because of a limit violation, this value is incremented.</summary>
    public required uint TotalProcesses { get; init; }

    /// <summary>The total number of processes currently associated with the job. When a process is associated with a job, but the association fails because of a limit violation, this value is temporarily incremented. When the terminated process exits and all references to the process are released, this value is decremented.</summary>
    public required uint ActiveProcesses { get; init; }

    /// <summary>The total number of processes terminated because of a limit violation.</summary>
    public required uint TotalTerminatedProcesses { get; init; }
}