namespace Meziantou.Framework.Yamlish.Nodes;

public abstract class YamlishNode
{
    public abstract YamlishNodeKind Kind { get; }

    public override string ToString()
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        YamlishWriter.Write(writer, this, indentCharacter: ' ', indentSize: 2, newLine: writer.NewLine);
        return writer.ToString();
    }
}
