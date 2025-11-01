namespace Meziantou.Framework.CodeDom;

/// <summary>Specifies the binary operators for binary expressions.</summary>
public enum BinaryOperator
{
    /// <summary>No operator.</summary>
    None,

    /// <summary>Equality operator (==).</summary>
    Equals,

    /// <summary>Inequality operator (!=).</summary>
    NotEquals,

    /// <summary>Less than operator (&lt;).</summary>
    LessThan,

    /// <summary>Less than or equal operator (&lt;=).</summary>
    LessThanOrEqual,

    /// <summary>Greater than operator (&gt;).</summary>
    GreaterThan,

    /// <summary>Greater than or equal operator (&gt;=).</summary>
    GreaterThanOrEqual,

    /// <summary>Logical OR operator (||).</summary>
    Or,

    /// <summary>Bitwise OR operator (|).</summary>
    BitwiseOr,

    /// <summary>Logical AND operator (&amp;&amp;).</summary>
    And,

    /// <summary>Bitwise AND operator (&amp;).</summary>
    BitwiseAnd,

    /// <summary>Addition operator (+).</summary>
    Add,

    /// <summary>Subtraction operator (-).</summary>
    Substract,

    /// <summary>Multiplication operator (*).</summary>
    Multiply,

    /// <summary>Division operator (/).</summary>
    Divide,

    /// <summary>Modulo operator (%).</summary>
    Modulo,

    /// <summary>Left shift operator (&lt;&lt;).</summary>
    ShiftLeft,

    /// <summary>Right shift operator (&gt;&gt;).</summary>
    ShiftRight,

    /// <summary>Bitwise XOR operator (^).</summary>
    Xor,
}
