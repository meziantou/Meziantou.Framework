using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Meziantou.Framework;

/// <summary>
/// Fluent, immutable builder for configuring and running processes.
/// Every <c>With*</c> method returns a new instance with the replaced value.
/// Every <c>Add*</c> method returns a new instance with the appended value.
/// </summary>
public sealed class ProcessWrapper
{
    private readonly string _fileName;
    private ImmutableArray<string> _arguments;
    private string? _workingDirectory;
    private ImmutableArray<EnvironmentVariableAction> _envActions;
    private ExitCodeValidationMode _exitCodeValidation;
    private ImmutableArray<Action<string>> _outputHandlers;
    private ImmutableArray<Action<string>> _errorHandlers;
    private ProcessInputStream? _inputStream;

    private ProcessWrapper(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        _fileName = fileName;
        _arguments = [];
        _envActions = [];
        _exitCodeValidation = ExitCodeValidationMode.FailIfNotZero;
        _outputHandlers = [];
        _errorHandlers = [];
    }

    private ProcessWrapper(ProcessWrapper other)
    {
        _fileName = other._fileName;
        _arguments = other._arguments;
        _workingDirectory = other._workingDirectory;
        _envActions = other._envActions;
        _exitCodeValidation = other._exitCodeValidation;
        _outputHandlers = other._outputHandlers;
        _errorHandlers = other._errorHandlers;
        _inputStream = other._inputStream;
    }

    /// <summary>Creates a new <see cref="ProcessWrapper"/> for the specified executable.</summary>
    public static ProcessWrapper Create(string fileName) => new(fileName);

    /// <summary>Sets the arguments for the process, replacing any previously set arguments.</summary>
    public ProcessWrapper WithArguments(params string[] arguments)
    {
        return new ProcessWrapper(this) { _arguments = [.. arguments] };
    }

    /// <summary>Sets the arguments for the process, replacing any previously set arguments.</summary>
    public ProcessWrapper WithArguments(IEnumerable<string> arguments)
    {
        return new ProcessWrapper(this) { _arguments = [.. arguments] };
    }

    /// <summary>Sets the working directory for the process.</summary>
    public ProcessWrapper WithWorkingDirectory(string workingDirectory)
    {
        return new ProcessWrapper(this) { _workingDirectory = workingDirectory };
    }

    /// <summary>Configures environment variables using a callback. Accumulates with previous calls.</summary>
    public ProcessWrapper WithEnvironmentVariables(Action<ProcessWrapperEnvironmentVariables> configure)
    {
        var builder = new ProcessWrapperEnvironmentVariables();
        configure(builder);
        return new ProcessWrapper(this) { _envActions = _envActions.AddRange(builder.Actions) };
    }

    /// <summary>Configures environment variables from a dictionary. A null value removes the variable. Accumulates with previous calls.</summary>
    public ProcessWrapper WithEnvironmentVariables(IReadOnlyDictionary<string, string?> variables)
    {
        var actions = _envActions;
        foreach (var (name, value) in variables)
        {
            actions = actions.Add(new EnvironmentVariableAction(name, value));
        }

        return new ProcessWrapper(this) { _envActions = actions };
    }

    /// <summary>Sets the exit code validation mode.</summary>
    public ProcessWrapper WithExitCodeValidation(ExitCodeValidationMode mode)
    {
        return new ProcessWrapper(this) { _exitCodeValidation = mode };
    }

    /// <summary>Replaces all output stream handlers with the specified handler.</summary>
    public ProcessWrapper WithOutputStream(Action<string> handler)
    {
        return new ProcessWrapper(this) { _outputHandlers = [handler] };
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
        return new ProcessWrapper(this) { _outputHandlers = _outputHandlers.Add(handler) };
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
        return new ProcessWrapper(this) { _errorHandlers = [handler] };
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
        return new ProcessWrapper(this) { _errorHandlers = _errorHandlers.Add(handler) };
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
        return new ProcessWrapper(this) { _inputStream = new ProcessInputStream.StreamInput(stream) };
    }

    /// <summary>Sets the input stream to the specified <see cref="TextReader"/>.</summary>
    public ProcessWrapper WithInputStream(TextReader reader)
    {
        return new ProcessWrapper(this) { _inputStream = new ProcessInputStream.TextReaderInput(reader) };
    }

    /// <summary>Sets the input stream to the specified string.</summary>
    public ProcessWrapper WithInputStream(string text)
    {
        return new ProcessWrapper(this) { _inputStream = new ProcessInputStream.StringInput(text) };
    }

    /// <summary>
    /// Starts the process and returns a <see cref="ProcessInstance"/> immediately.
    /// Await the returned instance to wait for the process to exit.
    /// </summary>
    public ProcessInstance ExecuteAsync(CancellationToken cancellationToken = default)
    {
        return StartProcess(_outputHandlers, _errorHandlers,
            (process, inputTask, registration, ct) => new ProcessInstance(process, inputTask, registration, _exitCodeValidation, ct),
            cancellationToken);
    }

    /// <summary>
    /// Starts the process with output buffering and returns a <see cref="BufferedProcessInstance"/> immediately.
    /// Await the returned instance to wait for the process to exit. Output is collected in <see cref="BufferedProcessInstance.Output"/>.
    /// </summary>
    public BufferedProcessInstance ExecuteBufferedAsync(CancellationToken cancellationToken = default)
    {
        var output = new ProcessOutputCollection();

        var outputHandlers = _outputHandlers.Add(line => output.Add(ProcessOutputType.StandardOutput, line));
        var errorHandlers = _errorHandlers.Add(line => output.Add(ProcessOutputType.StandardError, line));

        return StartProcess(outputHandlers, errorHandlers,
            (process, inputTask, registration, ct) => new BufferedProcessInstance(process, inputTask, registration, _exitCodeValidation, output, ct),
            cancellationToken);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000")]
    private T StartProcess<T>(ImmutableArray<Action<string>> outputHandlers, ImmutableArray<Action<string>> errorHandlers, Func<Process, Task, CancellationTokenRegistration, CancellationToken, T> factory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var hasOutputHandlers = !outputHandlers.IsEmpty;
        var hasErrorHandlers = !errorHandlers.IsEmpty;
        var hasInputStream = _inputStream is not null;

        var psi = new ProcessStartInfo
        {
            FileName = _fileName,
            UseShellExecute = false,
            ErrorDialog = false,
            RedirectStandardOutput = hasOutputHandlers,
            RedirectStandardError = hasErrorHandlers,
            RedirectStandardInput = hasInputStream,
        };

        foreach (var arg in _arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        if (_workingDirectory is not null)
        {
            psi.WorkingDirectory = _workingDirectory;
        }

        foreach (var action in _envActions)
        {
            if (action.Value is null)
            {
                psi.Environment.Remove(action.Name);
            }
            else
            {
                psi.Environment[action.Name] = action.Value;
            }
        }

        var process = new Process { StartInfo = psi };

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
                    foreach (var handler in errorHandlers)
                    {
                        handler(e.Data);
                    }
                }
            };
        }

        if (!process.Start())
        {
            throw new Win32Exception("Cannot start the process");
        }

        if (hasOutputHandlers)
        {
            process.BeginOutputReadLine();
        }

        if (hasErrorHandlers)
        {
            process.BeginErrorReadLine();
        }

        var inputTask = Task.CompletedTask;
        if (_inputStream is not null)
        {
            var inputStream = _inputStream;
            inputTask = Task.Run(async () =>
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

        return factory(process, inputTask, registration, cancellationToken);
    }
}
