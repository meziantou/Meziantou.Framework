#nullable disable
using System;
using Meziantou.Framework.Win32.Natives;

namespace Meziantou.Framework.Win32
{
    /// <summary>
    /// Defines a job object limits.
    /// </summary>
    public sealed class JobObjectLimits
    {
        private long _perProcessUserTimeLimit;
        private long _perJobUserTimeLimit;
        private ulong _minimumWorkingSetSize;
        private ulong _maximumWorkingSetSize;
        private uint _activeProcessLimit;
        private IntPtr _affinity;
        private uint _priorityClass;
        private uint _schedulingClass;
        private ulong _processMemoryLimit;
        private ulong _jobMemoryLimit;
        private JobObjectLimitFlags _flags;

        internal LimitFlags InternalFlags { get; set; }

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
                InternalFlags |= (LimitFlags)_flags;
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
                InternalFlags |= LimitFlags.ProcessTime;
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
                InternalFlags |= LimitFlags.JobTime;
            }
        }

        /// <summary>
        /// Gets or sets the minimum working set size for each process associated with the job.
        /// </summary>
        /// <value>
        /// The minimum working set size for each process associated with the job.
        /// </value>
        public ulong MinimumWorkingSetSize
        {
            get => _minimumWorkingSetSize;
            set
            {
                if (IntPtr.Size == 4 && value > uint.MaxValue)
                {
                    value = uint.MaxValue;
                }

                _minimumWorkingSetSize = value;
                InternalFlags |= LimitFlags.WorkingSet;
            }
        }

        /// <summary>
        /// Gets or sets the maximum working set size for each process associated with the job.
        /// </summary>
        /// <value>
        /// The maximum working set size for each process associated with the job.
        /// </value>
        public ulong MaximumWorkingSetSize
        {
            get => _maximumWorkingSetSize;
            set
            {
                if (IntPtr.Size == 4 && value > uint.MaxValue)
                {
                    value = uint.MaxValue;
                }

                _maximumWorkingSetSize = value;
                InternalFlags |= LimitFlags.WorkingSet;
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
                InternalFlags |= LimitFlags.ActiveProcess;
            }
        }

        /// <summary>
        /// Gets or sets the processor affinity for all processes associated with the job.
        /// </summary>
        /// <value>
        /// The processor affinity for all processes associated with the job.
        /// </value>
        public IntPtr Affinity
        {
            get => _affinity;
            set
            {
                _affinity = value;
                InternalFlags |= LimitFlags.Affinity;
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
                InternalFlags |= LimitFlags.PriorityClass;
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
                InternalFlags |= LimitFlags.SchedulingClass;
            }
        }

        /// <summary>
        /// Gets or sets the limit for the virtual memory that can be committed by a process.
        /// </summary>
        /// <value>
        /// The limit for the virtual memory that can be committed by a process.
        /// </value>
        public ulong ProcessMemoryLimit
        {
            get => _processMemoryLimit;
            set
            {
                if (IntPtr.Size == 4 && value > uint.MaxValue)
                {
                    value = uint.MaxValue;
                }

                _processMemoryLimit = value;
                InternalFlags |= LimitFlags.ProcessMemory;
            }
        }

        /// <summary>
        /// Gets or sets limit for the virtual memory that can be committed for the job.
        /// </summary>
        /// <value>
        /// The limit for the virtual memory that can be committed for the job.
        /// </value>
        public ulong JobMemoryLimit
        {
            get => _jobMemoryLimit;
            set
            {
                if (IntPtr.Size == 4 && value > uint.MaxValue)
                {
                    value = uint.MaxValue;
                }

                _jobMemoryLimit = value;
                InternalFlags |= LimitFlags.JobMemory;
            }
        }
    }
}
