namespace Meziantou.Framework.Imaging.Tests.Conformance.Hardening;

/// <summary>
/// Fault injection with the real codecs over every valid corpus input: allocation failure at
/// every rental, I/O failure at every input position (non-seekable short reads), cancellation at every rental and at stream
/// positions, failing and canceled outputs, uncompleted writers, and failures inside geometry transactions. Every case runs
/// under a <see cref="PoolAudit"/>: the injected exception must propagate unchanged (never reported as a clean end or wrapped
/// as content errors), readers and writers fault, no buffer leaks or is used after its return, scopes return to zero, prior
/// destination files survive, only the owned temporary file is removed, and failed geometry operations change nothing.
/// </summary>
public sealed partial class FaultInjectionTests
{
    private const int MaxPositions = 160;

    private static readonly PixelConversionOptions Conversion = new() { DiscardIncompatibleColorProfile = true };

    private static void AssertCanceled(Exception? exception)
        => Assert.IsAssignableTo<OperationCanceledException>(exception);

    private static IEnumerable<long> Positions(long length)
    {
        if (length <= MaxPositions)
            return Enumerable.Range(0, (int)length).Select(position => (long)position);

        // Every position near both ends, evenly spread positions in between
        return Enumerable.Range(0, 40).Select(position => (long)position)
            .Concat(Enumerable.Range(0, MaxPositions - 80).Select(i => 40 + ((length - 80) * i / (MaxPositions - 80))))
            .Concat(Enumerable.Range(0, 40).Select(i => length - 40 + i))
            .Distinct();
    }

    private static Exception? Catch(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static async Task<Exception?> CatchAsync(Func<Task> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static TheoryData<string, string> Combine(IEnumerable<string> ids, string[] values)
    {
        var data = new TheoryData<string, string>();
        foreach (var id in ids)
        {
            foreach (var value in values)
            {
                data.Add(id, value);
            }
        }

        return data;
    }
}