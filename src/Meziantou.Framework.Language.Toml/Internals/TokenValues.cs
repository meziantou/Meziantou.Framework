using Meziantou.Framework.Language.Toml.Syntax.InternalSyntax;

namespace Meziantou.Framework.Language.Toml.Internals;

/// <summary>Reads the value of a token that was built by hand rather than by the lexer, and so carries no value.</summary>
internal static class TokenValues
{
    public static long ParseInteger(string text)
        => ScalarParser.ParseInteger(text, out var value) == ScalarParser.Result.Success ? value : throw NotA("an integer", text);

    public static double ParseFloat(string text)
        => ScalarParser.ParseFloat(text, out var value) == ScalarParser.Result.Success ? value : throw NotA("a float", text);

    public static object ParseDateTime(string text)
        => ScalarParser.ParseDateTime(text, out _, out var value, out _, out _) == ScalarParser.Result.Success ? value! : throw NotA("a date or a time", text);

    private static InvalidOperationException NotA(string what, string text) => new($"The token '{text}' is not {what}.");
}
