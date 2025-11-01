using Windows.Win32.System.JobObjects;

namespace Meziantou.Framework.Win32;

/// <summary>
/// Defines a job object limits.
/// </summary>
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
    internal JOB_OBJECT_LIMIT InternalFlags { get; set; }

    /// <summary>
    /// Defines options for a job object.
    /// </summary>
    /// <value>
    /// The options for a job object.
    /// </value>
    public JobObjectLimitFlags Flags
    {
        get;
        set
        {
            field = value;
            InternalFlags |= (JOB_OBJECT_LIMIT)field;
        }
    }

    /// <summary>
    /// Gets or sets the per-process user-mode execution time limit, in 100-nanosecond ticks.
    /// </summary>
    /// <value>
    /// The per-process user-mode execution time limit, in 100-nanosecond ticks.
    /// </value>
    public long PerProcessUserTimeLimit
    {
        get;
        set
        {
            field = value;
            InternalFlags |= JOB_OBJECT_LIMIT.JOB_OBJECT_LIMIT_PROCESS_TIME;
        }
    }

    /// <summary>
    /// Gets or sets the per-job user-mode execution time limit, in 100-nanosecond ticks.
    /// </summary>
    /// <value>
    /// The per-job user-mode execution time limit, in 100-nanosecond ticks.
    /// </value>
    public long PerJobUserTimeLimit
    {
        get;
        set
        {
            field = value;
            InternalFlags |= JOB_OBJECT_LIMIT.JOB_OBJECT_LIMIT_JOB_TIME;
        }
    }

    /// <summary>
    /// Gets or sets the minimum working set size for each process associated with the job.
    /// </summary>
    /// <value>
    /// The minimum working set size for each process associated with the job.
    /// </value>
    public nuint MinimumWorkingSetSize
    {
        get;
        set
        {
            field = value;
            InternalFlags |= JOB_OBJECT_LIMIT.JOB_OBJECT_LIMIT_WORKINGSET;
        }
    }

    /// <summary>
    /// Gets or sets the maximum working set size for each process associated with the job.
    /// </summary>
    /// <value>
    /// The maximum working set size for each process associated with the job.
    /// </value>
    public nuint MaximumWorkingSetSize
    {
        get;
        set
        {
            field = value;
            InternalFlags |= JOB_OBJECT_LIMIT.JOB_OBJECT_LIMIT_WORKINGSET;
        }
    }

    /// <summary>
    /// Gets or sets the active process limit for the job.
    /// </summary>
    /// <value>
    /// The active process limit for the job.
    /// </value>
    public uint ActiveProcessLimit
    {
        get;
        set
        {
            field = value;
            InternalFlags |= JOB_OBJECT_LIMIT.JOB_OBJECT_LIMIT_ACTIVE_PROCESS;
        }
    }

    /// <summary>
    /// Gets or sets the processor affinity for all processes associated with the job.
    /// </summary>
    /// <value>
    /// The processor affinity for all processes associated with the job.
    /// </value>
    public nuint Affinity
    {
        get;
        set
        {
            field = value;
            InternalFlags |= JOB_OBJECT_LIMIT.JOB_OBJECT_LIMIT_AFFINITY;
        }
    }

    /// <summary>
    /// Gets or sets priority class for all processes associated with the job.
    /// </summary>
    /// <value>
    /// The priority class for all processes associated with the job.
    /// </value>
    public uint PriorityClass
    {
        get;
        set
        {
            field = value;
            InternalFlags |= JOB_OBJECT_LIMIT.JOB_OBJECT_LIMIT_PRIORITY_CLASS;
        }
    }

    /// <summary>
    /// Gets or sets scheduling  class for all processes associated with the job.
    /// </summary>
    /// <value>
    /// The scheduling  class for all processes associated with the job.
    /// </value>
    public uint SchedulingClass
    {
        get;
        set
        {
            field = value;
            InternalFlags |= JOB_OBJECT_LIMIT.JOB_OBJECT_LIMIT_SCHEDULING_CLASS;
        }
    }

    /// <summary>
    /// Gets or sets the limit for the virtual memory that can be committed by a process.
    /// </summary>
    /// <value>
    /// The limit for the virtual memory that can be committed by a process.
    /// </value>
    public nuint ProcessMemoryLimit
    {
        get;
        set
        {

            field = value;
            InternalFlags |= JOB_OBJECT_LIMIT.JOB_OBJECT_LIMIT_PROCESS_MEMORY;
        }
    }

    /// <summary>
    /// Gets or sets limit for the virtual memory that can be committed for the job.
    /// </summary>
    /// <value>
    /// The limit for the virtual memory that can be committed for the job.
    /// </value>
    public nuint JobMemoryLimit
    {
        get;
        set
        {
            field = value;
            InternalFlags |= JOB_OBJECT_LIMIT.JOB_OBJECT_LIMIT_JOB_MEMORY;
        }
    }
}
