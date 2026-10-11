using System.Collections;
using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Meziantou.Framework.Imaging.TestHarness.ExternalTools;

/// <summary>Runs a process with <c>posix_spawn</c> on macOS, in place of <see cref="System.Diagnostics.Process"/>.</summary>
/// <remarks>
/// <para>
/// Before .NET 11, <see cref="System.Diagnostics.Process"/> forks on macOS. A fork of a .NET process takes milliseconds and
/// the forks of a process run one at a time: tests that start thousands of tools end up waiting on each other, whatever
/// the number of tests running in parallel. .NET 11 starts processes with <c>posix_spawn</c> on macOS, and the other
/// systems do not have this cost.
/// </para>
/// <para>
/// On .NET 11 (11.0.0-rc.1), <c>Process.Kill(entireProcessTree: true)</c> never returns on macOS when another child
/// process exits while it runs: it stops the process before killing it, and the <c>waitid</c> of macOS reports a stopped
/// child to the loop that reaps exited children, which then spins with the lock the kill needs. Here, the child gets its
/// own process group and a cancellation kills the group.
/// </para>
/// </remarks>
[SupportedOSPlatform("macos")]
internal static class PosixSpawnProcess
{
    private const short POSIX_SPAWN_SETPGROUP = 0x0002;
    private const short POSIX_SPAWN_SETSIGDEF = 0x0004;
    private const short POSIX_SPAWN_SETSIGMASK = 0x0008;
    private const short POSIX_SPAWN_CLOEXEC_DEFAULT = 0x4000;
    private const int O_RDONLY = 0;
    private const int EINTR = 4;
    private const int SIGKILL = 9;
    private const int SIGSTOP = 17;

