using Microsoft.CodeAnalysis;

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
            _ => (null, null),
        };

        if (source is null)
        {
            result = null;
            return false;
        }

        result = new SerializedSnapshot([new SnapshotData(GetExtension(language), Encoding.UTF8.GetBytes(source.Replace("\r\n", "\n", StringComparison.Ordinal)))]);
        return true;
    }

    private static string GetExtension(string? language) => language switch
    {
        LanguageNames.CSharp => "cs",
        LanguageNames.VisualBasic => "vb",
        _ => "txt",
    };
}
