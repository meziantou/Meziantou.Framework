namespace Meziantou.Framework.Yamlish.Nodes;

/// <summary>Represents a node in a Yamlish document.</summary>
public abstract class YamlishNode
{
    /// <summary>Gets the kind of the Yamlish node.</summary>
    public abstract YamlishNodeKind Kind { get; }

    /// <inheritdoc />
    public override string ToString()
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        YamlishWriter.Write(writer, this, indentCharacter: ' ', indentSize: 2, newLine: writer.NewLine);
        return writer.ToString();
    }
}
