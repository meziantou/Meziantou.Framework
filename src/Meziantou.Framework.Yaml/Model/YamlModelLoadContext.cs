namespace Meziantou.Framework.Yaml.Model;

/// <summary>
/// Tracks the anchors declared by a document and bounds how many nodes aliases are allowed to materialize.
/// </summary>
/// <remarks>
/// The model API does not preserve aliases as a distinct node type, so every alias is expanded into a copy of the
/// anchored subtree. Nesting anchors so that each level references the previous one several times makes the node
/// count grow exponentially while the document grows linearly, which lets a sub-kilobyte payload exhaust memory.
/// Every node produced by an alias is charged against a budget so that expansion stays proportional to the input.
/// </remarks>
internal sealed class YamlModelLoadContext
{
    private readonly Dictionary<object, long> _nodeCounts = new(ReferenceEqualityComparer.Instance);
    private readonly long _maxAliasExpansionNodeCount;
    private long _aliasExpansionNodeCount;

    public YamlModelLoadContext(int maxAliasExpansionNodeCount, bool allowAnchors, bool allowAliases)
    {
        _maxAliasExpansionNodeCount = maxAliasExpansionNodeCount;
        AllowAnchors = allowAnchors;
        AllowAliases = allowAliases;
    }

    public Dictionary<string, YamlElement> Anchors { get; } = new(StringComparer.Ordinal);

    public bool AllowAnchors { get; }

    public bool AllowAliases { get; }

    /// <summary>Charges the nodes an alias is about to materialize against the budget.</summary>
    /// <exception cref="YamlException">Expanding the alias would exceed the budget.</exception>
    public void ChargeAliasExpansion(YamlElement anchored, string alias, Mark start, Mark end)
    {
        var remaining = _maxAliasExpansionNodeCount - _aliasExpansionNodeCount;
        var count = CountNodes(anchored, remaining);
        if (count > remaining)
        {
            throw new YamlException(start, end, FormattableString.Invariant($"Expanding the alias '*{alias}' would materialize more than the maximum of {_maxAliasExpansionNodeCount} nodes allowed for alias expansion. Set {nameof(YamlSerializerOptions)}.{nameof(YamlSerializerOptions.MaxAliasExpansionNodeCount)} to allow more, or disable aliases with {nameof(YamlSerializerOptions.AllowAliases)}."));
        }

        _aliasExpansionNodeCount += count;
    }

    /// <summary>Records the node count of an anchored element so later aliases can be charged without rewalking it.</summary>
    public void RegisterNodeCount(YamlElement element)
    {
        // Only anchored elements can be expanded later, so nothing else is worth measuring.
        _nodeCounts[element] = CountNodes(element, long.MaxValue);
    }

    /// <summary>
    /// Counts the nodes in <paramref name="element"/>, giving up as soon as the count exceeds <paramref name="limit"/>.
    /// </summary>
    /// <remarks>
    /// Stopping at the limit keeps the work bounded by the remaining budget, so counting cannot itself become the
    /// expensive operation the budget exists to prevent.
    /// </remarks>
    private long CountNodes(YamlElement element, long limit)
    {
        if (_nodeCounts.TryGetValue(element, out var cached))
        {
            return cached;
        }

        long count = 0;
        var pending = new Stack<YamlElement>();
        pending.Push(element);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (_nodeCounts.TryGetValue(current, out var known))
            {
                count += known;
            }
            else
            {
                count++;
                switch (current)
                {
                    case YamlMapping mapping:
                        foreach (var key in mapping.Keys)
                        {
                            pending.Push(key);
                            if (mapping[key] is { } value)
                            {
                                pending.Push(value);
                            }
                        }

                        break;

                    case YamlSequence sequence:
                        foreach (var item in sequence)
                        {
                            pending.Push(item);
                        }

                        break;
                }
            }

            if (count > limit)
            {
                return count;
            }
        }

        return count;
    }
}
