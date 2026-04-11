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
    private ImmutableArray<Action<string>> _outputHandlers;
    private ImmutableArray<Stream> _outputBinaryHandlers;
    private ImmutableArray<Action<string>> _errorHandlers;
    private ImmutableArray<Stream> _errorBinaryHandlers;
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
        _outputBinaryHandlers = [];
        _errorHandlers = [];
        _errorBinaryHandlers = [];
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

    /// <summary>Replaces all output stream handlers with the specified handlers.</summary>
    public ProcessWrapper WithOutputStream(params ReadOnlySpan<Action<string>> handlers)
    {
        _outputHandlers = CreateImmutableArray(handlers, nameof(handlers));
        _outputBinaryHandlers = [];
        return this;
    }

    /// <summary>Replaces all binary output stream handlers with the specified streams.</summary>
    public ProcessWrapper WithOutputStream(params ReadOnlySpan<Stream> streams)
    {
        _outputHandlers = [];
        _outputBinaryHandlers = CreateImmutableArray(streams, nameof(streams));
        return this;
    }

    /// <summary>Replaces all output stream handlers with one that appends to the specified <see cref="StringBuilder"/>.</summary>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Output stream handlers are managed for the process lifetime.")]
    public ProcessWrapper WithOutputStream(StringBuilder stringBuilder)
    {
        return WithOutputStream(CreateStringBuilderOutputStream(stringBuilder));
    }

    /// <summary>Replaces all output stream handlers with one that adds to the specified <see cref="ProcessOutputCollection"/>.</summary>
    public ProcessWrapper WithOutputStream(ProcessOutputCollection collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        return WithOutputStream(line => collection.Add(ProcessOutputType.StandardOutput, line));
    }

    /// <summary>Adds additional output stream handlers.</summary>
    public ProcessWrapper AddOutputStream(params ReadOnlySpan<Action<string>> handlers)
    {
        _outputHandlers = AddToImmutableArray(_outputHandlers, handlers, nameof(handlers));
        return this;
    }

    /// <summary>Adds additional binary output stream handlers.</summary>
    public ProcessWrapper AddOutputStream(params ReadOnlySpan<Stream> streams)
    {
        _outputBinaryHandlers = AddToImmutableArray(_outputBinaryHandlers, streams, nameof(streams));
        return this;
    }

    /// <summary>Adds an additional output stream handler that appends to the specified <see cref="StringBuilder"/>.</summary>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Output stream handlers are managed for the process lifetime.")]
    public ProcessWrapper AddOutputStream(StringBuilder stringBuilder)
    {
        return AddOutputStream(CreateStringBuilderOutputStream(stringBuilder));
    }

    /// <summary>Adds an additional output stream handler that adds to the specified <see cref="ProcessOutputCollection"/>.</summary>
    public ProcessWrapper AddOutputStream(ProcessOutputCollection collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        return AddOutputStream(line => collection.Add(ProcessOutputType.StandardOutput, line));
    }

    /// <summary>Replaces all error stream handlers with the specified handlers.</summary>
    public ProcessWrapper WithErrorStream(params ReadOnlySpan<Action<string>> handlers)
    {
        _errorHandlers = CreateImmutableArray(handlers, nameof(handlers));
        _errorBinaryHandlers = [];
        return this;
    }

    /// <summary>Replaces all binary error stream handlers with the specified streams.</summary>
    public ProcessWrapper WithErrorStream(params ReadOnlySpan<Stream> streams)
    {
        _errorHandlers = [];
        _errorBinaryHandlers = CreateImmutableArray(streams, nameof(streams));
        return this;
    }

    /// <summary>Replaces all error stream handlers with one that appends to the specified <see cref="StringBuilder"/>.</summary>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Output stream handlers are managed for the process lifetime.")]
    public ProcessWrapper WithErrorStream(StringBuilder stringBuilder)
    {
        return WithErrorStream(CreateStringBuilderOutputStream(stringBuilder));
    }

    /// <summary>Replaces all error stream handlers with one that adds to the specified <see cref="ProcessOutputCollection"/>.</summary>
    public ProcessWrapper WithErrorStream(ProcessOutputCollection collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        return WithErrorStream(line => collection.Add(ProcessOutputType.StandardError, line));
    }

    /// <summary>Adds additional error stream handlers.</summary>
    public ProcessWrapper AddErrorStream(params ReadOnlySpan<Action<string>> handlers)
    {
        _errorHandlers = AddToImmutableArray(_errorHandlers, handlers, nameof(handlers));
        return this;
    }

    /// <summary>Adds additional binary error stream handlers.</summary>
    public ProcessWrapper AddErrorStream(params ReadOnlySpan<Stream> streams)
    {
        _errorBinaryHandlers = AddToImmutableArray(_errorBinaryHandlers, streams, nameof(streams));
        return this;
    }

    /// <summary>Adds an additional error stream handler that appends to the specified <see cref="StringBuilder"/>.</summary>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Output stream handlers are managed for the process lifetime.")]
    public ProcessWrapper AddErrorStream(StringBuilder stringBuilder)
    {
        return AddErrorStream(CreateStringBuilderOutputStream(stringBuilder));
    }

    /// <summary>Adds an additional error stream handler that adds to the specified <see cref="ProcessOutputCollection"/>.</summary>
    public ProcessWrapper AddErrorStream(ProcessOutputCollection collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
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
        return StartProcess(_outputHandlers, _errorHandlers, _outputBinaryHandlers, _errorBinaryHandlers,
            (process, inputTask, outputTask, registration, limiter, hasStandardErrorOutput, ct) => new ProcessInstance(process, inputTask, outputTask, registration, limiter, _validationMode, hasStandardErrorOutput, ct),
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

        return StartProcess(outputHandlers, errorHandlers, _outputBinaryHandlers, _errorBinaryHandlers,
            (process, inputTask, outputTask, registration, limiter, hasStandardErrorOutput, ct) => new BufferedProcessInstance(process, inputTask, outputTask, registration, limiter, _validationMode, output, hasStandardErrorOutput, ct),
            cancellationToken);
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "ProcessInstance will dispose it")]
    private T StartProcess<T>(ImmutableArray<Action<string>> outputHandlers, ImmutableArray<Action<string>> errorHandlers, ImmutableArray<Stream> outputBinaryHandlers, ImmutableArray<Stream> errorBinaryHandlers, Func<Process, Task, Task, CancellationTokenRegistration, IDisposable?, Func<bool>, CancellationToken, T> factory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var hasOutputTextHandlers = !outputHandlers.IsEmpty;
        var hasOutputBinaryHandlers = !outputBinaryHandlers.IsEmpty;
        var hasOutputHandlers = hasOutputTextHandlers || hasOutputBinaryHandlers;
        var shouldValidateErrorOutput = (_validationMode & ProcessValidationMode.FailIfStdError) == ProcessValidationMode.FailIfStdError;
        var hasErrorTextHandlers = !errorHandlers.IsEmpty;
        var hasErrorBinaryHandlers = !errorBinaryHandlers.IsEmpty;
        var hasErrorHandlers = hasErrorTextHandlers || hasErrorBinaryHandlers || shouldValidateErrorOutput;
        var hasInputStream = _inputStream is not null;
        var hasStandardErrorOutput = 0;

        var configuredFileName = _startInfo.FileName;
        _startInfo.RedirectStandardOutput = hasOutputHandlers;
        _startInfo.RedirectStandardError = hasErrorHandlers;
        _startInfo.RedirectStandardInput = hasInputStream;
        _startInfo.FileName = ResolveFileName(configuredFileName, _startInfo.WorkingDirectory);

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
            outputStreamTask = PumpStreamAsync(process.StandardOutput.BaseStream, process.StandardOutput.CurrentEncoding, outputHandlers, outputBinaryHandlers, onDataRead: null);
        }

        var errorStreamTask = Task.CompletedTask;
        if (hasErrorHandlers)
        {
            errorStreamTask = PumpStreamAsync(process.StandardError.BaseStream, process.StandardError.CurrentEncoding, errorHandlers, errorBinaryHandlers, onDataRead: () => Interlocked.Exchange(ref hasStandardErrorOutput, 1));
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

        return factory(process, inputStreamTask, Task.WhenAll(outputStreamTask, errorStreamTask), registration, processLimiter, () => Volatile.Read(ref hasStandardErrorOutput) != 0, cancellationToken);
    }

    private static async Task PumpStreamAsync(Stream stream, Encoding encoding, ImmutableArray<Action<string>> lineHandlers, ImmutableArray<Stream> binaryHandlers, Action? onDataRead)
    {
        InitializeBinaryHandlers(binaryHandlers, encoding);

        if (lineHandlers.IsEmpty)
        {
            await CopyStreamToBinaryHandlersAsync(stream, binaryHandlers, onDataRead).ConfigureAwait(false);
            return;
        }

        await PumpMixedTextAndBinaryStreamAsync(stream, encoding, lineHandlers, binaryHandlers, onDataRead).ConfigureAwait(false);
    }

    private static async Task CopyStreamToBinaryHandlersAsync(Stream stream, ImmutableArray<Stream> binaryHandlers, Action? onDataRead)
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
            var data = buffer.AsMemory(0, bytesRead);
            foreach (var binaryHandler in binaryHandlers)
            {
                await binaryHandler.WriteAsync(data).ConfigureAwait(false);
            }
        }

        foreach (var binaryHandler in binaryHandlers)
        {
            await binaryHandler.FlushAsync().ConfigureAwait(false);
        }
    }

    private static async Task PumpMixedTextAndBinaryStreamAsync(Stream stream, Encoding encoding, ImmutableArray<Action<string>> lineHandlers, ImmutableArray<Stream> binaryHandlers, Action? onDataRead)
    {
        var decoder = encoding.GetDecoder();
        var buffer = new byte[4096];
        var chars = new char[encoding.GetMaxCharCount(buffer.Length)];
        var lineBuilder = new StringBuilder();
        var lastCharacterWasCarriageReturn = false;

        while (true)
        {
            var bytesRead = await ReadBufferAsync(stream, buffer.AsMemory()).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                break;
            }

            onDataRead?.Invoke();
            var data = buffer.AsMemory(0, bytesRead);
            foreach (var binaryHandler in binaryHandlers)
            {
                await binaryHandler.WriteAsync(data).ConfigureAwait(false);
            }

            var charsRead = decoder.GetChars(buffer, 0, bytesRead, chars, 0, flush: false);
            DispatchLines(chars.AsSpan(0, charsRead), lineHandlers, lineBuilder, ref lastCharacterWasCarriageReturn);
        }

        var finalCharsRead = decoder.GetChars(Array.Empty<byte>(), 0, 0, chars, 0, flush: true);
        DispatchLines(chars.AsSpan(0, finalCharsRead), lineHandlers, lineBuilder, ref lastCharacterWasCarriageReturn);

        if (lastCharacterWasCarriageReturn || lineBuilder.Length > 0)
        {
            DispatchLine(lineHandlers, lineBuilder);
        }

        foreach (var binaryHandler in binaryHandlers)
        {
            await binaryHandler.FlushAsync().ConfigureAwait(false);
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

    private static void DispatchLines(ReadOnlySpan<char> chars, ImmutableArray<Action<string>> lineHandlers, StringBuilder lineBuilder, ref bool lastCharacterWasCarriageReturn)
    {
        foreach (var character in chars)
        {
            if (lastCharacterWasCarriageReturn)
            {
                lastCharacterWasCarriageReturn = false;
                if (character == '\n')
                {
                    continue;
                }
            }

            if (character == '\r')
            {
                DispatchLine(lineHandlers, lineBuilder);
                lastCharacterWasCarriageReturn = true;
                continue;
            }

            if (character == '\n')
            {
                DispatchLine(lineHandlers, lineBuilder);
                continue;
            }

            lineBuilder.Append(character);
        }
    }

    private static void DispatchLine(ImmutableArray<Action<string>> lineHandlers, StringBuilder lineBuilder)
    {
        var line = lineBuilder.ToString();
        lineBuilder.Clear();
        foreach (var lineHandler in lineHandlers)
        {
            lineHandler(line);
        }
    }

    private static void InitializeBinaryHandlers(ImmutableArray<Stream> binaryHandlers, Encoding encoding)
    {
        foreach (var binaryHandler in binaryHandlers)
        {
            if (binaryHandler is StringBuilderOutputStream stringBuilderOutputStream)
            {
                stringBuilderOutputStream.SetEncoding(encoding);
            }
        }
    }

    private static StringBuilderOutputStream CreateStringBuilderOutputStream(StringBuilder stringBuilder)
    {
        ArgumentNullException.ThrowIfNull(stringBuilder);
        return new StringBuilderOutputStream(stringBuilder);
    }

    private static ImmutableArray<T> CreateImmutableArray<T>(ReadOnlySpan<T> values, string parameterName)
        where T : class
    {
        if (values.IsEmpty)
            return [];

        var builder = ImmutableArray.CreateBuilder<T>(values.Length);
        foreach (var value in values)
        {
            ArgumentNullException.ThrowIfNull(value, parameterName);
            builder.Add(value);
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<T> AddToImmutableArray<T>(ImmutableArray<T> existingValues, ReadOnlySpan<T> values, string parameterName)
        where T : class
    {
        if (values.IsEmpty)
            return existingValues;

        var builder = existingValues.ToBuilder();
        foreach (var value in values)
        {
            ArgumentNullException.ThrowIfNull(value, parameterName);
            builder.Add(value);
        }

        return builder.ToImmutable();
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
