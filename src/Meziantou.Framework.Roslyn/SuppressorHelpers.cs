#if !MEZIANTOU_FRAMEWORK_ROSLYN_ENABLE_WARNINGS
#pragma warning disable
#endif
#nullable enable
using System.Threading;
using Microsoft.CodeAnalysis;

namespace Meziantou.Framework.Roslyn;

internal static partial class SuppressorHelpers
{
    public static SyntaxNode? FindNode(this Diagnostic diagnostic, CancellationToken cancellationToken)
    {
        return FindNode(diagnostic.Location, cancellationToken);
    }

    private static SyntaxNode? FindNode(Location? location, CancellationToken cancellationToken)
    {
        if (location is null)
            return null;

        var syntaxTree = location.SourceTree;
        if (syntaxTree is null)
            return null;

        var root = syntaxTree.GetRoot(cancellationToken);
        return root.FindNode(location.SourceSpan);
    }
}
