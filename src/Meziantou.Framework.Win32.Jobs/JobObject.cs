using System;
using System.Runtime.InteropServices;
using System.ComponentModel;
using Meziantou.Framework.Win32.Natives;
using System.Diagnostics;

namespace Meziantou.Framework.Win32
{
    /// <summary>
    /// A utility class that represents a Windows job object. Job objects allow groups of processes to be managed as a unit.
    /// </summary>
    public sealed class JobObject : SafeHandle
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="JobObject"/> class. The associated job object will have no name.
        /// </summary>
        public JobObject()
            : this(name: null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="JobObject"/> class.
        /// </summary>
        /// <param name="name">The job object name. May be null.</param>
        public JobObject(string name)
            : base(IntPtr.Zero, ownsHandle: true)
        {
            var atts = new SECURITY_ATTRIBUTES
            {
                bInheritHandle = true,
                lpSecurityDescriptor = IntPtr.Zero,
                nLength = Marshal.SizeOf(typeof(SECURITY_ATTRIBUTES)),
            };

            base.SetHandle(NativeMethods.CreateJobObject(ref atts, name));
        }

        /// <summary>
        /// When overridden in a derived class, gets a value indicating whether the handle value is invalid.
        /// </summary>
        /// <returns>true if the handle value is invalid; otherwise, false.</returns>
        public override bool IsInvalid => IsClosed || handle == IntPtr.Zero;

        /// <summary>
        /// When overridden in a derived class, executes the code required to free the handle.
        /// </summary>
        /// <returns>
        /// true if the handle is released successfully; otherwise, in the event of a catastrophic failure, false. In this case, it generates a releaseHandleFailed MDA Managed Debugging Assistant.
        /// </returns>
        protected override bool ReleaseHandle()
        {
            return NativeMethods.CloseHandle(handle);
        }

        /// <summary>
        /// Terminates all processes currently associated with the job. If the job is nested, this function terminates all processes currently associated with the job and all of its child jobs in the hierarchy.
        /// </summary>
        public void Terminate()
        {
            Terminate(1);
        }

        /// <summary>
        /// Terminates all processes currently associated with the job. If the job is nested, this function terminates all processes currently associated with the job and all of its child jobs in the hierarchy.
        /// </summary>
        /// <param name="exitCode">The exit code to be used by all processes and threads in the job object.</param>
        public void Terminate(int exitCode)
        {
            NativeMethods.TerminateJobObject(this, unchecked((uint)exitCode));
        }

        /// <summary>
        /// Assigns a process to an existing job object.
        /// </summary>
        /// <param name="process">The process.</param>
        /// <returns>
        /// true if the function succeeds; otherwise false.
        /// </returns>
        public bool AssignProcess(Process process)
        {
            return AssignProcess(process, throwOnError: false);
        }

        /// <summary>
        /// Assigns a process to an existing job object.
        /// </summary>
        /// <param name="process">The process.</param>
        /// <param name="throwOnError">if set to <c>true</c> errors may be thrown.</param>
        /// <returns>
        /// true if the function succeeds; otherwise false.
        /// </returns>
        public bool AssignProcess(Process process, bool throwOnError)
        {
            return AssignProcess(process.Handle, throwOnError);
        }

        /// <summary>
        /// Assigns a process to an existing job object.
        /// </summary>
        /// <param name="processHandle">The process handle.</param>
        /// <returns>
        /// true if the function succeeds; otherwise false.
        /// </returns>
        public bool AssignProcess(IntPtr processHandle)
        {
            return AssignProcess(processHandle, throwOnError: false);
        }

        /// <summary>
        /// Assigns a process to an existing job object.
        /// </summary>
        /// <param name="processHandle">The process handle.</param>
        /// <param name="throwOnError">if set to <c>true</c> errors may be thrown.</param>
        /// <returns>
        /// true if the function succeeds; otherwise false.
        /// </returns>
        public bool AssignProcess(IntPtr processHandle, bool throwOnError)
        {
            if (!NativeMethods.AssignProcessToJobObject(this, processHandle))
            {
                if (!throwOnError)
                    return false;

                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            return true;
        }

        /// <summary>
        /// Sets limits to the job.
        /// </summary>
        /// <param name="limits">The limits. May not be null.</param>
        /// <returns>
        /// true if the function succeeds; otherwise false.
        /// </returns>
        public bool SetLimits(JobObjectLimits limits)
        {
            return SetLimits(limits, throwOnError: false);
        }

        /// <summary>
        /// Sets limits to the jhob.
        /// </summary>
        /// <param name="limits">The limits. May not be null.</param>
        /// <param name="throwOnError">if set to <c>true</c> errors may be thrown.</param>
        /// <returns>
        /// <c>true</c> if the function succeeds; otherwise <c>false</c>.
        /// </returns>
        public bool SetLimits(JobObjectLimits limits, bool throwOnError)
        {
            if (limits == null)
                throw new ArgumentNullException(nameof(limits));

            var info = JOBOBJECT_INFO.From(limits);
            var length = Environment.Is64BitProcess ? Marshal.SizeOf(info.ExtendedLimits64) : Marshal.SizeOf(info.ExtendedLimits32);
            if (!NativeMethods.SetInformationJobObject(this, JobObjectInfoClass.ExtendedLimitInformation, ref info, length))
            {
                if (!throwOnError)
                    return false;

                var err = Marshal.GetLastWin32Error();
                throw new Win32Exception(err);
            }

            return true;
        }
    }
}
