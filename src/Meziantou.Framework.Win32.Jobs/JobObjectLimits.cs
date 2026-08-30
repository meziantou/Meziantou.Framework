using Windows.Win32.System.JobObjects;

namespace Meziantou.Framework.Win32;

/// <summary>Defines a job object limits.</summary>
/// <remarks>
/// A limit applies only when its property is set to a non-<see langword="null"/> value. Leave a property
/// <see langword="null"/> - the default - to leave that limit alone.
/// </remarks>
/// <example>
/// <code>
/// var limits = new JobObjectLimits
/// {
///     Flags = JobObjectLimitFlags.KillOnJobClose,
///     ActiveProcessLimit = 10,
///     ProcessMemoryLimit = 100 * 1024 * 1024 // 100 MB
/// };
/// job.SetLimits(limits);
/// </code>
/// </example>
public sealed class JobObjectLimits
{
    /// <summary>Defines options for a job object.</summary>
    /// <value>The options for a job object.</value>
    public JobObjectLimitFlags Flags { get; set; }

    /// <summary>Gets or sets the per-process user-mode execution time limit, in 100-nanosecond ticks.</summary>
    /// <value>The per-process user-mode execution time limit, in 100-nanosecond ticks, or <see langword="null"/> to apply no limit.</value>
    public long? PerProcessUserTimeLimit { get; set; }

    /// <summary>Gets or sets the per-job user-mode execution time limit, in 100-nanosecond ticks.</summary>
    /// <value>The per-job user-mode execution time limit, in 100-nanosecond ticks, or <see langword="null"/> to apply no limit.</value>
    public long? PerJobUserTimeLimit { get; set; }

    /// <summary>Gets or sets the minimum working set size for each process associated with the job.</summary>
    /// <value>The minimum working set size for each process associated with the job, or <see langword="null"/> to apply no limit.</value>
    /// <remarks>Windows applies the working set limit only when both <see cref="MinimumWorkingSetSize"/> and <see cref="MaximumWorkingSetSize"/> are set.</remarks>
    public nuint? MinimumWorkingSetSize { get; set; }

    /// <summary>Gets or sets the maximum working set size for each process associated with the job.</summary>
    /// <value>The maximum working set size for each process associated with the job, or <see langword="null"/> to apply no limit.</value>
    /// <remarks>Windows applies the working set limit only when both <see cref="MinimumWorkingSetSize"/> and <see cref="MaximumWorkingSetSize"/> are set.</remarks>
    public nuint? MaximumWorkingSetSize { get; set; }

    /// <summary>Gets or sets the active process limit for the job.</summary>
    /// <value>The active process limit for the job, or <see langword="null"/> to apply no limit. A value of <c>0</c> is an explicit limit of zero processes, which makes the job reject every process.</value>
    public uint? ActiveProcessLimit { get; set; }

    /// <summary>Gets or sets the processor affinity for all processes associated with the job.</summary>
    /// <value>The processor affinity for all processes associated with the job, or <see langword="null"/> to apply no limit.</value>
    public nuint? Affinity { get; set; }

    /// <summary>Gets or sets priority class for all processes associated with the job.</summary>
    /// <value>The priority class for all processes associated with the job, or <see langword="null"/> to apply no limit.</value>
    public uint? PriorityClass { get; set; }

    /// <summary>Gets or sets scheduling class for all processes associated with the job.</summary>
    /// <value>The scheduling class for all processes associated with the job, or <see langword="null"/> to apply no limit.</value>
    public uint? SchedulingClass { get; set; }

    /// <summary>Gets or sets the limit for the virtual memory that can be committed by a process.</summary>
    /// <value>The limit for the virtual memory that can be committed by a process, or <see langword="null"/> to apply no limit.</value>
    public nuint? ProcessMemoryLimit { get; set; }

    /// <summary>Gets or sets limit for the virtual memory that can be committed for the job.</summary>
    /// <value>The limit for the virtual memory that can be committed for the job, or <see langword="null"/> to apply no limit.</value>
    public nuint? JobMemoryLimit { get; set; }

    internal JOB_OBJECT_LIMIT ComputeLimitFlags()
    {
        var flags = (JOB_OBJECT_LIMIT)Flags;

        if (PerProcessUserTimeLimit.HasValue)
            flags |= JOB_OBJECT_LIMIT.JOB_OBJECT_LIMIT_PROCESS_TIME;

        if (PerJobUserTimeLimit.HasValue)
            flags |= JOB_OBJECT_LIMIT.JOB_OBJECT_LIMIT_JOB_TIME;

        if (MinimumWorkingSetSize.HasValue || MaximumWorkingSetSize.HasValue)
            flags |= JOB_OBJECT_LIMIT.JOB_OBJECT_LIMIT_WORKINGSET;

        if (ActiveProcessLimit.HasValue)
            flags |= JOB_OBJECT_LIMIT.JOB_OBJECT_LIMIT_ACTIVE_PROCESS;

        if (Affinity.HasValue)
            flags |= JOB_OBJECT_LIMIT.JOB_OBJECT_LIMIT_AFFINITY;

        if (PriorityClass.HasValue)
            flags |= JOB_OBJECT_LIMIT.JOB_OBJECT_LIMIT_PRIORITY_CLASS;

        if (SchedulingClass.HasValue)
            flags |= JOB_OBJECT_LIMIT.JOB_OBJECT_LIMIT_SCHEDULING_CLASS;

        if (ProcessMemoryLimit.HasValue)
            flags |= JOB_OBJECT_LIMIT.JOB_OBJECT_LIMIT_PROCESS_MEMORY;

        if (JobMemoryLimit.HasValue)
            flags |= JOB_OBJECT_LIMIT.JOB_OBJECT_LIMIT_JOB_MEMORY;

        return flags;
    }
}
