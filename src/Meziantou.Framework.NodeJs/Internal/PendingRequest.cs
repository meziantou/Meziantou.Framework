using System.Diagnostics.CodeAnalysis;

namespace Meziantou.Framework.NodeJs.Internal;

/// <summary>A call waiting for its response.</summary>
internal abstract class PendingRequest
{
    /// <summary>Completes the call with the result read from its UTF-8 JSON representation.</summary>
    public abstract void SetResult(ReadOnlySpan<byte> utf8Json);

    /// <summary>Completes a call whose response has no result, such as a void call.</summary>
    public abstract void SetNoResult();

    public abstract void SetException(Exception exception);
}

internal sealed class PendingRequest<T>(ResultReader<T> readResult) : PendingRequest
{
    private readonly TaskCompletionSource<T> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<T> Task => _completion.Task;

    // The reader can run user code (JSON converters). Its failure is the failure of this call, not of the connection.
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The exception is reported to the caller")]
    public override void SetResult(ReadOnlySpan<byte> utf8Json)
    {
        T result;
        try
        {
            result = readResult(utf8Json);
        }
        catch (Exception ex)
        {
            _completion.TrySetException(ex);
            return;
        }

        _completion.TrySetResult(result);
    }

    public override void SetNoResult() => _completion.TrySetResult(default!);

    public override void SetException(Exception exception) => _completion.TrySetException(exception);
}
