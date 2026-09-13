using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Simplification;

namespace Meziantou.Framework.Analyzers.TaggedValues;

/// <summary>
/// Changes the tags of the declaration a mismatched value flows to, or of an override, to the expected tags.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ChangeValueTagCodeFixProvider))]
public sealed class ChangeValueTagCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds => [ValueTagDiagnostics.FlowMismatchDiagnosticId, ValueTagDiagnostics.InheritedTagMismatchDiagnosticId];

    public override FixAllProvider? GetFixAllProvider()
    {
        return null;
    }

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        foreach (var diagnostic in context.Diagnostics)
        {
            if (diagnostic.AdditionalLocations.Count is 0 ||
                !diagnostic.Properties.TryGetValue(ValueTagDiagnostics.TagsProperty, out var serializedTags) || serializedTags is null ||
                !diagnostic.Properties.TryGetValue(ValueTagDiagnostics.TargetKindProperty, out var targetKind) || targetKind is null)
            {
                continue;
            }

            var tags = TagInfo.Deserialize(serializedTags);
            var targetLocation = diagnostic.AdditionalLocations[0];
            var document = context.Document.Project.Solution.GetDocument(targetLocation.SourceTree);
            if (tags.IsEmpty || document is null)
                continue;

            var root = await document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            if (root is null)
                continue;

            var token = root.FindToken(targetLocation.SourceSpan.Start);
            if (targetKind is ValueTagTargetKind.Local)
            {
                if (FindLocalComment(token) is not { } comment)
                    continue;

                var newComment = ValueTagComment.Format(tags, isLineComment: comment.IsKind(SyntaxKind.SingleLineCommentTrivia));
                var title = "Change the tag of '" + token.ValueText + "' to " + newComment;
                context.RegisterCodeFix(
                    CodeAction.Create(title, _ => Task.FromResult(document.WithSyntaxRoot(root.ReplaceTrivia(comment, SyntaxFactory.Comment(newComment)))), equivalenceKey: title),
                    diagnostic);
            }
            else
            {
                if (GetDeclaration(token) is null)
                    continue;

                var title = "Change the tag of '" + token.ValueText + "' to " + tags.ToAttributeString();
                context.RegisterCodeFix(
                    CodeAction.Create(title, cancellationToken => ChangeAttributeAsync(document, targetLocation.SourceSpan.Start, tags, isReturnValue: targetKind is ValueTagTargetKind.ReturnValue, cancellationToken), equivalenceKey: title),
                    diagnostic);
            }
        }
    }

    private static SyntaxTrivia? FindLocalComment(SyntaxToken identifier)
    {
        if (identifier.Parent is not { } declaration)
            return null;

        foreach (var trivia in TagResolver.GetLocalCommentTrivia(declaration))
        {
            if (ValueTagComment.Parse(trivia.ToString(), out _) is ValueTagCommentKind.Valid)
                return trivia;
        }

        return null;
    }

    private static SyntaxNode? GetDeclaration(SyntaxToken identifier)
    {
        foreach (var node in identifier.Parent?.AncestorsAndSelf() ?? [])
        {
            switch (node)
            {
                case ParameterSyntax or PropertyDeclarationSyntax or IndexerDeclarationSyntax or MethodDeclarationSyntax or LocalFunctionStatementSyntax:
                    return node;

                case VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax { Parent: FieldDeclarationSyntax field, Variables.Count: 1 } }:
                    return field;

                case VariableDeclaratorSyntax:
                    return null;
            }
        }

        return null;
    }

    private static async Task<Document> ChangeAttributeAsync(Document document, int position, TagInfo tags, bool isReturnValue, CancellationToken cancellationToken)
    {
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
        var declaration = GetDeclaration(editor.OriginalRoot.FindToken(position));
        if (declaration is null)
            return document;

        foreach (var attributeList in GetAttributeLists(declaration))
        {
            var isReturnList = attributeList.Target?.Identifier.IsKind(SyntaxKind.ReturnKeyword) is true;
            if (isReturnList != isReturnValue || (!isReturnValue && attributeList.Target is not null))
                continue;

            foreach (var attribute in attributeList.Attributes)
            {
                if (editor.SemanticModel.GetSymbolInfo(attribute, cancellationToken).Symbol is IMethodSymbol constructor && TagResolver.IsValueTagAttribute(constructor.ContainingType))
                {
                    editor.RemoveNode(attribute);
                }
            }
        }

        var generator = editor.Generator;
        var arguments = new List<SyntaxNode>();
        foreach (var tag in tags.Tags)
        {
            arguments.Add(generator.AttributeArgument(generator.LiteralExpression(tag)));
        }

        foreach (var key in tags.Key)
        {
            arguments.Add(generator.AttributeArgument("Key", generator.LiteralExpression(key)));
        }

        foreach (var value in tags.Value)
        {
            arguments.Add(generator.AttributeArgument("Value", generator.LiteralExpression(value)));
        }

        var attributeName = SyntaxFactory.ParseName("Meziantou.Framework.ValueTag").WithAdditionalAnnotations(Simplifier.Annotation);
        var newAttribute = generator.Attribute(attributeName, arguments);
        if (isReturnValue)
        {
            editor.AddReturnAttribute(declaration, newAttribute);
        }
        else
        {
            editor.AddAttribute(declaration, newAttribute);
        }

        return editor.GetChangedDocument();
    }

    private static SyntaxList<AttributeListSyntax> GetAttributeLists(SyntaxNode declaration)
    {
        return declaration switch
        {
            ParameterSyntax parameter => parameter.AttributeLists,
            MemberDeclarationSyntax member => member.AttributeLists,
            LocalFunctionStatementSyntax localFunction => localFunction.AttributeLists,
            _ => default,
        };
    }
}
