namespace Meziantou.Framework.WasmEngine;

/// <summary>
/// The timer of the interruptions of a runtime (see <see cref="WasmStoreOptions.CheckInterrupt"/>): at each interval, it
/// asks the machines of the stores that have a <see cref="WasmStoreOptions.CheckInterrupt"/> action to call it (see
/// <see cref="Machine.RequestInterruptCheck"/>). The running code only reads a field of its machine. The timer runs while
/// such stores exist.
/// </summary>
/// <remarks>
/// The timer references the machines weakly, and does not reference the runtime: the stores and the runtime that are not
/// disposed can be collected.
/// </remarks>
internal sealed class InterruptionTimer : IDisposable
{
    private readonly Lock _lock = new();
    private readonly List<WeakReference<Machine>> _machines = [];
    private readonly TimeSpan _interval;
    private readonly Timer _timer;
    private bool _running;
    private bool _disposed;

    public InterruptionTimer(TimeSpan interval)
    {
        _interval = interval;
        _timer = new Timer(state => ((InterruptionTimer)state!).Tick(), this, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Whether the timer runs: a registered machine exists.</summary>
    public bool IsRunning
    {
        get
        {
            lock (_lock)
            {
                return _running;
            }
        }
    }

    public void Register(Machine machine)
    {
        lock (_lock)
        {
            if (_disposed)
                return;

            _machines.Add(new WeakReference<Machine>(machine));
            if (!_running)
            {
                _running = true;
                _timer.Change(_interval, _interval);
            }
        }
    }

    public void Unregister(Machine machine)
    {
        lock (_lock)
        {
            _machines.RemoveAll(reference => !reference.TryGetTarget(out var target) || ReferenceEquals(target, machine));
            StopIfEmpty();
        }
    }

    private void Tick()
    {
        lock (_lock)
        {
            // The machines of the collected stores are removed
            _machines.RemoveAll(reference =>
            {
                if (!reference.TryGetTarget(out var machine))
                    return true;

                machine.RequestInterruptCheck();
                return false;
            });

            StopIfEmpty();
        }
    }

    private void StopIfEmpty()
    {
        if (_running && _machines.Count == 0)
        {
            _running = false;
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            _running = false;
            _machines.Clear();
        }

        _timer.Dispose();
    }
}
