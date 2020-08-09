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

        public static async Task<ProcessResult> RunAsTask(this ProcessStartInfo psi, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var logs = new List<ProcessOutput>();
            ProcessResult result;
            using (var process = Process.Start(psi))
            {
                if (psi.RedirectStandardError)
                {
                    process.ErrorDataReceived += (sender, e) =>
                    {
                        if (e.Data != null)
                        {
                            logs.Add(new ProcessOutput(ProcessOutputType.StandardError, e.Data));
                        }
                    };
                    process.BeginErrorReadLine();
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
                    process.BeginOutputReadLine();
                }

                if (psi.RedirectStandardInput)
                {
                    process.StandardInput.Close();
                }

                if (process.HasExited)
                    return new ProcessResult(process.ExitCode, logs);

                CancellationTokenRegistration registration = default;
                if (cancellationToken.CanBeCanceled)
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
                process.WaitForExit();
                registration.Dispose();
                result = new ProcessResult(process.ExitCode, logs);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }

        public static async Task WaitForExitAsync(this Process process, CancellationToken cancellationToken = default)
        {
            // https://source.dot.net/#System.Diagnostics.Process/System/Diagnostics/Process.cs,b6a5b00714a61f06
            // Because the process has already started by the time this method is called,
            // we're in a race against the process to set up our exit handlers before the process
            // exits. As a result, there are several different flows that must be handled:
            //
            // CASE 1: WE ENABLE EVENTS
            // This is the "happy path". In this case we enable events.
            //
            // CASE 1.1: PROCESS EXITS OR IS CANCELED AFTER REGISTERING HANDLER
            // This case continues the "happy path". The process exits or waiting is canceled after
            // registering the handler and no special cases are needed.
            //
            // CASE 1.2: PROCESS EXITS BEFORE REGISTERING HANDLER
            // It's possible that the process can exit after we enable events but before we reigster
            // the handler. In that case we must check for exit after registering the handler.
            //
            //
            // CASE 2: PROCESS EXITS BEFORE ENABLING EVENTS
            // The process may exit before we attempt to enable events. In that case EnableRaisingEvents
            // will throw an exception like this:
            //     System.InvalidOperationException : Cannot process request because the process (42) has exited.
            // In this case we catch the InvalidOperationException. If the process has exited, our work
            // is done and we return. If for any reason (now or in the future) enabling events fails
            // and the process has not exited, bubble the exception up to the user.
            //
            //
            // CASE 3: USER ALREADY ENABLED EVENTS
            // In this case the user has already enabled raising events. Re-enabling events is a no-op
            // as the value hasn't changed. However, no-op also means that if the process has already
            // exited, EnableRaisingEvents won't throw an exception.
            //
            // CASE 3.1: PROCESS EXITS OR IS CANCELED AFTER REGISTERING HANDLER
            // (See CASE 1.1)
            //
            // CASE 3.2: PROCESS EXITS BEFORE REGISTERING HANDLER
            // (See CASE 1.2)

            if (!process.HasExited)
            {
                // Early out for cancellation before doing more expensive work
                cancellationToken.ThrowIfCancellationRequested();
            }

            try
            {
                // CASE 1: We enable events
                // CASE 2: Process exits before enabling events (and throws an exception)
                // CASE 3: User already enabled events (no-op)
                process.EnableRaisingEvents = true;
            }
            catch (InvalidOperationException)
            {
                // CASE 2: If the process has exited, our work is done, otherwise bubble the
                // exception up to the user
                if (process.HasExited)
                {
                    return;
                }

                throw;
            }

            var tcs = new TaskCompletionSourceWithCancellation<bool>();

            void Handler(object? s, EventArgs e) => tcs.TrySetResult(true);
            process.Exited += Handler;

            try
            {
                if (process.HasExited)
                {
                    // CASE 1.2 & CASE 3.2: Handle race where the process exits before registering the handler
                    return;
                }

                // CASE 1.1 & CASE 3.1: Process exits or is canceled here
                await tcs.WaitWithCancellationAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                process.Exited -= Handler;
            }
        }

        private sealed class TaskCompletionSourceWithCancellation<T> : TaskCompletionSource<T>
        {
            private CancellationToken _cancellationToken;

            public TaskCompletionSourceWithCancellation() : base(TaskCreationOptions.RunContinuationsAsynchronously)
            {
            }

            private void OnCancellation()
            {
                TrySetCanceled(_cancellationToken);
            }

#if NETCOREAPP3_1
            public async ValueTask<T> WaitWithCancellationAsync(CancellationToken cancellationToken)
            {
                _cancellationToken = cancellationToken;
                using (cancellationToken.UnsafeRegister(s => ((TaskCompletionSourceWithCancellation<T>)s!).OnCancellation(), this))
                {
                    return await Task.ConfigureAwait(false);
                }
            }
#elif NET461 || NETSTANDARD2_0
            public async Task<T> WaitWithCancellationAsync(CancellationToken cancellationToken)
            {
                _cancellationToken = cancellationToken;
                using (cancellationToken.Register(s => ((TaskCompletionSourceWithCancellation<T>)s!).OnCancellation(), this))
                {
                    return await Task.ConfigureAwait(false);
                }
            }
#else
#error Platform not supported
#endif
        }
    }
}
