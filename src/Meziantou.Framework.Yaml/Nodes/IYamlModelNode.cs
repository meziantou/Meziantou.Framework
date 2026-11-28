namespace Meziantou.Framework.Yaml.Nodes;

/// <summary>
/// A stream, a document or a node of the document object model: the state that the change tracking and the events of
/// the model need from each of them. They have no public base type, so that a stream or a document is not a node.
/// </summary>
internal interface IYamlModelNode
{
    /// <summary>Gets or sets a value indicating whether the object was loaded from a text that keeps its changes, see <see cref="YamlStream.ToStringPreservingFormatting"/>.</summary>
    bool IsTracked { get; set; }

    /// <summary>Gets or sets the state of the object before its first change, or <see langword="null"/> while it is unchanged.</summary>
    YamlNodeSnapshot? Snapshot { get; set; }
}
