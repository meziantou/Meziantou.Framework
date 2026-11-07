using System.Collections.Immutable;
using Meziantou.Framework.TaggedValues.Analyzer;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;

namespace Meziantou.Framework.TaggedValues.CodeFix;

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
        // The batch fixer merges the edits of each diagnostic, which conflict when several comments precede the same token
        return FixAllProvider.Create(async (fixAllContext, document, diagnostics) =>
        {
            var root = await document.GetSyntaxRootAsync(fixAllContext.CancellationToken).ConfigureAwait(false);
            return root is null ? null : document.WithSyntaxRoot(Remove(document, root, diagnostics));
        });
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

            if (FindComment(root, diagnostic) is not null || FindAttribute(root, diagnostic) is not null)
            {
                context.RegisterCodeFix(
                    CodeAction.Create(Title, _ => Task.FromResult(context.Document.WithSyntaxRoot(Remove(context.Document, root, [diagnostic]))), equivalenceKey: Title),
                    diagnostic);
            }
        }
    }

    private static SyntaxTrivia? FindComment(SyntaxNode root, Diagnostic diagnostic)
    {
        var span = diagnostic.Location.SourceSpan;
        var trivia = root.FindTrivia(span.Start);
        return (trivia.IsKind(SyntaxKind.MultiLineCommentTrivia) || trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)) && trivia.Span == span ? trivia : null;
    }

    private static AttributeSyntax? FindAttribute(SyntaxNode root, Diagnostic diagnostic)
    {
        return root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true) as AttributeSyntax;
    }

    /// <summary>
    /// Removes the comments and the attributes located by the diagnostics, in a single edit so that the removals of adjacent comments do not conflict.
    /// </summary>
    private static SyntaxNode Remove(Document document, SyntaxNode root, IEnumerable<Diagnostic> diagnostics)
    {
        var comments = new List<SyntaxTrivia>();
        var attributes = new List<AttributeSyntax>();
        foreach (var diagnostic in diagnostics)
        {
            if (!diagnostic.Properties.ContainsKey(ValueTagDiagnostics.RemovableProperty))
                continue;

            if (FindComment(root, diagnostic) is { } comment)
            {
                comments.Add(comment);
            }
            else if (FindAttribute(root, diagnostic) is { } attribute)
            {
                attributes.Add(attribute);
            }
        }

        // The comments are removed first, as removing an attribute can move the comments around it
        var annotation = new SyntaxAnnotation();
        root = root.ReplaceNodes(attributes, (_, attribute) => attribute.WithAdditionalAnnotations(annotation));
        root = RemoveTrivia(root, comments.ConvertAll(comment => root.FindTrivia(comment.SpanStart)));

        var editor = new SyntaxEditor(root, document.Project.Solution.Services);
        foreach (var attribute in root.GetAnnotatedNodes(annotation))
        {
            editor.RemoveNode(attribute);
        }

        return editor.GetChangedRoot();
    }

    private static SyntaxNode RemoveTrivia(SyntaxNode root, List<SyntaxTrivia> comments)
    {
        var tokens = comments.Select(comment => comment.Token).Distinct().ToList();
        return root.ReplaceTokens(tokens, (token, _) =>
        {
            var leadingTrivia = RemoveTrivia(token.LeadingTrivia, comments, isLeading: true);
            var trailingTrivia = RemoveTrivia(token.TrailingTrivia, comments, isLeading: false);
            return token.WithLeadingTrivia(leadingTrivia).WithTrailingTrivia(trailingTrivia);
        });
    }

    private static SyntaxTriviaList RemoveTrivia(SyntaxTriviaList triviaList, List<SyntaxTrivia> comments, bool isLeading)
    {
        // The indexes are read from the original list, whose trivia belong to the token, and the comments are removed from the end,
        // so the indexes of the comments that remain to remove do not change
        var indexes = new List<int>();
        for (var index = 0; index < triviaList.Count; index++)
        {
            if (comments.Contains(triviaList[index]))
            {
                indexes.Add(index);
            }
        }

        for (var i = indexes.Count - 1; i >= 0; i--)
        {
            triviaList = RemoveTrivia(triviaList, indexes[i], isLeading);
        }

        return triviaList;
    }

    private static SyntaxTriviaList RemoveTrivia(SyntaxTriviaList triviaList, int index, bool isLeading)
    {
        var trivia = triviaList[index];
        var newTriviaList = triviaList.RemoveAt(index);

        // A comment on its own line: remove the whole line, i.e. the indentation before the comment and the line break after it.
        // A line comment at the end of a line: remove the spaces before it and keep the line break.
        var isAloneOnItsLine = isLeading && index < newTriviaList.Count && newTriviaList[index].IsKind(SyntaxKind.EndOfLineTrivia) &&
            (index is 0 || newTriviaList[index - 1].IsKind(SyntaxKind.EndOfLineTrivia) || (newTriviaList[index - 1].IsKind(SyntaxKind.WhitespaceTrivia) && (index - 1 is 0 || newTriviaList[index - 2].IsKind(SyntaxKind.EndOfLineTrivia))));
        if (trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) || isAloneOnItsLine)
        {
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

        return newTriviaList;
    }
}
