namespace Meziantou.Framework.Templating;

/// <summary>Represents a text block in a template that will be written to the output.</summary>
public class TextBlock : TemplateBlock
{
    /// <summary>Initializes a new instance of the <see cref="TextBlock"/> class.</summary>
    /// <param name="template">The template that contains this block.</param>
    /// <param name="text">The text content of the block.</param>
    /// <param name="index">The index of this block in the template.</param>
    public TextBlock(Template template, string text, int index)
        : base(template, text, index)
    {
    }

    /// <summary>Builds the C# code for this text block.</summary>
    /// <returns>The generated C# code that writes the text to the output.</returns>
    public override string BuildCode()
    {
        return Template.OutputParameterName + "." + nameof(TextWriter.Write) + "(@\"" + EscapeVerbatimString(Text) + "\");";
    }
}
