using System.Collections;

namespace Meziantou.Framework.TemporaryContainers;

/// <summary>An ordered collection of wait strategies run when a container starts.</summary>
public sealed class ContainerWaitStrategyCollection : IEnumerable<IWaitStrategy>
{
    private readonly List<IWaitStrategy> _strategies;

    internal ContainerWaitStrategyCollection()
    {
        _strategies = [];
    }

    internal ContainerWaitStrategyCollection(ContainerWaitStrategyCollection other)
    {
        _strategies = [.. other._strategies];
    }

    /// <summary>Gets the number of strategies in the collection.</summary>
    public int Count => _strategies.Count;

    /// <summary>Adds a wait strategy.</summary>
    /// <param name="strategy">The strategy to add.</param>
    public void Add(IWaitStrategy strategy)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        _strategies.Add(strategy);
    }

    /// <summary>Returns an enumerator over the strategies.</summary>
    /// <returns>An enumerator.</returns>
    public IEnumerator<IWaitStrategy> GetEnumerator() => _strategies.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
