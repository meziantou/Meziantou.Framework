namespace Meziantou.Framework.CodeDom;

/// <summary>Represents a using statement for automatic resource disposal.</summary>
public class UsingStatement : Statement
{
    public Statement? Statement
    {
        get;
        set => SetParent(ref field, value);
    }

    public StatementCollection? Body { get; set; }
}
