namespace Meziantou.Framework.SimpleQueryLanguage;

/// <summary>Specifies the comparison operator used in a key-value query.</summary>
public enum KeyValueOperator
{
    /// <summary>Represents the equals operator (: or =).</summary>
    EqualTo,

    /// <summary>Represents the not equals operator (&lt;&gt;).</summary>
    NotEqualTo,

    /// <summary>Represents the less than operator (&lt;).</summary>
    LessThan,

    /// <summary>Represents the less than or equal operator (&lt;=).</summary>
    LessThanOrEqual,

    /// <summary>Represents the greater than operator (&gt;).</summary>
    GreaterThan,

    /// <summary>Represents the greater than or equal operator (&gt;=).</summary>
    GreaterThanOrEqual,
}
