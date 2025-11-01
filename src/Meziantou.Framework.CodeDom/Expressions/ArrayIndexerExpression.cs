namespace Meziantou.Framework.CodeDom;

/// <summary>Represents an array indexer expression (e.g., array[index]).</summary>
public class ArrayIndexerExpression : Expression
{
    public ArrayIndexerExpression()
        : this(array: null)
    {
    }

    public ArrayIndexerExpression(Expression? array, params Expression[] indices)
    {
        Indices = new CodeObjectCollection<Expression>(this);

        ArrayExpression = array;
        foreach (var index in indices)
        {
            Indices.Add(index);
        }
    }

    public Expression? ArrayExpression
    {
        get;
        set => SetParent(ref field, value);
    }

    public CodeObjectCollection<Expression> Indices { get; }
}
