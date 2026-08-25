namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a <c>set</c> statement, including its <c>/a</c> and <c>/p</c> forms.</summary>
public sealed class CmdSetStatementSyntax : ShellStatementSyntax
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public CmdSetStatementSyntax(
        ShellSyntaxToken setKeyword,
        ShellSyntaxToken? switchToken,
        ShellSyntaxToken? nameToken,
        ShellSyntaxToken? equalsToken,
        ShellWordSyntax? value)
        : base(
            ShellSyntaxKind.CmdSetStatement,
            setKeyword.ToFullString() + (switchToken?.ToFullString() ?? string.Empty) + (nameToken?.ToFullString() ?? string.Empty) + (equalsToken?.ToFullString() ?? string.Empty) + (value?.ToFullString() ?? string.Empty),
            setKeyword.FullSpan.Start,
            BuildTokens(setKeyword, switchToken, nameToken, equalsToken))
    {
        SetKeyword = setKeyword;
        SwitchToken = switchToken;
        NameToken = nameToken;
        EqualsToken = equalsToken;
        Value = value;
        _childNodes = [.. OptionalNode(Value)];
    }

    /// <summary>The <c>set</c> keyword.</summary>
    public ShellSyntaxToken SetKeyword { get; }

    /// <summary>The <c>/a</c> or <c>/p</c> switch, when present.</summary>
    public ShellSyntaxToken? SwitchToken { get; }

    /// <summary>The variable name.</summary>
    public ShellSyntaxToken? NameToken { get; }

    /// <summary>The <c>=</c> token.</summary>
    public ShellSyntaxToken? EqualsToken { get; }

    /// <summary>The assigned value, absent when the statement clears the variable.</summary>
    public ShellWordSyntax? Value { get; }

    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    /// <summary>The variable name, or an empty string when the statement has no name.</summary>
    public string Name => NameToken?.ValueText ?? string.Empty;

    /// <summary>Returns <see langword="true"/> for <c>set /a</c>, which evaluates its value arithmetically.</summary>
    public bool IsArithmetic => string.Equals(SwitchToken?.Text, "/a", StringComparison.OrdinalIgnoreCase);

    /// <summary>Returns <see langword="true"/> for <c>set /p</c>, which prompts the user.</summary>
    public bool IsPrompt => string.Equals(SwitchToken?.Text, "/p", StringComparison.OrdinalIgnoreCase);

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitCmdSet(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitCmdSet(this);

    private static List<ShellSyntaxToken> BuildTokens(
        ShellSyntaxToken setKeyword,
        ShellSyntaxToken? switchToken,
        ShellSyntaxToken? nameToken,
        ShellSyntaxToken? equalsToken)
    {
        var tokens = new List<ShellSyntaxToken>();
        tokens.Add(setKeyword);
        if (switchToken is not null)
        {
            tokens.Add(switchToken);
        }

        if (nameToken is not null)
        {
            tokens.Add(nameToken);
        }

        if (equalsToken is not null)
        {
            tokens.Add(equalsToken);
        }

        return tokens;
    }
}
