using Meziantou.Framework.Yaml.Events;

namespace Meziantou.Framework.Yaml.Model;

/// <summary>The state of a node when it was loaded, to find what changed since and where the node is in the source text.</summary>
internal sealed class YamlNodeSnapshot
{
    /// <summary>Gets or sets the start event of a collection, the scalar event of a value, or the start of a document.</summary>
    public ParsingEvent? Header { get; set; }

    /// <summary>Gets or sets the location of the node in the source text, with its properties.</summary>
    public Mark Start { get; set; }

    public Mark End { get; set; }

    public YamlElement[]? Keys { get; set; }

    public YamlElement?[]? Values { get; set; }

    public YamlElement[]? Items { get; set; }

    public YamlElement? Contents { get; set; }

    public YamlDocument[]? Documents { get; set; }
}