    public static async Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        using var outputPipe = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.None);
        using var errorPipe = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.None);
        var child = new ChildProcess(Spawn(fileName, arguments, outputPipe.ClientSafePipeHandle, errorPipe.ClientSafePipeHandle));

        // The child holds the only write ends from now on: the reads end when it closes them
        outputPipe.DisposeLocalCopyOfClientHandle();
        errorPipe.DisposeLocalCopyOfClientHandle();

        // Not disposed: MemoryStream holds no unmanaged resource
        var output = new MemoryStream();
        using var errorReader = new StreamReader(errorPipe, Encoding.UTF8);
        int status;
        Task outputTask;
        Task<string> errorTask;
        using (cancellationToken.UnsafeRegister(state => ((ChildProcess)state!).Kill(), child))
        {
#pragma warning disable CA2025 // False positive: both tasks are awaited below, before the pipes are disposed
            outputTask = outputPipe.CopyToAsync(output, cancellationToken);
            errorTask = errorReader.ReadToEndAsync(cancellationToken);
#pragma warning restore CA2025

            // Never dispose the pipes while they are being read, and always reap the child
            await Task.WhenAll(outputTask, errorTask).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            status = await Task.Run(child.WaitForExit, CancellationToken.None).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var error = await errorTask.ConfigureAwait(false);
        await outputTask.ConfigureAwait(false);

        // As Process.ExitCode does: the exit code, or 128 + the number of the signal that terminated the process
        var signal = status & 0x7F;
        return new ProcessResult(signal is 0 ? (status >> 8) & 0xFF : 128 + signal, output.ToArray(), error);
    }

    private static int Spawn(string fileName, IEnumerable<string> arguments, SafePipeHandle output, SafePipeHandle error)
    {
        nint argv = 0;
        nint envp = 0;
        nint emptyInput = 0;
        nint attributes = 0;
        nint fileActions = 0;
        var hasAttributes = false;
        var hasFileActions = false;
        try
        {
            argv = AllocateStringArray([fileName, .. arguments]);
            envp = AllocateStringArray(Environment.GetEnvironmentVariables().Cast<DictionaryEntry>().Select(entry => $"{entry.Key}={entry.Value}"));
            emptyInput = Marshal.StringToCoTaskMemUTF8("/dev/null");

            // Only the three standard descriptors reach the child, in its own process group (so that a cancellation
            // also stops the processes it started), with the default signal dispositions and no blocked signal
            ThrowOnError(unsafe(((delegate* unmanaged<nint*, int>)Interop.PosixSpawnAttrInit)(&attributes)), fileName);
            hasAttributes = true;
            var allSignals = uint.MaxValue & ~(1u << (SIGKILL - 1)) & ~(1u << (SIGSTOP - 1));
            var noSignal = 0u;
            ThrowOnError(unsafe(((delegate* unmanaged<nint*, uint*, int>)Interop.PosixSpawnAttrSetSigDefault)(&attributes, &allSignals)), fileName);
            ThrowOnError(unsafe(((delegate* unmanaged<nint*, uint*, int>)Interop.PosixSpawnAttrSetSigMask)(&attributes, &noSignal)), fileName);
            ThrowOnError(unsafe(((delegate* unmanaged<nint*, int, int>)Interop.PosixSpawnAttrSetPGroup)(&attributes, 0)), fileName);
            ThrowOnError(unsafe(((delegate* unmanaged<nint*, short, int>)Interop.PosixSpawnAttrSetFlags)(&attributes, POSIX_SPAWN_CLOEXEC_DEFAULT | POSIX_SPAWN_SETPGROUP | POSIX_SPAWN_SETSIGDEF | POSIX_SPAWN_SETSIGMASK)), fileName);

            // An empty standard input, as a redirected input that is closed at once
            ThrowOnError(unsafe(((delegate* unmanaged<nint*, int>)Interop.PosixSpawnFileActionsInit)(&fileActions)), fileName);
            hasFileActions = true;
            ThrowOnError(unsafe(((delegate* unmanaged<nint*, int, nint, int, ushort, int>)Interop.PosixSpawnFileActionsAddOpen)(&fileActions, 0, emptyInput, O_RDONLY, 0)), fileName);
            ThrowOnError(unsafe(((delegate* unmanaged<nint*, int, int, int>)Interop.PosixSpawnFileActionsAddDup2)(&fileActions, (int)output.DangerousGetHandle(), 1)), fileName);
            ThrowOnError(unsafe(((delegate* unmanaged<nint*, int, int, int>)Interop.PosixSpawnFileActionsAddDup2)(&fileActions, (int)error.DangerousGetHandle(), 2)), fileName);

            var processId = 0;
            ThrowOnError(unsafe(((delegate* unmanaged<int*, nint, nint*, nint*, nint, nint, int>)Interop.PosixSpawnP)(&processId, Marshal.ReadIntPtr(argv), &fileActions, &attributes, argv, envp)), fileName);
            return processId;
        }
        finally
        {
            if (hasFileActions)
            {
                _ = unsafe(((delegate* unmanaged<nint*, int>)Interop.PosixSpawnFileActionsDestroy)(&fileActions));
            }

            if (hasAttributes)
            {
                _ = unsafe(((delegate* unmanaged<nint*, int>)Interop.PosixSpawnAttrDestroy)(&attributes));
            }

            unsafe
            {
                Marshal.FreeCoTaskMem(emptyInput);
            }

            FreeStringArray(envp);
            FreeStringArray(argv);
        }
    }

    // The posix_spawn functions return the error number instead of setting errno
    private static void ThrowOnError(int error, string fileName)
    {
        if (error is not 0)
            throw new Win32Exception(error, $"Cannot start '{fileName}': {Marshal.GetPInvokeErrorMessage(error)}");
    }

    private static nint AllocateStringArray(IEnumerable<string> values)
    {
        var items = values.ToArray();
        unsafe
        {
            // Zeroed: the array ends with a null pointer
            var array = (nint*)NativeMemory.AllocZeroed((nuint)items.Length + 1, (nuint)sizeof(nint));
            for (var i = 0; i < items.Length; i++)
            {
                array[i] = Marshal.StringToCoTaskMemUTF8(items[i]);
            }

            return (nint)array;
        }
    }

    private static void FreeStringArray(nint array)
    {
        if (array is 0)
            return;

        unsafe
        {
            for (var item = (nint*)array; *item is not 0; item++)
            {
                Marshal.FreeCoTaskMem(*item);
            }

            NativeMemory.Free((void*)array);
        }
    }

    private sealed class ChildProcess(int processId)
    {
        private readonly Lock _lock = new();
        private bool _exited;

        /// <summary>Kills the process group of the child, unless the child was reaped: its identifier may then belong to another process.</summary>
        public void Kill()
        {
            lock (_lock)
            {
                if (!_exited)
                {
                    _ = unsafe(((delegate* unmanaged<int, int, int>)Interop.Kill)(-processId, SIGKILL));
                }
            }
        }

        /// <summary>Blocks until the child exits and reaps it.</summary>
        /// <returns>The wait status.</returns>
        public int WaitForExit()
        {
            int result;
            var status = 0;
            var error = 0;
            do
            {
                result = unsafe(((delegate* unmanaged<int, int*, int, int>)Interop.WaitPid)(processId, &status, 0));
                error = Marshal.GetLastSystemError();
            }
            while (result < 0 && error is EINTR);

            lock (_lock)
            {
                _exited = true;
            }

            if (result != processId)
                throw new Win32Exception(error, $"Cannot get the exit code of the process {processId.ToString(CultureInfo.InvariantCulture)}: {Marshal.GetPInvokeErrorMessage(error)}");

            return status;
        }
    }

    // Function pointers: the code LibraryImport generates does not compile under the updated memory safety rules
    private static class Interop
    {
        public static readonly nint PosixSpawnP = GetExport("posix_spawnp");
        public static readonly nint PosixSpawnAttrInit = GetExport("posix_spawnattr_init");
        public static readonly nint PosixSpawnAttrDestroy = GetExport("posix_spawnattr_destroy");
        public static readonly nint PosixSpawnAttrSetFlags = GetExport("posix_spawnattr_setflags");
        public static readonly nint PosixSpawnAttrSetSigDefault = GetExport("posix_spawnattr_setsigdefault");
        public static readonly nint PosixSpawnAttrSetSigMask = GetExport("posix_spawnattr_setsigmask");
        public static readonly nint PosixSpawnAttrSetPGroup = GetExport("posix_spawnattr_setpgroup");
        public static readonly nint PosixSpawnFileActionsInit = GetExport("posix_spawn_file_actions_init");
        public static readonly nint PosixSpawnFileActionsDestroy = GetExport("posix_spawn_file_actions_destroy");
        public static readonly nint PosixSpawnFileActionsAddDup2 = GetExport("posix_spawn_file_actions_adddup2");
        public static readonly nint PosixSpawnFileActionsAddOpen = GetExport("posix_spawn_file_actions_addopen");
        public static readonly nint WaitPid = GetExport("waitpid");
        public static readonly nint Kill = GetExport("kill");

        private static nint GetExport(string name) => NativeLibrary.GetExport(NativeLibrary.GetMainProgramHandle(), name);
    }
}
