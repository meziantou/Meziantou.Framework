namespace Meziantou.Framework.CodeDom;

/// <summary>Represents a code snippet statement for injecting raw code.</summary>
public class SnippetStatement : Statement
{
    public SnippetStatement()
    {
    }

    public SnippetStatement(string? statement)
    {
        Statement = statement;
    }

    public string? Statement { get; set; }
}
