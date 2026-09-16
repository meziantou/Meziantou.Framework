using System.Diagnostics;

#if MEZIANTOU_INLINE_SNAPSHOT_TESTING
namespace Meziantou.Framework.InlineSnapshotTesting.MergeTools;
#else
namespace Meziantou.Framework.SnapshotTesting.MergeTools;
#endif

/// <summary>
/// A merge tool process, and the cleanup to run once it exits. Disposing the result does not cancel that cleanup: a
/// merge tool that does not block the test is released right after it starts, and disposing the <see cref="Process" />
/// at that point would stop watching for its exit, so the files the cleanup removes would never be deleted. The
/// process is only disposed once it is both released and exited.
/// </summary>
internal sealed class ProcessMergeToolResult : MergeToolResult
{
    private readonly Lock _lock = new();
    private readonly Action? _onExited;
    private readonly bool _waitsForMerge;
    private bool _exited;
    private bool _disposed;

    public ProcessMergeToolResult(Process process, bool waitsForMerge = true)
        : this(process, onExited: null, waitsForMerge)
    {
    }

    private ProcessMergeToolResult(Process process, Action? onExited, bool waitsForMerge)
    {
        Process = process;
        _onExited = onExited;
        _waitsForMerge = waitsForMerge;
        if (onExited is not null)
        {
            process.EnableRaisingEvents = true;
            process.Exited += OnProcessExited;
        }
    }

    public Process Process { get; }

    public override bool WaitsForMerge => _waitsForMerge;

    /// <summary>
    /// Starts a process and runs <paramref name="onExited" /> when it exits. The exit notification is set up before the
    /// process starts, as a tool that exits immediately would otherwise be gone before anything listens for it.
    /// </summary>
    public static ProcessMergeToolResult Start(ProcessStartInfo startInfo, Action? onExited, bool waitsForMerge = true)
    {
        var process = new Process { StartInfo = startInfo };
        try
        {
            var result = new ProcessMergeToolResult(process, onExited, waitsForMerge);
            process.Start();
            return result;
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    public override void WaitForExit() => Process.WaitForExit();

    public override void Dispose()
    {
        if (_onExited is null)
        {
            Process.Dispose();
            return;
        }

        lock (_lock)
        {
            if (_disposed)
                return;

            _disposed = true;
            if (!_exited)
                return;
        }

        Process.Dispose();
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        lock (_lock)
        {
            if (_exited)
                return;

            _exited = true;
        }

        try
        {
            _onExited?.Invoke();
        }
        catch
        {
            // The cleanup is best effort, and an exception thrown from the exit notification would crash the test host.
        }

        lock (_lock)
        {
            if (!_disposed)
                return;
        }

        Process.Dispose();
    }
}
