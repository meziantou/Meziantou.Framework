using System.Diagnostics;
using Microsoft.Win32.SafeHandles;

namespace Meziantou.Framework;

public sealed class SystemProcessFactory : IProcessFactory
{
    public static SystemProcessFactory Instance { get; } = new();

    private SystemProcessFactory()
    {
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "SystemProcessHandle owns and disposes the Process instance.")]
    public IProcessHandle Create(ProcessStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        return new SystemProcessHandle(new Process { StartInfo = startInfo });
    }

    internal sealed class SystemProcessHandle(Process process) : IProcessHandle, IProcessHandleWithEncoding
    {
        public Process Process { get; } = process ?? throw new ArgumentNullException(nameof(process));

        public int Id => Process.Id;

        public bool HasExited => Process.HasExited;

        public int ExitCode => Process.ExitCode;

        public Stream InputStream => Process.StandardInput.BaseStream;

        public Stream OutputStream => Process.StandardOutput.BaseStream;

        public Stream ErrorStream => Process.StandardError.BaseStream;

        public SafeProcessHandle? SafeProcessHandle
        {
            get
            {
                try
                {
                    return Process.SafeHandle;
                }
                catch (InvalidOperationException)
                {
                    return null;
                }
            }
        }

        public Encoding OutputEncoding => Process.StandardOutput.CurrentEncoding;

        public Encoding ErrorEncoding => Process.StandardError.CurrentEncoding;

        public bool Start() => Process.Start();

        public Task WaitForExitAsync(CancellationToken cancellationToken) => Process.WaitForExitAsync(cancellationToken);

        public void Kill(bool entireProcessTree = true)
        {
            try
            {
                Process.Kill(entireProcessTree);
            }
            catch (AggregateException) when (entireProcessTree)
            {
                try
                {
                    Process.Kill();
                }
                catch (InvalidOperationException)
                {
                }
            }
            catch (InvalidOperationException) when (entireProcessTree)
            {
                try
                {
                    Process.Kill();
                }
                catch (InvalidOperationException)
                {
                }
            }
            catch (InvalidOperationException)
            {
            }
        }

        public void Dispose()
        {
            Process.Dispose();
        }

    }
}
