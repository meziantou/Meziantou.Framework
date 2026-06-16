namespace Meziantou.Framework.Yamlish.Nodes;

/// <summary>Represents a Yamlish document.</summary>
public sealed class YamlishDocument
{
    /// <summary>Initializes a new instance of the <see cref="YamlishDocument" /> class.</summary>
    /// <param name="root">The root node of the document.</param>
    public YamlishDocument(YamlishNode root)
    {
        Root = root ?? throw new ArgumentNullException(nameof(root));
    }

    /// <summary>Gets the root node of the document.</summary>
    public YamlishNode Root { get; }

    /// <summary>Parses Yamlish content into a document.</summary>
    /// <param name="content">The Yamlish content to parse.</param>
    /// <returns>The parsed document.</returns>
    public static YamlishDocument Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return new YamlishDocument(YamlishParser.Parse(content));
    }

    /// <summary>Reads Yamlish content from a text reader and parses it into a document.</summary>
    /// <param name="reader">The reader that provides the Yamlish content.</param>
    /// <returns>The parsed document.</returns>
    public static YamlishDocument Parse(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return Parse(reader.ReadToEnd());
    }

    /// <summary>Writes the document to a text writer.</summary>
    /// <param name="writer">The writer that receives the Yamlish content.</param>
    public void WriteTo(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        YamlishWriter.Write(writer, Root, indentCharacter: ' ', indentSize: 2, newLine: writer.NewLine);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        WriteTo(writer);
        return writer.ToString();
    }
}
