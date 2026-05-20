using Microsoft.Win32.SafeHandles;

namespace Meziantou.Framework;

/// <summary>Represents an in-memory process handle intended for tests.</summary>
public sealed class FakeProcess : IProcessHandle
{
    private static int s_processId;
    private readonly TaskCompletionSource _waitForExitTaskSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly int _id;
    private readonly Stream _inputStream;
    private readonly Stream _outputStream;
    private readonly Stream _errorStream;
    private readonly int _initialExitCode;
    private bool _completeOnStart = true;
    private bool _isDisposed;
    private int _exitCode;
    private bool _hasExited;

    private FakeProcess(int exitCode, Stream outputStream, Stream errorStream)
    {
        ArgumentNullException.ThrowIfNull(outputStream);
        ArgumentNullException.ThrowIfNull(errorStream);

        _initialExitCode = exitCode;
        _exitCode = exitCode;
        _outputStream = outputStream;
        _errorStream = errorStream;
        _inputStream = new MemoryStream();
        _id = Interlocked.Increment(ref s_processId);
    }

    /// <summary>Creates a fake process with empty standard output and standard error streams.</summary>
    /// <param name="exitCode">The process exit code.</param>
    public static FakeProcess Create(int exitCode)
    {
        return new FakeProcess(exitCode, Stream.Null, Stream.Null);
    }

    /// <summary>Creates a fake process with the specified standard output and standard error streams.</summary>
    /// <param name="exitCode">The process exit code.</param>
    /// <param name="outputStream">The stream used as standard output.</param>
    /// <param name="errorStream">The stream used as standard error.</param>
    public static FakeProcess Create(int exitCode, Stream outputStream, Stream errorStream)
    {
        return new FakeProcess(exitCode, outputStream, errorStream);
    }

    /// <summary>Creates a fake process with UTF-8 encoded text output.</summary>
    /// <param name="exitCode">The process exit code.</param>
    /// <param name="outputText">The standard output text.</param>
    /// <param name="errorText">The standard error text.</param>
    public static FakeProcess Create(int exitCode, string outputText, string errorText)
    {
        return new FakeProcess(exitCode, CreateTextStream(outputText), CreateTextStream(errorText));
    }

    /// <summary>Creates a fake process that does not complete automatically when started.</summary>
    /// <param name="exitCode">The process exit code.</param>
    public static FakeProcess CreatePending(int exitCode)
    {
        var process = Create(exitCode);
        process._completeOnStart = false;
        return process;
    }

    /// <summary>Creates a fake process with provided streams that does not complete automatically when started.</summary>
    /// <param name="exitCode">The process exit code.</param>
    /// <param name="outputStream">The stream used as standard output.</param>
    /// <param name="errorStream">The stream used as standard error.</param>
    public static FakeProcess CreatePending(int exitCode, Stream outputStream, Stream errorStream)
    {
        var process = Create(exitCode, outputStream, errorStream);
        process._completeOnStart = false;
        return process;
    }

    /// <summary>Creates a fake process with text output that does not complete automatically when started.</summary>
    /// <param name="exitCode">The process exit code.</param>
    /// <param name="outputText">The standard output text.</param>
    /// <param name="errorText">The standard error text.</param>
    public static FakeProcess CreatePending(int exitCode, string outputText, string errorText)
    {
        var process = Create(exitCode, outputText, errorText);
        process._completeOnStart = false;
        return process;
    }

    /// <summary>Completes the process execution and sets the process exit code.</summary>
    /// <param name="exitCode">Optional exit code override. If <see langword="null" />, keeps the configured code.</param>
    public void Complete(int? exitCode = null)
    {
        if (_hasExited)
            return;

        _exitCode = exitCode ?? _initialExitCode;
        _hasExited = true;
        _waitForExitTaskSource.TrySetResult();
    }

    /// <summary>Reads the captured standard input text as UTF-8 by default.</summary>
    /// <param name="encoding">The text encoding.</param>
    /// <returns>The captured standard input content.</returns>
    public string ReadInputAsText(Encoding? encoding = null)
    {
        if (_inputStream is not MemoryStream inputStream)
            throw new NotSupportedException("Reading captured input is supported only when input stream is a MemoryStream.");

        return (encoding ?? Encoding.UTF8).GetString(inputStream.ToArray());
    }

    private static MemoryStream CreateTextStream(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new MemoryStream(Encoding.UTF8.GetBytes(text));
    }

    private bool StartCore()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (_completeOnStart)
        {
            Complete();
        }

        return true;
    }

    private Task WaitForExitCoreAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        return _waitForExitTaskSource.Task.WaitAsync(cancellationToken);
    }

    private void KillCore()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (!_hasExited)
        {
            Complete(_initialExitCode == 0 ? -1 : _initialExitCode);
        }
    }

    private void DisposeCore()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        _inputStream.Dispose();
        _outputStream.Dispose();
        _errorStream.Dispose();
    }

    bool IProcessHandle.Start() => StartCore();
    int IProcessHandle.Id => _id;
    bool IProcessHandle.HasExited => _hasExited;
    int IProcessHandle.ExitCode => _exitCode;
    Stream IProcessHandle.InputStream => _inputStream;
    Stream IProcessHandle.OutputStream => _outputStream;
    Stream IProcessHandle.ErrorStream => _errorStream;
    SafeProcessHandle? IProcessHandle.SafeProcessHandle => null;
    Task IProcessHandle.WaitForExitAsync(CancellationToken cancellationToken) => WaitForExitCoreAsync(cancellationToken);
    void IProcessHandle.Kill(bool entireProcessTree) => KillCore();
    void IDisposable.Dispose() => DisposeCore();
}
