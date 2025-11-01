using System.Collections;

namespace Meziantou.Framework;

/// <summary>
/// Represents a read-only collection of process output.
/// </summary>
public sealed class ProcessOutputCollection : IReadOnlyList<ProcessOutput>
{
    private readonly IReadOnlyList<ProcessOutput> _output;

    internal ProcessOutputCollection(IReadOnlyList<ProcessOutput> output)
    {
        _output = output;
    }

    /// <inheritdoc/>
    public int Count => _output.Count;

    /// <inheritdoc/>
    public ProcessOutput this[int index] => _output[index];

    /// <inheritdoc/>
    public IEnumerator<ProcessOutput> GetEnumerator() => _output.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc/>
    public override string ToString()
    {
        var sb = new StringBuilder();
        foreach (var item in _output)
        {
            sb.Append(item).AppendLine();
        }

        return sb.ToString();
    }
}
