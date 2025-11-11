namespace Meziantou.Framework.Templating;

/// <summary>Represents a block of executable code in a template.</summary>
public class CodeBlock(Template template, string text, int index)
    : ParsedBlock(template, text, index)
{
    /// <summary>Gets or sets the prefix string that indicates an evaluation expression.</summary>
    protected string EvalPrefixString { get; set; } = "=";

    /// <summary>Builds the C# code for this code block.</summary>
    /// <returns>The generated C# code.</returns>
    public override string BuildCode()
    {
        if (Text.StartsWith(EvalPrefixString, StringComparison.Ordinal))
        {
            return Template.OutputParameterName + ".Write(\"{0}\", " + Text[EvalPrefixString.Length..] + ");";
        }

        return Text;
    }
}
