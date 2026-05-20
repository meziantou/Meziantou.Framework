using System.Diagnostics;

namespace Meziantou.Framework;

/// <summary>
/// Represents a process factory creating <see cref="FakeProcess" /> instances.
/// </summary>
public sealed class FakeProcessFactory : IProcessFactory
{
    private readonly Func<ProcessStartInfo, FakeProcess> _factory;

    /// <summary>Initializes a factory using a callback to create fake processes.</summary>
    /// <param name="factory">The callback creating fake processes.</param>
    public FakeProcessFactory(Func<ProcessStartInfo, FakeProcess> factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <summary>Initializes a factory that returns the provided fake processes in sequence.</summary>
    /// <param name="processes">The sequence of fake processes to return.</param>
    public FakeProcessFactory(params FakeProcess[] processes)
    {
        ArgumentNullException.ThrowIfNull(processes);

        if (processes.Length == 0)
            throw new ArgumentException("At least one fake process must be provided.", nameof(processes));

        foreach (var process in processes)
        {
            if(process == null)
                throw new ArgumentException("Fake processes cannot contain null values.", nameof(processes));
        }

        var queue = new Queue<FakeProcess>(processes);
        var syncObject = new Lock();
        _factory = _ =>
        {
            lock (syncObject)
            {
                if (!queue.TryDequeue(out var process))
                    throw new InvalidOperationException("No fake process is available for this execution.");

                return process;
            }
        };
    }

    IProcessHandle IProcessFactory.Create(ProcessStartInfo processStartInfo)
    {
            ArgumentNullException.ThrowIfNull(processStartInfo);

        var process = _factory(processStartInfo);
        return process ?? throw new InvalidOperationException("Fake process factory returned null.");
    }
}
