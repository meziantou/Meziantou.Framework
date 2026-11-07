namespace Meziantou.Framework.Language.Css;

/// <summary>How an attribute selector compares the value of the attribute.</summary>
public enum CssAttributeOperator
{
    /// <summary>There is no value: the attribute only has to be present, as in <c>[href]</c>.</summary>
    None,

    /// <summary><c>=</c>: exactly the value.</summary>
    Equals,

    /// <summary><c>~=</c>: one of the whitespace-separated words of the attribute is the value.</summary>
    Includes,

    /// <summary><c>|=</c>: the value, or the value followed by <c>-</c>.</summary>
    DashMatch,

    /// <summary><c>^=</c>: starts with the value.</summary>
    Prefix,

    /// <summary><c>$=</c>: ends with the value.</summary>
    Suffix,

    /// <summary><c>*=</c>: contains the value.</summary>
    Substring,
}
