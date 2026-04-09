using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;

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
    /// Use <see cref="ProcessInstance.Kill()"/> or <see cref="ProcessInstance.Kill(bool)"/> to stop the process explicitly.
    /// </summary>
    public ProcessInstance ExecuteAsync(CancellationToken cancellationToken = default)
    {
        return StartProcess(_outputHandlers, _errorHandlers,
            (process, inputTask, registration, hasStandardErrorOutput, ct) => new ProcessInstance(process, inputTask, registration, _validationMode, hasStandardErrorOutput, ct),
            cancellationToken);
    }

    /// <summary>
    /// Starts the process with output buffering and returns a <see cref="BufferedProcessInstance"/> immediately.
    /// Await the returned instance to wait for the process to exit and get a <see cref="BufferedProcessResult"/>.
    /// Use <see cref="ProcessInstance.Kill()"/> or <see cref="ProcessInstance.Kill(bool)"/> to stop the process explicitly.
    /// </summary>
    public BufferedProcessInstance ExecuteBufferedAsync(CancellationToken cancellationToken = default)
    {
        var output = new ProcessOutputCollection();

        var outputHandlers = _outputHandlers.Add(line => output.Add(ProcessOutputType.StandardOutput, line));
        var errorHandlers = _errorHandlers.Add(line => output.Add(ProcessOutputType.StandardError, line));

        return StartProcess(outputHandlers, errorHandlers,
            (process, inputTask, registration, hasStandardErrorOutput, ct) => new BufferedProcessInstance(process, inputTask, registration, _validationMode, output, hasStandardErrorOutput, ct),
            cancellationToken);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000")]
    private T StartProcess<T>(ImmutableArray<Action<string>> outputHandlers, ImmutableArray<Action<string>> errorHandlers, Func<Process, Task, CancellationTokenRegistration, Func<bool>, CancellationToken, T> factory, CancellationToken cancellationToken)
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

        try
        {
            if (!process.Start())
            {
                throw new Win32Exception("Cannot start the process");
            }
        }
        finally
        {
            _startInfo.FileName = configuredFileName;
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
            registration = cancellationToken.Register(() => ProcessInstance.KillProcess(process));
        }

        return factory(process, inputStreamTask, registration, () => Volatile.Read(ref hasStandardErrorOutput) != 0, cancellationToken);
    }

    private static string ResolveFileName(string fileName, string? workingDirectory)
    {
        var normalizedWorkingDirectory = string.IsNullOrEmpty(workingDirectory) ? null : workingDirectory;
        return ExecutableFinder.GetFullExecutablePath(fileName, normalizedWorkingDirectory) ?? fileName;
    }
}
