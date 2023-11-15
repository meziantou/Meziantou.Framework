namespace Meziantou.Framework.Templating;

public class CodeBlock(Template template, string text, int index)
    : ParsedBlock(template, text, index)
{
    protected string EvalPrefixString { get; set; } = "=";

    public override string BuildCode()
    {
        if (Text.StartsWith(EvalPrefixString, StringComparison.Ordinal))
        {
            return Template.OutputParameterName + ".Write(\"{0}\", " + Text[EvalPrefixString.Length..] + ");";
        }

        return Text;
    }
}
