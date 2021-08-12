using System;
using System.IO;

namespace Meziantou.Framework.Templating;

public class ParsedBlock
{
    public ParsedBlock(Template template, string text, int index)
    {
        Template = template ?? throw new ArgumentNullException(nameof(template));
        Text = text ?? throw new ArgumentNullException(nameof(text));
        Index = index;
    }

    public Template Template { get; }
    public string Text { get; }
    public int Index { get; }

    public int StartLine { get; internal set; }
    public int EndLine { get; internal set; }
    public int StartColumn { get; internal set; }
    public int EndColumn { get; internal set; }

    public virtual string BuildCode()
    {
        return Template.OutputParameterName + "." + nameof(TextWriter.Write) + "(@\"" + EscapeVerbatimString(Text) + "\");";
    }

    protected static string? EscapeVerbatimString(string? s)
    {
        return s?.Replace("\"", "\"\"", StringComparison.Ordinal);
    }
}
