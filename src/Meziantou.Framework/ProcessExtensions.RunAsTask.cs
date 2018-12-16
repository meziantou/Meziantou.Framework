using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Meziantou.Framework
{
    public static partial class ProcessExtensions
    {
        public static Task<ProcessResult> RunAsTask(string fileName, string arguments, CancellationToken cancellationToken = default)
        {
            return RunAsTask(fileName, arguments, workingDirectory: null, cancellationToken);
        }

        public static Task<ProcessResult> RunAsTask(string fileName, string arguments, string workingDirectory, CancellationToken cancellationToken = default)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                ErrorDialog = false,
                UseShellExecute = false
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

        public static Task<ProcessResult> RunAsTask(this ProcessStartInfo psi, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled<ProcessResult>(cancellationToken);

            var tcs = new TaskCompletionSource<ProcessResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var logs = new List<ProcessOutput>();

            var process = new Process
            {
                StartInfo = psi,
                EnableRaisingEvents = true
            };

            process.Exited += (sender, e) =>
            {
                process.WaitForExit();
                tcs.SetResult(new ProcessResult(process.ExitCode, logs));
                process.Dispose();
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    logs.Add(new ProcessOutput(ProcessOutputType.StandardError, e.Data));
                }
            };
            process.OutputDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    logs.Add(new ProcessOutput(ProcessOutputType.StandardOutput, e.Data));
                }
            };

            if (!process.Start())
                throw new InvalidOperationException($"Cannot start the process '{psi.FileName}'");

            if (psi.RedirectStandardOutput)
            {
                process.BeginOutputReadLine();
            }

            if (psi.RedirectStandardError)
            {
                process.BeginErrorReadLine();
            }

            if (cancellationToken.CanBeCanceled)
            {
                cancellationToken.Register(() =>
                {
                    if (process.HasExited)
                        return;

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
                    finally
                    {
                        process.Dispose();
                    }
                });
            }

            if (psi.RedirectStandardInput)
            {
                process.StandardInput.Close();
            }

            return tcs.Task;
        }
    }
}
