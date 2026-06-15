namespace Meziantou.Framework.Yamlish;

public sealed class YamlishDocument
{
    public YamlishDocument(YamlishNode root)
    {
        Root = root ?? throw new ArgumentNullException(nameof(root));
    }

    public YamlishNode Root { get; }

    public static YamlishDocument Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return new YamlishDocument(YamlishParser.Parse(content));
    }

    public static YamlishDocument Parse(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return Parse(reader.ReadToEnd());
    }

    public void WriteTo(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        YamlishWriter.Write(writer, Root, indentCharacter: ' ', indentSize: 2, newLine: writer.NewLine);
    }

    public override string ToString()
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        WriteTo(writer);
        return writer.ToString();
    }
}
