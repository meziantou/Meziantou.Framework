using System.Collections;

namespace Meziantou.Framework;

/// <summary>A thread-safe collection of <see cref="ProcessOutput"/> lines.</summary>
public sealed class ProcessOutputCollection : IReadOnlyList<ProcessOutput>
{
    private readonly List<ProcessOutput> _output = [];

    /// <summary>Initializes a new empty <see cref="ProcessOutputCollection"/>.</summary>
    public ProcessOutputCollection()
    {
    }

    /// <summary>Gets the number of output lines.</summary>
    public int Count
    {
        get
        {
            lock (_output)
            {
                return _output.Count;
            }
        }
    }

    /// <summary>Gets the output line at the specified index.</summary>
    public ProcessOutput this[int index]
    {
        get
        {
            lock (_output)
            {
                return _output[index];
            }
        }
    }

    /// <summary>Gets all output lines from the standard output stream.</summary>
    public IEnumerable<ProcessOutput> StandardOutput => this.Where(o => o.Type == ProcessOutputType.StandardOutput);

    /// <summary>Gets all output lines from the standard error stream.</summary>
    public IEnumerable<ProcessOutput> StandardError => this.Where(o => o.Type == ProcessOutputType.StandardError);

    internal void Add(ProcessOutputType type, string text)
    {
        lock (_output)
        {
            _output.Add(new ProcessOutput(type, text));
        }
    }

    /// <inheritdoc/>
    public IEnumerator<ProcessOutput> GetEnumerator()
    {
        List<ProcessOutput> snapshot;
        lock (_output)
        {
            snapshot = [.. _output];
        }

        return snapshot.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc/>
    public override string ToString()
    {
        lock (_output)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var item in _output)
            {
                sb.Append(item).AppendLine();
            }

            return sb.ToString();
        }
    }
}
