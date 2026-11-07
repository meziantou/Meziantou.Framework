using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Meziantou.Framework.SnapshotTesting.Roslyn;

internal sealed class SyntaxSnapshotSerializer : ISnapshotSerializer
{
    public static ISnapshotSerializer Instance { get; } = new SyntaxSnapshotSerializer();

    public bool TrySerialize(SnapshotType type, object? value, [NotNullWhen(true)] out SerializedSnapshot? result)
    {
        // The full string is what the compiler sees, trivia included. Normalizing the whitespace would
        // make the snapshot readable at the cost of hiding the formatting bugs it should be catching.
        var (source, language) = value switch
        {
            SyntaxTree tree => (tree.GetText().ToString(), tree.Options.Language),
            SyntaxNode node => (node.ToFullString(), node.Language),
            SyntaxToken token => (token.ToFullString(), token.Language),
            SyntaxNodeOrToken nodeOrToken => (nodeOrToken.ToFullString(), nodeOrToken.Language),
            SyntaxTrivia trivia => (trivia.ToFullString(), trivia.Language),
            SyntaxTokenList tokens => (tokens.ToFullString(), tokens.Count > 0 ? tokens[0].Language : null),
            SyntaxTriviaList trivias => (trivias.ToFullString(), trivias.Count > 0 ? trivias[0].Language : null),
            SourceText text => (text.ToString(), null),
            _ => (null, null),
        };

        if (source is null)
        {
            result = null;
            return false;
        }

        result = new SerializedSnapshot([new SnapshotData(GetExtension(language), Encoding.UTF8.GetBytes(source.ReplaceLineEndings("\n")))]);
        return true;
    }

    // An unknown language leaves the extension to the requested snapshot type, which defaults to txt.
    private static string? GetExtension(string? language) => language switch
    {
        LanguageNames.CSharp => "cs",
        LanguageNames.VisualBasic => "vb",
        _ => null,
    };
}
