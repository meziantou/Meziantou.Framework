using System.Diagnostics;
using System.Globalization;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;

namespace Meziantou.Framework.Diagnostics;

public static partial class MemoryDump
{
    private const int PR_SET_PTRACER = 0x59616d61;
    private const nint PR_SET_PTRACER_ANY = -1;
    private const nint PR_SET_PTRACER_NONE = 0;

    // createdump is the tool used by the runtime to create dumps on crash (DOTNET_DbgEnableMiniDump).
    // It dumps the process from a separate process, so the threads of the current process are suspended while the dump is written.
    private static void WriteWithCreateDump(string filePath, MemoryDumpType dumpType)
    {
        using var process = new Process { StartInfo = CreateDumpStartInfo(GetCreateDumpPath(), filePath, dumpType) };
        var output = new StringBuilder();
        AllowPtraceFromAnyProcess();
        try
        {
            StartCreateDump(process, output);
            process.WaitForExit();
        }
        finally
        {
            RevokePtraceFromAnyProcess();
        }

        EnsureSuccess(process, filePath, output);
    }

    private static async Task WriteWithCreateDumpAsync(string filePath, MemoryDumpType dumpType, CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = CreateDumpStartInfo(GetCreateDumpPath(), filePath, dumpType) };
        var output = new StringBuilder();
        AllowPtraceFromAnyProcess();
        try
        {
            StartCreateDump(process, output);
            try
            {
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                KillCreateDump(process);
                TryDeleteFile(filePath);
                throw;
            }
        }
        finally
        {
            RevokePtraceFromAnyProcess();
        }

        EnsureSuccess(process, filePath, output);
    }

    private static ProcessStartInfo CreateDumpStartInfo(string createDumpPath, string filePath, MemoryDumpType dumpType)
    {
        var startInfo = new ProcessStartInfo(createDumpPath)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        startInfo.ArgumentList.Add(dumpType switch
        {
            MemoryDumpType.Normal => "--normal",
            MemoryDumpType.WithHeap => "--withheap",
            MemoryDumpType.Triage => "--triage",
            MemoryDumpType.Full => "--full",
            _ => throw new ArgumentOutOfRangeException(nameof(dumpType), dumpType, message: null),
        });

        // createdump expands templates such as %p or %e in the file name, and %% is a literal %
        startInfo.ArgumentList.Add("--name");
        startInfo.ArgumentList.Add(filePath.Replace("%", "%%", StringComparison.Ordinal));
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        return startInfo;
    }

    private static string GetCreateDumpPath()
    {
        if (TryGetCreateDumpPath(out var path))
            return path;

        throw new FileNotFoundException("Cannot find the createdump tool in the .NET runtime directory or the application directory", GetCreateDumpFileName());
    }

    private static bool TryGetCreateDumpPath([NotNullWhen(true)] out string? path)
    {
        // Framework-dependent apps use the tool from the shared runtime. Self-contained and single-file apps have it next to the application.
        var fileName = GetCreateDumpFileName();
        var runtimeDirectory = RuntimeEnvironment.GetRuntimeDirectory();
        if (!string.IsNullOrEmpty(runtimeDirectory))
        {
            path = Path.Combine(runtimeDirectory, fileName);
            if (File.Exists(path))
                return true;
        }

        path = Path.Combine(AppContext.BaseDirectory, fileName);
        if (File.Exists(path))
            return true;

        path = null;
        return false;
    }

    private static string GetCreateDumpFileName() => OperatingSystem.IsWindows() ? "createdump.exe" : "createdump";

    private static void StartCreateDump(Process process, StringBuilder output)
    {
        process.OutputDataReceived += (_, e) => AppendLine(output, e.Data);
        process.ErrorDataReceived += (_, e) => AppendLine(output, e.Data);
        process.Start();
        process.StandardInput.Close();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        static void AppendLine(StringBuilder output, string? line)
        {
            if (line is null)
                return;

            lock (output)
            {
                output.AppendLine(line);
            }
        }
    }

    private static void EnsureSuccess(Process process, string filePath, StringBuilder output)
    {
        if (process.ExitCode is 0 && File.Exists(filePath))
            return;

        TryDeleteFile(filePath);

        string message;
        lock (output)
        {
            message = output.ToString().Trim();
        }

        throw new InvalidOperationException($"createdump failed with exit code {process.ExitCode.ToString(CultureInfo.InvariantCulture)}: {message}");
    }

    private static void KillCreateDump(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
        }
        catch (InvalidOperationException)
        {
            // The process has already exited
        }
    }

    // Linux Yama (kernel.yama.ptrace_scope = 1) only lets a process trace its descendants.
    // createdump is a child of the current process, so the current process must explicitly allow it to attach.
    private static void AllowPtraceFromAnyProcess()
    {
        if (OperatingSystem.IsLinux())
        {
            // Fails with EINVAL when Yama is not enabled, in which case no permission is needed
            _ = UnixInterop.prctl(PR_SET_PTRACER, PR_SET_PTRACER_ANY, 0, 0, 0);
        }
    }

    private static void RevokePtraceFromAnyProcess()
    {
        if (OperatingSystem.IsLinux())
        {
            _ = UnixInterop.prctl(PR_SET_PTRACER, PR_SET_PTRACER_NONE, 0, 0, 0);
        }
    }

    // DllImport rather than LibraryImport: the .NET 10 LibraryImport generator emits code rejected by the updated memory safety rules
    private static class UnixInterop
    {
        [DllImport("libc", EntryPoint = "prctl", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "Native method name")]
        [SuppressMessage("Interoperability", "SYSLIB1054:Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time", Justification = "See comment on the containing class")]
        internal static safe extern int prctl(int option, nint arg2, nint arg3, nint arg4, nint arg5);
    }
}
