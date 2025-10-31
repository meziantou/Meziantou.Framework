namespace Meziantou.Framework.CodeDom;

/// <summary>Represents a code snippet expression for injecting raw code.</summary>
public class SnippetExpression : Expression
{
    public SnippetExpression()
    {
    }

    public SnippetExpression(string? expression)
    {
        Expression = expression;
    }

    public string? Expression { get; set; }
}
