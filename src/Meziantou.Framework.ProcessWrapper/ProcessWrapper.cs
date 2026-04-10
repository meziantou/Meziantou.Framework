using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Meziantou.Framework.Unix.ControlGroups;
using Meziantou.Framework.Win32;

namespace Meziantou.Framework;

/// <summary>
/// Fluent builder for configuring and running processes.
/// Every <c>With*</c> method mutates the current instance with the replaced value.
/// Every <c>Add*</c> method mutates the current instance with the appended value.
/// </summary>
public sealed class ProcessWrapper
{
    private readonly ProcessStartInfo _startInfo;
    private ProcessValidationMode _validationMode;
    private ImmutableArray<Action<string>> _outputHandlers;
    private ImmutableArray<Action<string>> _errorHandlers;
    private ProcessInputStream? _inputStream;
    private ProcessLimits? _limits;
    private Action<JobObject>? _windowsJobObjectConfiguration;
    private Action<CGroup2>? _linuxControlGroupConfiguration;

    private ProcessWrapper(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        _startInfo = CreateStartInfo(fileName);
        _validationMode = ProcessValidationMode.FailIfNonZeroExitCode;
        _outputHandlers = [];
        _errorHandlers = [];
    }

    private static ProcessStartInfo CreateStartInfo(string fileName)
    {
        return new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            ErrorDialog = false,
        };
    }

    /// <summary>Creates a new <see cref="ProcessWrapper"/> for the specified executable.</summary>
    public static ProcessWrapper Create(string fileName) => new(fileName);

    /// <summary>Sets the arguments for the process, replacing any previously set arguments.</summary>
    public ProcessWrapper WithArguments(params string[] arguments)
    {
        _startInfo.ArgumentList.Clear();
        foreach (var argument in arguments)
        {
            _startInfo.ArgumentList.Add(argument);
        }

        return this;
    }

    /// <summary>Sets the arguments for the process, replacing any previously set arguments.</summary>
    public ProcessWrapper WithArguments(IEnumerable<string> arguments)
    {
        _startInfo.ArgumentList.Clear();
        foreach (var argument in arguments)
        {
            _startInfo.ArgumentList.Add(argument);
        }

        return this;
    }

    /// <summary>Sets the working directory for the process.</summary>
    public ProcessWrapper WithWorkingDirectory(string workingDirectory)
    {
        _startInfo.WorkingDirectory = workingDirectory;
        return this;
    }

    /// <summary>Configures environment variables using a callback. Accumulates with previous calls.</summary>
    public ProcessWrapper WithEnvironmentVariables(Action<ProcessWrapperEnvironmentVariables> configure)
    {
        var builder = new ProcessWrapperEnvironmentVariables(_startInfo.Environment);
        configure(builder);

        return this;
    }

    /// <summary>Configures environment variables from a dictionary. A null value removes the variable. Accumulates with previous calls.</summary>
    public ProcessWrapper WithEnvironmentVariables(IReadOnlyDictionary<string, string?> variables)
    {
        foreach (var (name, value) in variables)
        {
            if (value is null)
            {
                _startInfo.Environment.Remove(name);
            }
            else
            {
                _startInfo.Environment[name] = value;
            }
        }

        return this;
    }

    /// <summary>Sets process validation rules.</summary>
    public ProcessWrapper WithValidation(ProcessValidationMode mode)
    {
        _validationMode = mode;
        return this;
    }

    /// <summary>Sets the process limits, replacing previously configured limits.</summary>
    public ProcessWrapper WithLimits(ProcessLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        _limits = limits;
        return this;
    }

    /// <summary>Configures process limits. Accumulates with previous calls.</summary>
    public ProcessWrapper WithLimits(Action<ProcessLimits> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        _limits ??= new ProcessLimits();
        configure(_limits);
        return this;
    }

    /// <summary>Configures the Windows Job Object used to apply process limits.</summary>
    public ProcessWrapper WithWindowsJobObject(Action<JobObject> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _windowsJobObjectConfiguration = configure;
        return this;
    }

    /// <summary>Configures the Linux cgroup used to apply process limits.</summary>
    public ProcessWrapper WithLinuxControlGroup(Action<CGroup2> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _linuxControlGroupConfiguration = configure;
        return this;
    }

    /// <summary>Replaces all output stream handlers with the specified handler.</summary>
    public ProcessWrapper WithOutputStream(Action<string> handler)
    {
        _outputHandlers = [handler];
        return this;
    }

    /// <summary>Replaces all output stream handlers with one that appends to the specified <see cref="StringBuilder"/>.</summary>
    public ProcessWrapper WithOutputStream(StringBuilder stringBuilder)
    {
        return WithOutputStream(line =>
        {
            lock (stringBuilder)
            {
                stringBuilder.AppendLine(line);
            }
        });
    }

    /// <summary>Replaces all output stream handlers with one that adds to the specified <see cref="ProcessOutputCollection"/>.</summary>
    public ProcessWrapper WithOutputStream(ProcessOutputCollection collection)
    {
        return WithOutputStream(line => collection.Add(ProcessOutputType.StandardOutput, line));
    }

    /// <summary>Adds an additional output stream handler.</summary>
    public ProcessWrapper AddOutputStream(Action<string> handler)
    {
        _outputHandlers = _outputHandlers.Add(handler);
        return this;
    }

    /// <summary>Adds an additional output stream handler that appends to the specified <see cref="StringBuilder"/>.</summary>
    public ProcessWrapper AddOutputStream(StringBuilder stringBuilder)
    {
        return AddOutputStream(line =>
        {
            lock (stringBuilder)
            {
                stringBuilder.AppendLine(line);
            }
        });
    }

    /// <summary>Adds an additional output stream handler that adds to the specified <see cref="ProcessOutputCollection"/>.</summary>
    public ProcessWrapper AddOutputStream(ProcessOutputCollection collection)
    {
        return AddOutputStream(line => collection.Add(ProcessOutputType.StandardOutput, line));
    }

    /// <summary>Replaces all error stream handlers with the specified handler.</summary>
    public ProcessWrapper WithErrorStream(Action<string> handler)
    {
        _errorHandlers = [handler];
        return this;
    }

    /// <summary>Replaces all error stream handlers with one that appends to the specified <see cref="StringBuilder"/>.</summary>
    public ProcessWrapper WithErrorStream(StringBuilder stringBuilder)
    {
        return WithErrorStream(line =>
        {
            lock (stringBuilder)
            {
                stringBuilder.AppendLine(line);
            }
        });
    }

    /// <summary>Replaces all error stream handlers with one that adds to the specified <see cref="ProcessOutputCollection"/>.</summary>
    public ProcessWrapper WithErrorStream(ProcessOutputCollection collection)
    {
        return WithErrorStream(line => collection.Add(ProcessOutputType.StandardError, line));
    }

    /// <summary>Adds an additional error stream handler.</summary>
    public ProcessWrapper AddErrorStream(Action<string> handler)
    {
        _errorHandlers = _errorHandlers.Add(handler);
        return this;
    }

    /// <summary>Adds an additional error stream handler that appends to the specified <see cref="StringBuilder"/>.</summary>
    public ProcessWrapper AddErrorStream(StringBuilder stringBuilder)
    {
        return AddErrorStream(line =>
        {
            lock (stringBuilder)
            {
                stringBuilder.AppendLine(line);
            }
        });
    }

    /// <summary>Adds an additional error stream handler that adds to the specified <see cref="ProcessOutputCollection"/>.</summary>
    public ProcessWrapper AddErrorStream(ProcessOutputCollection collection)
    {
        return AddErrorStream(line => collection.Add(ProcessOutputType.StandardError, line));
    }

    /// <summary>Sets the input stream to the specified <see cref="Stream"/>.</summary>
    public ProcessWrapper WithInputStream(Stream stream)
    {
        _inputStream = new ProcessInputStream.StreamInput(stream);
        return this;
    }

    /// <summary>Sets the input stream to the specified <see cref="TextReader"/>.</summary>
    public ProcessWrapper WithInputStream(TextReader reader)
    {
        _inputStream = new ProcessInputStream.TextReaderInput(reader);
        return this;
    }

    /// <summary>Sets the input stream to the specified string.</summary>
    public ProcessWrapper WithInputStream(string text)
    {
        _inputStream = new ProcessInputStream.StringInput(text);
        return this;
    }

    /// <summary>
    /// Starts the process and returns a <see cref="ProcessInstance"/> immediately.
    /// Await the returned instance to wait for the process to exit and get a <see cref="ProcessResult"/>.
    /// Use <see cref="ProcessInstance.Kill(bool)"/> to stop the process explicitly.
    /// </summary>
    public ProcessInstance ExecuteAsync(CancellationToken cancellationToken = default)
    {
        return StartProcess(_outputHandlers, _errorHandlers,
            (process, inputTask, registration, limiter, hasStandardErrorOutput, ct) => new ProcessInstance(process, inputTask, registration, limiter, _validationMode, hasStandardErrorOutput, ct),
            cancellationToken);
    }

    /// <summary>
    /// Starts the process with output buffering and returns a <see cref="BufferedProcessInstance"/> immediately.
    /// Await the returned instance to wait for the process to exit and get a <see cref="BufferedProcessResult"/>.
    /// Use <see cref="ProcessInstance.Kill(bool)"/> to stop the process explicitly.
    /// </summary>
    public BufferedProcessInstance ExecuteBufferedAsync(CancellationToken cancellationToken = default)
    {
        var output = new ProcessOutputCollection();

        var outputHandlers = _outputHandlers.Add(line => output.Add(ProcessOutputType.StandardOutput, line));
        var errorHandlers = _errorHandlers.Add(line => output.Add(ProcessOutputType.StandardError, line));

        return StartProcess(outputHandlers, errorHandlers,
            (process, inputTask, registration, limiter, hasStandardErrorOutput, ct) => new BufferedProcessInstance(process, inputTask, registration, limiter, _validationMode, output, hasStandardErrorOutput, ct),
            cancellationToken);
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "ProcessInstance will dispose it")]
    private T StartProcess<T>(ImmutableArray<Action<string>> outputHandlers, ImmutableArray<Action<string>> errorHandlers, Func<Process, Task, CancellationTokenRegistration, IDisposable?, Func<bool>, CancellationToken, T> factory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var hasOutputHandlers = !outputHandlers.IsEmpty;
        var shouldValidateErrorOutput = (_validationMode & ProcessValidationMode.FailIfStdError) == ProcessValidationMode.FailIfStdError;
        var hasErrorHandlers = !errorHandlers.IsEmpty || shouldValidateErrorOutput;
        var hasInputStream = _inputStream is not null;
        var hasStandardErrorOutput = 0;

        var configuredFileName = _startInfo.FileName;
        _startInfo.RedirectStandardOutput = hasOutputHandlers;
        _startInfo.RedirectStandardError = hasErrorHandlers;
        _startInfo.RedirectStandardInput = hasInputStream;
        _startInfo.FileName = ResolveFileName(configuredFileName, _startInfo.WorkingDirectory);

        var process = new Process { StartInfo = _startInfo };
        var processLimiter = CreateProcessLimiter();

        if (hasOutputHandlers)
        {
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                {
                    foreach (var handler in outputHandlers)
                    {
                        handler(e.Data);
                    }
                }
            };
        }

        if (hasErrorHandlers)
        {
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                {
                    Interlocked.Exchange(ref hasStandardErrorOutput, 1);
                    foreach (var handler in errorHandlers)
                    {
                        handler(e.Data);
                    }
                }
            };
        }

        var processStarted = false;
        try
        {
            try
            {
                if (!process.Start())
                {
                    throw new Win32Exception("Cannot start the process");
                }

                processStarted = true;
            }
            finally
            {
                _startInfo.FileName = configuredFileName;
            }

            processLimiter?.Apply(process);
        }
        catch
        {
            if (processStarted)
            {
                ProcessInstance.KillProcess(process, entireProcessTree: true);
            }

            process.Dispose();
            processLimiter?.Dispose();
            throw;
        }

        if (hasOutputHandlers)
        {
            process.BeginOutputReadLine();
        }

        if (hasErrorHandlers)
        {
            process.BeginErrorReadLine();
        }

        var inputStreamTask = Task.CompletedTask;
        if (_inputStream is not null)
        {
            var inputStream = _inputStream;
            inputStreamTask = Task.Run(async () =>
            {
                try
                {
                    await inputStream.WriteAsync(process.StandardInput, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    process.StandardInput.Close();
                }
            }, cancellationToken);
        }
        else if (hasInputStream)
        {
            process.StandardInput.Close();
        }

        var registration = default(CancellationTokenRegistration);
        if (cancellationToken.CanBeCanceled && !process.HasExited)
        {
            registration = cancellationToken.Register(() => ProcessInstance.KillProcess(process, entireProcessTree: true));
        }

        return factory(process, inputStreamTask, registration, processLimiter, () => Volatile.Read(ref hasStandardErrorOutput) != 0, cancellationToken);
    }

    private IProcessLimiter? CreateProcessLimiter()
    {
        var hasWindowsConfiguration = _windowsJobObjectConfiguration is not null;
        var hasLinuxConfiguration = _linuxControlGroupConfiguration is not null;
        var hasCommonLimits = _limits?.HasAnyLimitConfigured == true;

        if (!hasWindowsConfiguration && !hasLinuxConfiguration && !hasCommonLimits)
            return null;

        if (hasCommonLimits)
        {
            ValidateLimits(_limits!);
        }

        if (OperatingSystem.IsWindows())
        {
            if (hasLinuxConfiguration)
                throw new PlatformNotSupportedException("Linux control group configuration can be used only on Linux.");

            return new WindowsProcessLimiter(_limits, _windowsJobObjectConfiguration);
        }

        if (OperatingSystem.IsLinux())
        {
            if (hasWindowsConfiguration)
                throw new PlatformNotSupportedException("Windows job object configuration can be used only on Windows.");

            return new LinuxProcessLimiter(_limits, _linuxControlGroupConfiguration);
        }

        throw new PlatformNotSupportedException("Process limits are supported only on Windows and Linux.");
    }

    private static void ValidateLimits(ProcessLimits limits)
    {
        if (limits.CpuPercentage is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(limits.CpuPercentage), "CPU percentage must be between 1 and 100.");

        if (limits.MemoryLimitInBytes is <= 0)
            throw new ArgumentOutOfRangeException(nameof(limits.MemoryLimitInBytes), "Memory limit must be greater than 0.");

        if (limits.ProcessCountLimit is <= 0)
            throw new ArgumentOutOfRangeException(nameof(limits.ProcessCountLimit), "Process count limit must be greater than 0.");
    }

    private interface IProcessLimiter : IDisposable
    {
        void Apply(Process process);
    }

    private sealed class WindowsProcessLimiter : IProcessLimiter
    {
        private readonly JobObject _jobObject;

        public WindowsProcessLimiter(ProcessLimits? limits, Action<JobObject>? configure)
        {
            _jobObject = new JobObject();

            if (limits?.MemoryLimitInBytes is not null || limits?.ProcessCountLimit is not null)
            {
                var jobLimits = new JobObjectLimits();

                if (limits.MemoryLimitInBytes is not null)
                {
                    jobLimits.JobMemoryLimit = checked((nuint)limits.MemoryLimitInBytes.Value);
                }

                if (limits.ProcessCountLimit is not null)
                {
                    jobLimits.ActiveProcessLimit = checked((uint)limits.ProcessCountLimit.Value);
                }

                _jobObject.SetLimits(jobLimits);
            }

            if (limits?.CpuPercentage is not null)
            {
                _jobObject.SetCpuRateHardCap(limits.CpuPercentage.Value * 100);
            }

            configure?.Invoke(_jobObject);
        }

        public void Apply(Process process)
        {
            ArgumentNullException.ThrowIfNull(process);
            _jobObject.AssignProcess(process);
        }

        public void Dispose()
        {
            _jobObject.Dispose();
        }
    }

    private sealed class LinuxProcessLimiter : IProcessLimiter
    {
        private readonly ProcessLimits? _limits;
        private readonly Action<CGroup2>? _configure;
        private CGroup2? _parentControlGroup;
        private CGroup2? _controlGroup;

        public LinuxProcessLimiter(ProcessLimits? limits, Action<CGroup2>? configure)
        {
            _limits = limits;
            _configure = configure;
        }

        public void Apply(Process process)
        {
            ArgumentNullException.ThrowIfNull(process);

            _parentControlGroup = CGroup2.Root.CreateOrGetChild($"process-wrapper-{Environment.ProcessId}-{Guid.NewGuid():N}");
            var availableControllers = _parentControlGroup.GetAvailableControllers().ToHashSet(StringComparer.Ordinal);
            var controllersToEnable = new HashSet<string>(StringComparer.Ordinal);

            if (_limits?.CpuPercentage is not null)
            {
                EnsureControllerIsAvailable(availableControllers, "cpu");
                controllersToEnable.Add("cpu");
            }

            if (_limits?.MemoryLimitInBytes is not null)
            {
                EnsureControllerIsAvailable(availableControllers, "memory");
                controllersToEnable.Add("memory");
            }

            if (_limits?.ProcessCountLimit is not null)
            {
                EnsureControllerIsAvailable(availableControllers, "pids");
                controllersToEnable.Add("pids");
            }

            if (_configure is not null)
            {
                foreach (var controller in availableControllers)
                {
                    controllersToEnable.Add(controller);
                }
            }

            if (controllersToEnable.Count > 0)
            {
                _parentControlGroup.SetControllers(controllersToEnable.ToArray());
            }

            _controlGroup = _parentControlGroup.CreateOrGetChild($"process-{Guid.NewGuid():N}");

            if (_limits?.CpuPercentage is not null)
            {
                const long periodMicroseconds = 100000;
                var maxMicroseconds = (long)Math.Ceiling(_limits.CpuPercentage.Value * periodMicroseconds / 100d);
                _controlGroup.SetCpuMax(maxMicroseconds, periodMicroseconds);
            }

            if (_limits?.MemoryLimitInBytes is not null)
            {
                _controlGroup.SetMemoryMax(_limits.MemoryLimitInBytes.Value);
            }

            if (_limits?.ProcessCountLimit is not null)
            {
                _controlGroup.SetPidsMax(_limits.ProcessCountLimit.Value);
            }

            _configure?.Invoke(_controlGroup);
            _controlGroup.AssociateProcess(process);
        }

        public void Dispose()
        {
            var controlGroup = Interlocked.Exchange(ref _controlGroup, null);
            TryDeleteControlGroup(controlGroup);

            var parentControlGroup = Interlocked.Exchange(ref _parentControlGroup, null);
            TryDeleteControlGroup(parentControlGroup);
        }

        private static void EnsureControllerIsAvailable(HashSet<string> availableControllers, string controllerName)
        {
            if (!availableControllers.Contains(controllerName))
                throw new NotSupportedException($"The '{controllerName}' cgroup controller is not available.");
        }

        private static void TryDeleteControlGroup(CGroup2? controlGroup)
        {
            if (controlGroup is null)
                return;

            try
            {
                if (controlGroup.Exists())
                {
                    controlGroup.Delete();
                }
            }
            catch (DirectoryNotFoundException)
            {
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static string ResolveFileName(string fileName, string? workingDirectory)
    {
        var normalizedWorkingDirectory = string.IsNullOrEmpty(workingDirectory) ? null : workingDirectory;
        return ExecutableFinder.GetFullExecutablePath(fileName, normalizedWorkingDirectory) ?? fileName;
    }
}
