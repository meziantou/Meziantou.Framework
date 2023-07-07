using System.ComponentModel;
using System.Diagnostics;

namespace Meziantou.Framework;

public static partial class ProcessExtensions
{
    public static Task<ProcessResult> RunAsTaskAsync(string fileName, string? arguments, CancellationToken cancellationToken = default)
    {
        return RunAsTaskAsync(fileName, arguments, workingDirectory: null, cancellationToken);
    }

    public static Task<ProcessResult> RunAsTaskAsync(string fileName, string? arguments, string? workingDirectory, CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            ErrorDialog = false,
            UseShellExecute = false,
        };

        if (arguments != null)
        {
            psi.Arguments = arguments;
        }

        if (workingDirectory != null)
        {
            psi.WorkingDirectory = workingDirectory;
        }

        return RunAsTaskAsync(psi, cancellationToken);
    }

    public static Task<ProcessResult> RunAsTaskAsync(string fileName, IEnumerable<string>? arguments, string? workingDirectory, CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            ErrorDialog = false,
            UseShellExecute = false,
        };

        if (arguments != null)
        {
            psi.ArgumentList.AddRange(arguments);
        }

        if (workingDirectory != null)
        {
            psi.WorkingDirectory = workingDirectory;
        }

        return RunAsTaskAsync(psi, cancellationToken);
    }

    public static Task<ProcessResult> RunAsTaskAsync(this ProcessStartInfo psi, bool redirectOutput, CancellationToken cancellationToken = default)
    {
        if (redirectOutput)
        {
            psi.RedirectStandardError = true;
            psi.RedirectStandardOutput = true;
            psi.UseShellExecute = false;
        }
        else
        {
            psi.RedirectStandardError = false;
            psi.RedirectStandardOutput = false;
        }

        return RunAsTaskAsync(psi, cancellationToken);
    }

    public static async Task<ProcessResult> RunAsTaskAsync(this ProcessStartInfo psi, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var logs = new List<ProcessOutput>();
        int exitCode;

        using (var process = new Process())
        {
            process.StartInfo = psi;
            if (psi.RedirectStandardError)
            {
                process.ErrorDataReceived += (sender, e) =>
                {
                    if (e.Data != null)
                    {
                        lock (logs)
                        {
                            logs.Add(new ProcessOutput(ProcessOutputType.StandardError, e.Data));
                        }
                    }
                };
            }

            if (psi.RedirectStandardOutput)
            {
                process.OutputDataReceived += (sender, e) =>
                {
                    if (e.Data != null)
                    {
                        lock (logs)
                        {
                            logs.Add(new ProcessOutput(ProcessOutputType.StandardOutput, e.Data));
                        }
                    }
                };
            }

            if (!process.Start())
                throw new Win32Exception("Cannot start the process");

            if (psi.RedirectStandardError)
            {
                process.BeginErrorReadLine();
            }

            if (psi.RedirectStandardOutput)
            {
                process.BeginOutputReadLine();
            }

            if (psi.RedirectStandardInput)
            {
                process.StandardInput.Close();
            }

            CancellationTokenRegistration registration = default;
            try
            {
                if (cancellationToken.CanBeCanceled && !process.HasExited)
                {
                    registration = cancellationToken.Register(() =>
                    {
                        try
                        {
                            process.Kill(entireProcessTree: true);
                        }
                        catch (InvalidOperationException)
                        {
                            try
                            {
                                // Try to at least kill the root process
                                process.Kill();
                            }
                            catch (InvalidOperationException)
                            {
                            }
                        }
                    });
                }

                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await registration.DisposeAsync().ConfigureAwait(false);
            }

            exitCode = process.ExitCode;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new ProcessResult(exitCode, logs);
    }
}
