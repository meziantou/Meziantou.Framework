using System.Collections.Immutable;
using Meziantou.Framework.TaggedValues.Analyzer;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Simplification;

namespace Meziantou.Framework.TaggedValues.CodeFix;

/// <summary>
/// Changes the tags of the declaration a mismatched value flows to, or of an override, to the expected tags.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ChangeValueTagCodeFixProvider))]
public sealed class ChangeValueTagCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds => [ValueTagDiagnostics.FlowMismatchDiagnosticId, ValueTagDiagnostics.InheritedTagMismatchDiagnosticId, ValueTagDiagnostics.MissingReturnTagDiagnosticId, ValueTagDiagnostics.UntaggedValueDiagnosticId];

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

                var isReturnValue = targetKind is ValueTagTargetKind.ReturnValue;
                var title = diagnostic.Id is ValueTagDiagnostics.MissingReturnTagDiagnosticId or ValueTagDiagnostics.UntaggedValueDiagnosticId
                    ? "Add " + tags.ToAttributeString(isReturnValue) + " to '" + token.ValueText + "'"
                    : "Change the tag of '" + token.ValueText + "' to " + tags.ToAttributeString();
                context.RegisterCodeFix(
                    CodeAction.Create(title, cancellationToken => ChangeAttributeAsync(document, targetLocation.SourceSpan.Start, tags, isReturnValue, cancellationToken), equivalenceKey: title),
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

        var attributeName = SyntaxFactory.ParseName("Meziantou.Framework.TaggedValues.ValueTag").WithAdditionalAnnotations(Simplifier.Annotation);
        var newAttributeList = (AttributeListSyntax)generator.Attribute(attributeName, arguments);
        if (isReturnValue)
        {
            newAttributeList = newAttributeList.WithTarget(SyntaxFactory.AttributeTargetSpecifier(SyntaxFactory.Token(SyntaxKind.ReturnKeyword)));
        }

        var lists = new List<AttributeListSyntax>();
        var insertIndex = -1;
        foreach (var attributeList in GetAttributeLists(declaration))
        {
            var isReturnList = attributeList.Target?.Identifier.IsKind(SyntaxKind.ReturnKeyword) is true;
            var isTargetList = isReturnValue ? isReturnList : attributeList.Target is null;
            var attributes = attributeList.Attributes
                .Where(attribute => !isTargetList || !IsValueTagAttribute(editor.SemanticModel, attribute, cancellationToken))
                .ToList();

            // The new attribute replaces the first list that only contained value tags
            if (attributes.Count is 0)
            {
                if (insertIndex < 0)
                {
                    insertIndex = lists.Count;
                }

                continue;
            }

            lists.Add(attributes.Count == attributeList.Attributes.Count ? attributeList : attributeList.WithAttributes(SyntaxFactory.SeparatedList(attributes)));
        }

        lists.Insert(insertIndex < 0 ? lists.Count : insertIndex, newAttributeList);
        editor.ReplaceNode(declaration, SetAttributeLists(declaration, lists));
        return editor.GetChangedDocument();
    }

    private static bool IsValueTagAttribute(SemanticModel semanticModel, AttributeSyntax attribute, CancellationToken cancellationToken)
    {
        return semanticModel.GetSymbolInfo(attribute, cancellationToken).Symbol is IMethodSymbol constructor && TagResolver.IsValueTagAttribute(constructor.ContainingType);
    }

    private static SyntaxNode SetAttributeLists(SyntaxNode declaration, List<AttributeListSyntax> lists)
    {
        if (declaration is ParameterSyntax parameter)
        {
            // [ValueTag("OrderId")] Guid orderId: the attributes stay on the line of the parameter
            var parameterLeadingTrivia = parameter.GetLeadingTrivia();
            for (var i = 0; i < lists.Count; i++)
            {
                lists[i] = lists[i].WithLeadingTrivia(i is 0 ? parameterLeadingTrivia : SyntaxTriviaList.Empty).WithTrailingTrivia(SyntaxFactory.Space);
            }

            var parameterWithoutAttributes = parameter.WithAttributeLists(default).WithoutLeadingTrivia();
            return parameterWithoutAttributes.WithAttributeLists(SyntaxFactory.List(lists));
        }

        // One attribute list per line, with the indentation and the line endings of the declaration. The formatter is not used,
        // because it would use the line endings of the environment instead of the ones of the document.
        var leadingTrivia = declaration.GetLeadingTrivia();
        var indentation = leadingTrivia.Count > 0 && leadingTrivia[leadingTrivia.Count - 1].IsKind(SyntaxKind.WhitespaceTrivia)
            ? SyntaxFactory.TriviaList(leadingTrivia[leadingTrivia.Count - 1])
            : SyntaxTriviaList.Empty;
        var endOfLine = declaration.SyntaxTree.GetRoot().DescendantTrivia().FirstOrDefault(trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia));
        if (endOfLine.IsKind(SyntaxKind.None))
        {
            endOfLine = SyntaxFactory.ElasticCarriageReturnLineFeed;
        }

        for (var i = 0; i < lists.Count; i++)
        {
            lists[i] = lists[i].WithLeadingTrivia(i is 0 ? leadingTrivia : indentation).WithTrailingTrivia(endOfLine);
        }

        SyntaxNode withoutAttributes = declaration switch
        {
            MemberDeclarationSyntax member => member.WithAttributeLists(default),
            LocalFunctionStatementSyntax localFunction => localFunction.WithAttributeLists(default),
            _ => declaration,
        };

        withoutAttributes = withoutAttributes.WithLeadingTrivia(indentation);
        return withoutAttributes switch
        {
            MemberDeclarationSyntax member => member.WithAttributeLists(SyntaxFactory.List(lists)),
            LocalFunctionStatementSyntax localFunction => localFunction.WithAttributeLists(SyntaxFactory.List(lists)),
            _ => withoutAttributes,
        };
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
