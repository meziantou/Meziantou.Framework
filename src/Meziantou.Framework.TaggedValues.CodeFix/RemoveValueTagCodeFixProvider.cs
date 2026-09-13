using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;

namespace Meziantou.Framework.Analyzers.TaggedValues;

/// <summary>
/// Removes an invalid or redundant <c>[ValueTag]</c> attribute or <c>/* ValueTag=... */</c> comment.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(RemoveValueTagCodeFixProvider))]
public sealed class RemoveValueTagCodeFixProvider : CodeFixProvider
{
    private const string Title = "Remove the value tag annotation";

    public override ImmutableArray<string> FixableDiagnosticIds => [ValueTagDiagnostics.InvalidAnnotationDiagnosticId, ValueTagDiagnostics.RedundantTagDiagnosticId];

    public override FixAllProvider GetFixAllProvider()
    {
        return WellKnownFixAllProviders.BatchFixer;
    }

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
            return;

        foreach (var diagnostic in context.Diagnostics)
        {
            if (!diagnostic.Properties.ContainsKey(ValueTagDiagnostics.RemovableProperty))
                continue;

            var span = diagnostic.Location.SourceSpan;
            var trivia = root.FindTrivia(span.Start);
            if ((trivia.IsKind(SyntaxKind.MultiLineCommentTrivia) || trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)) && trivia.Span == span)
            {
                context.RegisterCodeFix(
                    CodeAction.Create(Title, _ => Task.FromResult(context.Document.WithSyntaxRoot(RemoveTrivia(root, trivia))), equivalenceKey: Title),
                    diagnostic);
                continue;
            }

            if (root.FindNode(span, getInnermostNodeForTie: true) is AttributeSyntax)
            {
                context.RegisterCodeFix(
                    CodeAction.Create(Title, cancellationToken => RemoveAttributeAsync(context.Document, span, cancellationToken), equivalenceKey: Title),
                    diagnostic);
            }
        }
    }

    private static SyntaxNode RemoveTrivia(SyntaxNode root, SyntaxTrivia trivia)
    {
        var token = trivia.Token;
        var isLeading = token.LeadingTrivia.Contains(trivia);
        var triviaList = isLeading ? token.LeadingTrivia : token.TrailingTrivia;
        var index = triviaList.IndexOf(trivia);

        var newTriviaList = triviaList.RemoveAt(index);
        if (trivia.IsKind(SyntaxKind.SingleLineCommentTrivia))
        {
            // A comment on its own line: remove the whole line, i.e. the indentation before the comment and the line break after it.
            // A comment at the end of a line: remove the spaces before it and keep the line break.
            if (isLeading && index < newTriviaList.Count && newTriviaList[index].IsKind(SyntaxKind.EndOfLineTrivia))
            {
                newTriviaList = newTriviaList.RemoveAt(index);
            }

            if (index > 0 && newTriviaList[index - 1].IsKind(SyntaxKind.WhitespaceTrivia) && (!isLeading || index - 1 is 0 || newTriviaList[index - 2].IsKind(SyntaxKind.EndOfLineTrivia)))
            {
                newTriviaList = newTriviaList.RemoveAt(index - 1);
            }
        }
        else if (index < newTriviaList.Count && newTriviaList[index].IsKind(SyntaxKind.WhitespaceTrivia))
        {
            newTriviaList = newTriviaList.RemoveAt(index);
        }
        else if (index > 0 && newTriviaList[index - 1].IsKind(SyntaxKind.WhitespaceTrivia) && (index == newTriviaList.Count || !isLeading))
        {
            newTriviaList = newTriviaList.RemoveAt(index - 1);
        }

        var newToken = isLeading ? token.WithLeadingTrivia(newTriviaList) : token.WithTrailingTrivia(newTriviaList);
        return root.ReplaceToken(token, newToken);
    }

    private static async Task<Document> RemoveAttributeAsync(Document document, Microsoft.CodeAnalysis.Text.TextSpan span, CancellationToken cancellationToken)
    {
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
        if (editor.OriginalRoot.FindNode(span, getInnermostNodeForTie: true) is AttributeSyntax attribute)
        {
            editor.RemoveNode(attribute);
        }

        return editor.GetChangedDocument();
    }
}
