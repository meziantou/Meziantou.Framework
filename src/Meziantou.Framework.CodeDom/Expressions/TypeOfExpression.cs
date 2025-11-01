namespace Meziantou.Framework.CodeDom;

/// <summary>Represents a typeof expression.</summary>
public class TypeOfExpression : Expression
{
    public TypeOfExpression()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="TypeOfExpression"/> class with the specified type.</summary>
    /// <param name="type">The type to get the Type object for.</param>
    public TypeOfExpression(TypeReference? type)
    {
        Type = type;
    }

    public TypeReference? Type { get; set; }
}
