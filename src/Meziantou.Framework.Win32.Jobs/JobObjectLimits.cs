using Windows.Win32.System.JobObjects;

namespace Meziantou.Framework.Win32;

/// <summary>
/// Defines a job object limits.
/// </summary>
public sealed class JobObjectLimits
{
    private long _perProcessUserTimeLimit;
    private long _perJobUserTimeLimit;
    private nuint _minimumWorkingSetSize;
    private nuint _maximumWorkingSetSize;
    private uint _activeProcessLimit;
    private nuint _affinity;
    private uint _priorityClass;
    private uint _schedulingClass;
    private nuint _processMemoryLimit;
    private nuint _jobMemoryLimit;
    private JobObjectLimitFlags _flags;

    internal JOB_OBJECT_LIMIT InternalFlags { get; set; }

    /// <summary>
    /// Defines options for a job object.
    /// </summary>
    /// <value>
    /// The options for a job object.
    /// </value>
    public JobObjectLimitFlags Flags
    {
        get => _flags;
        set
        {
            _flags = value;
            InternalFlags |= (JOB_OBJECT_LIMIT)_flags;
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
        get => _perProcessUserTimeLimit;
        set
        {
            _perProcessUserTimeLimit = value;
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
        get => _perJobUserTimeLimit;
        set
        {
            _perJobUserTimeLimit = value;
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
        get => _minimumWorkingSetSize;
        set
        {
            _minimumWorkingSetSize = value;
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
        get => _maximumWorkingSetSize;
        set
        {
            _maximumWorkingSetSize = value;
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
        get => _activeProcessLimit;
        set
        {
            _activeProcessLimit = value;
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
        get => _affinity;
        set
        {
            _affinity = value;
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
        get => _priorityClass;
        set
        {
            _priorityClass = value;
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
        get => _schedulingClass;
        set
        {
            _schedulingClass = value;
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
        get => _processMemoryLimit;
        set
        {

            _processMemoryLimit = value;
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
        get => _jobMemoryLimit;
        set
        {
            _jobMemoryLimit = value;
            InternalFlags |= JOB_OBJECT_LIMIT.JOB_OBJECT_LIMIT_JOB_MEMORY;
        }
    }
}
