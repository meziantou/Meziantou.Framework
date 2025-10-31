namespace Meziantou.Framework.Templating;

/// <summary>Represents a text block in a template that will be written to the output.</summary>
public class ParsedBlock
{
    /// <summary>Initializes a new instance of the <see cref="ParsedBlock"/> class.</summary>
    /// <param name="template">The template that contains this block.</param>
    /// <param name="text">The text content of the block.</param>
    /// <param name="index">The index of this block in the template.</param>
    public ParsedBlock(Template template, string text, int index)
    {
        Template = template ?? throw new ArgumentNullException(nameof(template));
        Text = text ?? throw new ArgumentNullException(nameof(text));
        Index = index;
    }

    /// <summary>Gets the template that contains this block.</summary>
    public Template Template { get; }

    /// <summary>Gets the text content of the block.</summary>
    public string Text { get; }

    /// <summary>Gets the index of this block in the template.</summary>
    public int Index { get; }

    /// <summary>Gets or sets the starting line number of the block in the source template.</summary>
    public int StartLine { get; internal set; }

    /// <summary>Gets or sets the ending line number of the block in the source template.</summary>
    public int EndLine { get; internal set; }

    /// <summary>Gets or sets the starting column number of the block in the source template.</summary>
    public int StartColumn { get; internal set; }

    /// <summary>Gets or sets the ending column number of the block in the source template.</summary>
    public int EndColumn { get; internal set; }

    /// <summary>Builds the C# code for this text block.</summary>
    /// <returns>The generated C# code that writes the text to the output.</returns>
    public virtual string BuildCode()
    {
        return Template.OutputParameterName + "." + nameof(TextWriter.Write) + "(@\"" + EscapeVerbatimString(Text) + "\");";
    }

    protected static string? EscapeVerbatimString(string? s)
    {
        return s?.Replace("\"", "\"\"", StringComparison.Ordinal);
    }
}
