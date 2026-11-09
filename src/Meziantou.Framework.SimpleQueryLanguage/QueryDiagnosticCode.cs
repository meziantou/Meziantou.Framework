namespace Meziantou.Framework.SimpleQueryLanguage;

/// <summary>Specifies the problem a <see cref="QueryDiagnostic"/> reports.</summary>
public enum QueryDiagnosticCode
{
    /// <summary>A <c>)</c> closes no <c>(</c>. It is searched as text.</summary>
    UnmatchedCloseParenthesis,

    /// <summary>A <c>(</c> has no matching <c>)</c>. The group ends at the end of the query.</summary>
    MissingCloseParenthesis,

    /// <summary>A pair of parentheses encloses nothing.</summary>
    EmptyParentheses,

    /// <summary>An <c>AND</c>, <c>OR</c>, <c>NOT</c> or <c>-</c> has no operand. It is searched as text.</summary>
    MissingOperand,

    /// <summary>A key and its operator are not followed by a value, as in <c>author:</c> or <c>author: alice</c>.</summary>
    MissingValue,

    /// <summary>A quoted text has no closing quote. It ends at the end of the query.</summary>
    UnterminatedQuotedText,

    /// <summary>No handler is registered for the key of a term. It is searched as text.</summary>
    UnknownKey,
}
