namespace Meziantou.Framework.Templating;

/// <summary>Represents a directive block in an HTML email template.</summary>
public sealed class HtmlEmailDirectiveBlock : DirectiveBlock
{
    public HtmlEmailDirectiveBlock(Template template, string text, int index, string name, string value)
        : base(template, text, index, name, value)
    {
    }

    public override string BuildCode()
    {
        if (string.Equals(Name, "begin_section", StringComparison.OrdinalIgnoreCase))
        {
            var sectionName = Nullify(Value);
            return Template.OutputParameterName + $".{nameof(HtmlEmailOutput.BeginSection)}(@\"{EscapeVerbatimString(sectionName)}\");";
        }

        if (string.Equals(Name, "end_section", StringComparison.OrdinalIgnoreCase))
        {
            var sectionName = Nullify(Value);
            return Template.OutputParameterName + $".{nameof(HtmlEmailOutput.EndSection)}(@\"{EscapeVerbatimString(sectionName)}\");";
        }

        return base.BuildCode();
    }

    private static string? Nullify(string? text)
    {
        if (text is null)
            return null;

        text = text.Trim();
        return text.Length == 0 ? null : text;
    }
}
