using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Meziantou.Framework
{
    public static partial class ProcessExtensions
    {
        public static Task<ProcessResult> RunAsTask(string fileName, string? arguments, CancellationToken cancellationToken = default)
        {
            return RunAsTask(fileName, arguments, workingDirectory: null, cancellationToken);
        }

        public static Task<ProcessResult> RunAsTask(string fileName, string? arguments, string? workingDirectory, CancellationToken cancellationToken = default)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                ErrorDialog = false,
                UseShellExecute = false,
            };

            return RunAsTask(psi, cancellationToken);
        }

        public static Task<ProcessResult> RunAsTask(this ProcessStartInfo psi, bool redirectOutput, CancellationToken cancellationToken = default)
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

            return RunAsTask(psi, cancellationToken);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Code Quality", "IDE0067:Dispose objects before losing scope", Justification = "Disposed in Exited event")]
        public static Task<ProcessResult> RunAsTask(this ProcessStartInfo psi, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled<ProcessResult>(cancellationToken);

            var tcs = new TaskCompletionSource<ProcessResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var logs = new List<ProcessOutput>();
            var manualResetEvent = new ManualResetEventSlim(); // We cannot dispose the process before calling BeginXXXReadLine and InputStream.Close().

            var process = new Process
            {
                StartInfo = psi,
                EnableRaisingEvents = true,
            };

            process.Exited += (sender, e) =>
            {
                try
                {
#pragma warning disable MA0040 // Use a cancellation token, The process is already closed so it should be almost instant
                    manualResetEvent.Wait();
#pragma warning restore MA0040

                    process.WaitForExit();
                    tcs.TrySetResult(new ProcessResult(process.ExitCode, logs));
                    process.Dispose();
                    manualResetEvent.Dispose();
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            };

            if (psi.RedirectStandardError)
            {
                process.ErrorDataReceived += (sender, e) =>
                {
                    if (e.Data != null)
                    {
                        logs.Add(new ProcessOutput(ProcessOutputType.StandardError, e.Data));
                    }
                };
            }

            if (psi.RedirectStandardOutput)
            {
                process.OutputDataReceived += (sender, e) =>
                {
                    if (e.Data != null)
                    {
                        logs.Add(new ProcessOutput(ProcessOutputType.StandardOutput, e.Data));
                    }
                };
            }

            if (!process.Start())
                throw new InvalidOperationException($"Cannot start the process '{psi.FileName}'");

            if (cancellationToken.CanBeCanceled)
            {
                cancellationToken.Register(() =>
                {
                    if (process.HasExited)
                        return;

                    tcs.TrySetCanceled(cancellationToken);

                    try
                    {
                        if (IsWindows())
                        {
                            process.Kill(entireProcessTree: true);
                        }
                        else
                        {
                            process.Kill();
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        // the process may already be killed
                    }
                });
            }

            if (psi.RedirectStandardOutput)
            {
                process.BeginOutputReadLine();
            }

            if (psi.RedirectStandardError)
            {
                process.BeginErrorReadLine();
            }

            if (psi.RedirectStandardInput)
            {
                process.StandardInput.Close();
            }

            manualResetEvent.Set();
            return tcs.Task;
        }
    }
}
