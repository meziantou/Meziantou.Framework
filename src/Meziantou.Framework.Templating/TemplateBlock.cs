namespace Meziantou.Framework.Templating;

/// <summary>Represents a block in a template.</summary>
public abstract class TemplateBlock
{
    /// <summary>Initializes a new instance of the <see cref="TemplateBlock"/> class.</summary>
    /// <param name="template">The template that contains this block.</param>
    /// <param name="text">The text content of the block.</param>
    /// <param name="index">The index of this block in the template.</param>
    protected TemplateBlock(Template template, string text, int index)
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

    /// <summary>Gets the position where the block starts in the source template.</summary>
    public TextPosition Start => Span.Start;

    /// <summary>Gets the position where the block ends in the source template.</summary>
    public TextPosition End => Span.End;

    /// <summary>Gets or sets the source span of the block in the source template.</summary>
    public TextSpan Span { get; internal set; }

    /// <summary>Builds the C# code for this block.</summary>
    /// <returns>The generated C# code.</returns>
    public abstract string BuildCode();

    protected static string? EscapeVerbatimString(string? s)
    {
        return s?.Replace("\"", "\"\"", StringComparison.Ordinal);
    }
}
