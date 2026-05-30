namespace Meziantou.Framework.Templating;

/// <summary>Represents a block of executable code in a template.</summary>
public class CodeBlock(Template template, string text, int index, bool isExpression = false)
    : TemplateBlock(template, text, index)
{
    /// <summary>Gets a value indicating whether this block is an evaluation expression.</summary>
    public bool IsExpression { get; } = isExpression;

    /// <summary>Builds the C# code for this code block.</summary>
    /// <returns>The generated C# code.</returns>
    public override string BuildCode()
    {
        if (IsExpression)
        {
            return Template.OutputParameterName + ".Write(\"{0}\", " + Text + ");";
        }

        return Text;
    }
}
