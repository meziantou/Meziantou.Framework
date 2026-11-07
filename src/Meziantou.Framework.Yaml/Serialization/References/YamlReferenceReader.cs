using Meziantou.Framework.Yaml.Events;

namespace Meziantou.Framework.Yaml.Serialization.References;

internal sealed class YamlReferenceReader
{
    private readonly Dictionary<string, object> _anchors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Scalar> _scalarAnchors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MappingRecording> _mappingAnchors = new(StringComparer.Ordinal);
    private readonly List<MappingRecording> _activeRecordings = [];
    private readonly long _maxReplayedNodeCount;
    private long _replayedNodeCount;

    /// <param name="maxReplayedNodeCount">
    /// The number of nodes replayed aliases may produce in total, which bounds the expansion of merge keys that refer
    /// to mappings that merge other mappings.
    /// </param>
    public YamlReferenceReader(int maxReplayedNodeCount = YamlSerializerOptions.DefaultMaxAliasExpansionNodeCount)
    {
        _maxReplayedNodeCount = maxReplayedNodeCount;
    }

    /// <summary>Records the events of the anchored mappings, so an alias to one of them can be replayed.</summary>
    /// <param name="parsingEvent">An event read from the parser. Replayed events are not recorded again.</param>
    /// <remarks>
    /// A merge key copies the entries of the mapping node an alias refers to, not the value that node was
    /// deserialized into, so the events of the node are kept until the end of the stream.
    /// </remarks>
    public void Record(ParsingEvent parsingEvent)
    {
        for (var i = _activeRecordings.Count - 1; i >= 0; i--)
        {
            var recording = _activeRecordings[i];
            recording.Events.Add(parsingEvent);
            recording.Depth += parsingEvent.NestingIncrease;
            if (parsingEvent.NestingIncrease >= 0)
            {
                recording.NodeCount++;
            }

            if (recording.Depth == 0)
            {
                recording.IsComplete = true;
                _activeRecordings.RemoveAt(i);
            }
        }

        if (parsingEvent is NodeEvent { Anchor: { } anchor })
        {
            if (parsingEvent is MappingStart)
            {
                var recording = new MappingRecording();
                recording.Events.Add(parsingEvent);
                recording.Depth = 1;
                recording.NodeCount = 1;
                _mappingAnchors[anchor] = recording;
                _activeRecordings.Add(recording);
            }
            else
            {
                _mappingAnchors.Remove(anchor);
            }
        }
    }

    /// <summary>Gets the recorded events of the mapping an alias refers to.</summary>
    /// <exception cref="InvalidOperationException">
    /// The alias is unknown, does not refer to a mapping, refers to a mapping that contains it, or the replayed events
    /// exceed the alias expansion limit.
    /// </exception>
    public IReadOnlyList<ParsingEvent> GetMappingEvents(string alias)
    {
        if (!_mappingAnchors.TryGetValue(alias, out var recording))
        {
            if (_anchors.ContainsKey(alias) || _scalarAnchors.ContainsKey(alias))
            {
                throw new InvalidOperationException($"The YAML alias '*{alias}' does not refer to a mapping.");
            }

            throw new InvalidOperationException($"Unknown YAML alias '*{alias}'.");
        }

        if (!recording.IsComplete)
        {
            throw new InvalidOperationException($"The YAML alias '*{alias}' refers to a mapping that contains it.");
        }

        _replayedNodeCount += recording.NodeCount;
        if (_replayedNodeCount > _maxReplayedNodeCount)
        {
            throw new InvalidOperationException($"Replaying the YAML alias '*{alias}' exceeds the maximum alias expansion of {_maxReplayedNodeCount} nodes.");
        }

        return recording.Events;
    }

    public void Register(string anchor, object value)
    {
        _anchors[anchor] = value;
        _scalarAnchors.Remove(anchor);
    }

    /// <summary>Registers the scalar node an anchor names.</summary>
    /// <remarks>
    /// A scalar is replayed rather than shared, so an alias to it can be read as whatever type the destination
    /// member declares instead of the type the anchored occurrence happened to produce.
    /// </remarks>
    public void RegisterScalar(string anchor, Scalar scalar)
    {
        _scalarAnchors[anchor] = scalar;
        _anchors.Remove(anchor);
    }

    public bool TryResolveScalar(string alias, [NotNullWhen(true)] out Scalar? scalar)
        => _scalarAnchors.TryGetValue(alias, out scalar);

    public object Resolve(string alias)
    {
        if (_anchors.TryGetValue(alias, out var value))
        {
            return value;
        }

        throw new InvalidOperationException($"Unknown YAML alias '*{alias}'.");
    }

    private sealed class MappingRecording
    {
        public List<ParsingEvent> Events { get; } = [];

        public int Depth { get; set; }

        public int NodeCount { get; set; }

        public bool IsComplete { get; set; }
    }
}
