using System.Globalization;
using Meziantou.Framework.SimpleQueryLanguage.Syntax;

namespace Meziantou.Framework.SimpleQueryLanguage;

/// <summary>Describes a problem in a query, such as an unbalanced parenthesis or an unknown key, and where it is.</summary>
/// <remarks>
/// A query with diagnostics is still read: the part a diagnostic reports is read as text, or as described by
/// <see cref="QueryDiagnosticCode"/>. Use the diagnostics to show the problem to the user.
/// </remarks>
public sealed class QueryDiagnostic
{
    internal QueryDiagnostic(QueryDiagnosticCode code, TextSpan span, string message)
    {
        Code = code;
        Span = span;
        Message = message;
    }

    /// <summary>Gets the kind of problem.</summary>
    public QueryDiagnosticCode Code { get; }

    /// <summary>Gets the part of the query the problem is about.</summary>
    public TextSpan Span { get; }

    /// <summary>Gets a message describing the problem, in English.</summary>
    public string Message { get; }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Span}: {Message}");

    internal static QueryDiagnostic UnmatchedCloseParenthesis(TextSpan span) => new(QueryDiagnosticCode.UnmatchedCloseParenthesis, span, "')' does not close a '('");

    internal static QueryDiagnostic MissingCloseParenthesis(TextSpan span) => new(QueryDiagnosticCode.MissingCloseParenthesis, span, "'(' is not closed");

    internal static QueryDiagnostic EmptyParentheses(TextSpan span) => new(QueryDiagnosticCode.EmptyParentheses, span, "The parentheses are empty");

    internal static QueryDiagnostic MissingOperand(QueryToken token) => new(QueryDiagnosticCode.MissingOperand, token.Span, $"The operator '{token.Text}' has no operand");

    internal static QueryDiagnostic MissingValue(QueryToken key, QueryToken op) => new(QueryDiagnosticCode.MissingValue, op.Span, $"The key '{key.Text}' has no value after '{op.Text}'");

    internal static QueryDiagnostic UnterminatedQuotedText(TextSpan span) => new(QueryDiagnosticCode.UnterminatedQuotedText, span, "The quoted text has no closing quote");

    internal static QueryDiagnostic UnknownKey(QueryToken key) => new(QueryDiagnosticCode.UnknownKey, key.Span, $"The key '{key.Text}' is unknown");
}
