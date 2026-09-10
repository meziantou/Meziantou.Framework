namespace Meziantou.Framework.Language.Tests.TestLanguage;

/// <summary>The kinds of the toy language the shared syntax types are exercised with.</summary>
internal enum TestSyntaxKind
{
    None = 0,
    List = 1,

    OpenParenToken = 100,
    CloseParenToken = 101,
    CommaToken = 102,
    IdentifierToken = 103,
    EndOfFileToken = 104,
    OpenBracketToken = 105,
    CloseBracketToken = 106,

    WhitespaceTrivia = 200,
    EndOfLineTrivia = 201,

    TestRoot = 300,
    TestAtom = 301,
    TestList = 302,
    TestBlock = 303,
}
