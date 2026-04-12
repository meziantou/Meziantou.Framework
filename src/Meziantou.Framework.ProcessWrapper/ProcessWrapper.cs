using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
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
    private ImmutableArray<OutputTarget> _outputTargets;
    private ImmutableArray<OutputTarget> _errorTargets;
    private InputSource? _inputSource;
    private ProcessLimits? _limits;
    private Action<JobObject>? _windowsJobObjectConfiguration;
    private Action<CGroup2>? _linuxControlGroupConfiguration;

    private ProcessWrapper(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        _startInfo = CreateStartInfo(fileName);
        _validationMode = ProcessValidationMode.FailIfNonZeroExitCode;
        _outputTargets = [];
        _errorTargets = [];
    }

    private ProcessWrapper(ProcessWrapper other)
    {
        ArgumentNullException.ThrowIfNull(other);

        _startInfo = CloneStartInfo(other._startInfo);
        _validationMode = other._validationMode;
        _outputTargets = other._outputTargets;
        _errorTargets = other._errorTargets;
        _inputSource = other._inputSource;
        _limits = other._limits;
        _windowsJobObjectConfiguration = other._windowsJobObjectConfiguration;
        _linuxControlGroupConfiguration = other._linuxControlGroupConfiguration;
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

    private static ProcessStartInfo CloneStartInfo(ProcessStartInfo source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var startInfo = CreateStartInfo(source.FileName);
        startInfo.WorkingDirectory = source.WorkingDirectory;
        startInfo.StandardOutputEncoding = source.StandardOutputEncoding;
        startInfo.StandardErrorEncoding = source.StandardErrorEncoding;

        foreach (var argument in source.ArgumentList)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var (name, value) in source.Environment)
        {
            startInfo.Environment[name] = value;
        }

        return startInfo;
    }

    /// <summary>Creates a new <see cref="ProcessWrapper"/> for the specified executable.</summary>
    public static ProcessWrapper Create(string fileName) => new(fileName);

    /// <summary>Creates a pipeline between 2 commands.</summary>
    public static ProcessPipeline operator |(ProcessWrapper left, ProcessWrapper right) => ProcessPipeline.Create(left, right);

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

    /// <summary>Sets the encoding used to decode standard output.</summary>
    public ProcessWrapper WithOutputEncoding(Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(encoding);
        _startInfo.StandardOutputEncoding = encoding;
        return this;
    }

    /// <summary>Sets the encoding used to decode standard error.</summary>
    public ProcessWrapper WithErrorEncoding(Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(encoding);
        _startInfo.StandardErrorEncoding = encoding;
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

    /// <summary>Replaces all output stream handlers with the specified targets.</summary>
    public ProcessWrapper WithOutputStream(params ReadOnlySpan<OutputTarget> targets)
    {
        _outputTargets = CreateOutputTargets(targets, ProcessOutputType.StandardOutput, nameof(targets));
        return this;
    }

    /// <summary>Adds additional output stream handlers.</summary>
    public ProcessWrapper AddOutputStream(params ReadOnlySpan<OutputTarget> targets)
    {
        var outputTargets = CreateOutputTargets(targets, ProcessOutputType.StandardOutput, nameof(targets));
        _outputTargets = AddToImmutableArray(_outputTargets, outputTargets);
        return this;
    }

    /// <summary>Replaces all error stream handlers with the specified targets.</summary>
    public ProcessWrapper WithErrorStream(params ReadOnlySpan<OutputTarget> targets)
    {
        _errorTargets = CreateOutputTargets(targets, ProcessOutputType.StandardError, nameof(targets));
        return this;
    }

    /// <summary>Adds additional error stream handlers.</summary>
    public ProcessWrapper AddErrorStream(params ReadOnlySpan<OutputTarget> targets)
    {
        var errorTargets = CreateOutputTargets(targets, ProcessOutputType.StandardError, nameof(targets));
        _errorTargets = AddToImmutableArray(_errorTargets, errorTargets);
        return this;
    }

    /// <summary>Sets the input stream to the specified input source.</summary>
    public ProcessWrapper WithInputStream(InputSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _inputSource = source;
        return this;
    }

    internal bool HasInputSource => _inputSource is not null;

    internal ProcessWrapper Clone()
    {
        return new ProcessWrapper(this);
    }

    /// <summary>
    /// Starts the process and returns a <see cref="ProcessInstance"/> immediately.
    /// Await the returned instance to wait for the process to exit and get a <see cref="ProcessResult"/>.
    /// Use <see cref="ProcessInstance.Kill(bool)"/> to stop the process explicitly.
    /// </summary>
    public ProcessInstance ExecuteAsync(CancellationToken cancellationToken = default)
    {
        return StartProcess(_outputTargets, _errorTargets,
            (process, inputTask, outputTask, registration, limiter, hasStandardErrorOutput, activity, ct) => new ProcessInstance(process, inputTask, outputTask, registration, limiter, _validationMode, hasStandardErrorOutput, activity, ct),
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

        var outputTargets = _outputTargets.Add(OutputTarget.ToProcessOutputCollection(output).ForOutputType(ProcessOutputType.StandardOutput));
        var errorTargets = _errorTargets.Add(OutputTarget.ToProcessOutputCollection(output).ForOutputType(ProcessOutputType.StandardError));

        return StartProcess(outputTargets, errorTargets,
            (process, inputTask, outputTask, registration, limiter, hasStandardErrorOutput, activity, ct) => new BufferedProcessInstance(process, inputTask, outputTask, registration, limiter, _validationMode, output, hasStandardErrorOutput, activity, ct),
            cancellationToken);
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "ProcessInstance will dispose it")]
    private T StartProcess<T>(ImmutableArray<OutputTarget> outputTargets, ImmutableArray<OutputTarget> errorTargets, Func<Process, Task, Task, CancellationTokenRegistration, IDisposable?, Func<bool>, Activity?, CancellationToken, T> factory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var hasOutputHandlers = !outputTargets.IsEmpty;
        var shouldValidateErrorOutput = (_validationMode & ProcessValidationMode.FailIfStdError) == ProcessValidationMode.FailIfStdError;
        var hasErrorHandlers = !errorTargets.IsEmpty || shouldValidateErrorOutput;
        var hasInputStream = _inputSource is not null;
        var hasStandardErrorOutput = 0;

        var configuredFileName = _startInfo.FileName;
        var resolvedFileName = ResolveFileName(configuredFileName, _startInfo.WorkingDirectory);
        _startInfo.RedirectStandardOutput = hasOutputHandlers;
        _startInfo.RedirectStandardError = hasErrorHandlers;
        _startInfo.RedirectStandardInput = hasInputStream;
        _startInfo.FileName = resolvedFileName;

        var process = new Process { StartInfo = _startInfo };
        var processLimiter = CreateProcessLimiter();

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

        var outputStreamTask = Task.CompletedTask;
        if (hasOutputHandlers)
        {
            outputStreamTask = PumpStreamAsync(process.StandardOutput.BaseStream, process.StandardOutput.CurrentEncoding, outputTargets, onDataRead: null);
        }

        var errorStreamTask = Task.CompletedTask;
        if (hasErrorHandlers)
        {
            errorStreamTask = PumpStreamAsync(process.StandardError.BaseStream, process.StandardError.CurrentEncoding, errorTargets, onDataRead: () => Interlocked.Exchange(ref hasStandardErrorOutput, 1));
        }

        var inputStreamTask = Task.CompletedTask;
        if (_inputSource is not null)
        {
            var inputSource = _inputSource;
            inputStreamTask = Task.Run(() =>
            {
                var buffer = new byte[4096];
                try
                {
                    while (true)
                    {
                        var bytesRead = inputSource.Read(buffer);
                        if (bytesRead <= 0)
                        {
                            break;
                        }

                        process.StandardInput.BaseStream.Write(buffer, 0, bytesRead);
                    }

                    process.StandardInput.BaseStream.Flush();
                }
                finally
                {
                    inputSource.NotifyProcessCompleted();
                    process.StandardInput.Close();
                }
            }, cancellationToken);
        }

        var registration = default(CancellationTokenRegistration);
        if (cancellationToken.CanBeCanceled && !process.HasExited)
        {
            registration = cancellationToken.Register(() => ProcessInstance.KillProcess(process, entireProcessTree: true));
        }

        var activity = ProcessWrapperTelemetry.ActivitySource.StartActivity("process.execute");
        activity?.SetTag("process.executable.path", resolvedFileName);

        return factory(process, inputStreamTask, Task.WhenAll(outputStreamTask, errorStreamTask), registration, processLimiter, () => Volatile.Read(ref hasStandardErrorOutput) != 0, activity, cancellationToken);
    }

    private static async Task PumpStreamAsync(Stream stream, Encoding encoding, ImmutableArray<OutputTarget> targets, Action? onDataRead)
    {
        InitializeTargets(targets, encoding);
        try
        {
            var buffer = new byte[4096];
            while (true)
            {
                var bytesRead = await ReadBufferAsync(stream, buffer.AsMemory()).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    break;
                }

                onDataRead?.Invoke();
                var data = buffer.AsSpan(0, bytesRead);
                foreach (var target in targets)
                {
                    target.Write(data);
                }
            }
        }
        finally
        {
            foreach (var target in targets)
            {
                target.NotifyProcessCompleted();
            }
        }
    }

    private static async ValueTask<int> ReadBufferAsync(Stream stream, Memory<byte> buffer)
    {
        try
        {
            return await stream.ReadAsync(buffer).ConfigureAwait(false);
        }
        catch (IOException)
        {
            // Match Process.AsyncStreamReader behavior and treat cancellation-related read failures as EOF.
            return 0;
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
    }

    private static void InitializeTargets(ImmutableArray<OutputTarget> targets, Encoding encoding)
    {
        foreach (var target in targets)
        {
            target.SetEncoding(encoding);
        }
    }

    private static ImmutableArray<OutputTarget> CreateOutputTargets(ReadOnlySpan<OutputTarget> targets, ProcessOutputType outputType, string parameterName)
    {
        if (targets.IsEmpty)
            return [];

        var outputTargets = ImmutableArray.CreateBuilder<OutputTarget>(targets.Length);
        foreach (var target in targets)
        {
            ArgumentNullException.ThrowIfNull(target, parameterName);
            outputTargets.Add(target.ForOutputType(outputType));
        }

        return outputTargets.ToImmutable();
    }

    private static ImmutableArray<T> AddToImmutableArray<T>(ImmutableArray<T> existingValues, ImmutableArray<T> values)
    {
        if (values.IsEmpty)
            return existingValues;

        return existingValues.AddRange(values);
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

        if (OperatingSystem.IsWindowsVersionAtLeast(5, 1, 2600))
        {
            if (hasLinuxConfiguration)
                throw new PlatformNotSupportedException("Linux control group configuration can be used only on Linux.");

            return new WindowsProcessLimiter(_limits, _windowsJobObjectConfiguration);
        }

        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows process limits are supported only on Windows 5.1.2600 and later.");
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
            throw new ArgumentOutOfRangeException(nameof(limits), limits.CpuPercentage, $"{nameof(ProcessLimits.CpuPercentage)} must be between 1 and 100.");

        if (limits.MemoryLimitInBytes is <= 0)
            throw new ArgumentOutOfRangeException(nameof(limits), limits.MemoryLimitInBytes, $"{nameof(ProcessLimits.MemoryLimitInBytes)} must be greater than 0.");

        if (limits.ProcessCountLimit is <= 0)
            throw new ArgumentOutOfRangeException(nameof(limits), limits.ProcessCountLimit, $"{nameof(ProcessLimits.ProcessCountLimit)} must be greater than 0.");
    }

    private interface IProcessLimiter : IDisposable
    {
        void Apply(Process process);
    }

    [SupportedOSPlatform("windows5.1.2600")]
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

    [SupportedOSPlatform("linux")]
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
                const long PeriodMicroseconds = 100000;
                var maxMicroseconds = (long)Math.Ceiling(_limits.CpuPercentage.Value * PeriodMicroseconds / 100d);
                _controlGroup.SetCpuMax(maxMicroseconds, PeriodMicroseconds);
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
