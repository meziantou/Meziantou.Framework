using System.Diagnostics;

namespace Meziantou.Framework.WebPlatform.Playwright.Protocol;

/// <summary>
/// The progress of a protocol call (the <c>Progress</c> of the Playwright server): its timeout (the <c>timeout</c> of the
/// metadata of the call, 0 for no timeout), its cancellation (the call is aborted by the client, or the connection is
/// closed), and its log (the lines of the error message of a call that fails).
/// </summary>
internal sealed class Progress : IDisposable
{
    private readonly CancellationTokenSource _cancellation;
    private readonly CancellationTokenSource? _timeout;
    private readonly CancellationToken _connectionClosed;
    private readonly List<string> _log = [];
    private PlaywrightError? _abortError;

    public Progress(double timeout, CancellationToken connectionClosed)
    {
        Timeout = timeout;
        _connectionClosed = connectionClosed;
        if (timeout > 0)
        {
            _timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeout));
            _cancellation = CancellationTokenSource.CreateLinkedTokenSource(_timeout.Token, connectionClosed);
            Deadline = Stopwatch.GetTimestamp() + (long)(timeout / 1000 * Stopwatch.Frequency);
        }
        else
        {
            _cancellation = CancellationTokenSource.CreateLinkedTokenSource(connectionClosed);
        }
    }

    /// <summary>A progress without timeout, for the internal operations.</summary>
    public static Progress CreateInternal(CancellationToken cancellationToken = default) => new(0, cancellationToken);

    /// <summary>Gets the timeout of the call, in milliseconds (0 when there is no timeout).</summary>
    public double Timeout { get; }

    /// <summary>Gets the deadline of the call (a <see cref="Stopwatch"/> timestamp), or null when there is no timeout.</summary>
    public long? Deadline { get; }

    public CancellationToken CancellationToken => _cancellation.Token;

    public bool IsCanceled => _cancellation.IsCancellationRequested;

    public IReadOnlyList<string> Log
    {
        get
        {
            lock (_log)
            {
                return [.. _log];
            }
        }
    }

    /// <summary>Gets the time left before the deadline, or null when there is no timeout.</summary>
    public TimeSpan? RemainingTime => Deadline is { } deadline ? TimeSpan.FromSeconds(Math.Max(0, (double)(deadline - Stopwatch.GetTimestamp()) / Stopwatch.Frequency)) : null;

    public void AddLog(string message)
    {
        lock (_log)
        {
            _log.Add(message);
        }
    }

    /// <summary>Aborts the call (<c>__abort__</c>): the pending waits throw <paramref name="error"/>.</summary>
    public void Abort(PlaywrightError error)
    {
        AddLog("operation was aborted: " + error.Message);
        _abortError ??= error;
        _cancellation.Cancel();
    }

    /// <summary>Returns the error of a canceled call: the abort error, the timeout, or the closing of the connection.</summary>
    public PlaywrightError GetCancellationError()
    {
        if (_abortError is not null)
            return _abortError;

        if (_timeout is { IsCancellationRequested: true })
            return PlaywrightError.Timeout(Timeout);

        if (_connectionClosed.IsCancellationRequested)
            return PlaywrightError.TargetClosed();

        return PlaywrightError.Aborted();
    }

    public void ThrowIfCanceled()
    {
        if (_cancellation.IsCancellationRequested)
            throw GetCancellationError();
    }

    /// <summary>Waits for a delay, or throws the error of the call when it is canceled first.</summary>
    public async Task WaitAsync(TimeSpan delay)
    {
        try
        {
            await Task.Delay(delay, _cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            throw GetCancellationError();
        }
    }

    /// <summary>Waits for a task, or throws the error of the call when it is canceled first.</summary>
    public async Task<T> RaceAsync<T>(Task<T> task)
    {
        try
        {
            return await task.WaitAsync(_cancellation.Token);
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
            throw GetCancellationError();
        }
    }

    /// <summary>Waits for a task, or throws the error of the call when it is canceled first.</summary>
    public async Task RaceAsync(Task task)
    {
        try
        {
            await task.WaitAsync(_cancellation.Token);
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
            throw GetCancellationError();
        }
    }

    public void Dispose()
    {
        _cancellation.Dispose();
        _timeout?.Dispose();
    }
}
