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

                // Guid /* ValueTag=OrderId */ a, b: changing the shared comment would also change b, so a comment is added next to a instead
                var isShared = IsSharedComment(comment, token);
                var isLineComment = !isShared && comment.IsKind(SyntaxKind.SingleLineCommentTrivia);

                // A comment cannot declare every tag an attribute can, e.g. a tag that contains a space
                if (!ValueTagComment.CanFormat(tags, isLineComment))
                    continue;

                var newComment = ValueTagComment.Format(tags, isLineComment);
                var title = "Change the tag of '" + token.ValueText + "' to " + newComment;
                var newRoot = isShared
                    ? root.ReplaceToken(token, token.WithTrailingTrivia(token.TrailingTrivia.InsertRange(0, [SyntaxFactory.Space, SyntaxFactory.Comment(newComment)])))
                    : root.ReplaceTrivia(comment, SyntaxFactory.Comment(newComment));
                context.RegisterCodeFix(
                    CodeAction.Create(title, _ => Task.FromResult(document.WithSyntaxRoot(newRoot)), equivalenceKey: title),
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
                    CodeAction.Create(title, cancellationToken => ChangeAttributeAsync(document, targetLocation.SourceSpan.Start, tags, targetKind, cancellationToken), equivalenceKey: title),
                    diagnostic);
            }
        }
    }

    /// <summary>
    /// Returns whether a comment tags the other variables of the declaration too, i.e. it is not next to the name of the variable.
    /// </summary>
    private static bool IsSharedComment(SyntaxTrivia comment, SyntaxToken identifier)
    {
        if (identifier.Parent is not VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax { Variables.Count: > 1 } variableDeclaration } declarator)
            return false;

        if (identifier.LeadingTrivia.Contains(comment) || identifier.TrailingTrivia.Contains(comment))
            return false;

        var index = variableDeclaration.Variables.IndexOf(declarator);
        return index is 0 || !variableDeclaration.Variables.GetSeparator(index - 1).TrailingTrivia.Contains(comment);
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
                // The element of a tuple cannot have attributes, and the declaration that contains the tuple is another value
                case TupleElementSyntax:
                    return null;

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

    private static async Task<Solution> ChangeAttributeAsync(Document document, int position, TagInfo tags, string targetKind, CancellationToken cancellationToken)
    {
        var solutionEditor = new SolutionEditor(document.Project.Solution);
        var editor = await solutionEditor.GetDocumentEditorAsync(document.Id, cancellationToken).ConfigureAwait(false);
        var declaration = GetDeclaration(editor.OriginalRoot.FindToken(position));
        if (declaration is null)
            return document.Project.Solution;

        // The attributes of a record primary constructor parameter tag its property, unless the property has its own [property: ValueTag]
        SyntaxKind? targetKeyword = targetKind switch
        {
            ValueTagTargetKind.ReturnValue => SyntaxKind.ReturnKeyword,
            ValueTagTargetKind.RecordProperty when GetAttributeLists(declaration).Any(list => list.Target?.Identifier.IsKind(SyntaxKind.PropertyKeyword) is true && list.Attributes.Any(attribute => IsValueTagAttribute(editor.SemanticModel, attribute, cancellationToken))) => SyntaxKind.PropertyKeyword,
            _ => null,
        };

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
        // Without elastic trivia, so the formatter does not reindent the code around the attribute
        var newAttributeList = ((AttributeListSyntax)generator.Attribute(attributeName, arguments)).NormalizeWhitespace();
        if (targetKeyword is { } keyword)
        {
            newAttributeList = newAttributeList.WithTarget(SyntaxFactory.AttributeTargetSpecifier(SyntaxFactory.Token(keyword)));
        }

        var lists = new List<AttributeListSyntax>();
        var replaced = false;
        foreach (var attributeList in GetAttributeLists(declaration))
        {
            var attributes = attributeList.Attributes
                .Where(attribute => !IsTargetList(attributeList, targetKeyword) || !IsValueTagAttribute(editor.SemanticModel, attribute, cancellationToken))
                .ToList();

            if (attributes.Count == attributeList.Attributes.Count)
            {
                lists.Add(attributeList);
            }
            else if (attributes.Count > 0)
            {
                lists.Add(attributeList.WithAttributes(SyntaxFactory.SeparatedList(attributes)));
            }
            else if (!replaced)
            {
                // The new attribute replaces the first list that only contained value tags, and keeps its comments and its layout
                lists.Add(newAttributeList.WithTriviaFrom(attributeList));
                replaced = true;
            }
        }

        editor.ReplaceNode(declaration, replaced ? WithAttributeLists(declaration, lists) : InsertAttributeList(declaration, lists, newAttributeList));

        // The attributes of the parts of a partial member are merged, so the tags of the other part are removed too
        foreach (var otherPart in await GetOtherPartialDeclarationsAsync(document, declaration, cancellationToken).ConfigureAwait(false))
        {
            if (document.Project.Solution.GetDocument(otherPart.SyntaxTree) is not { } otherDocument)
                continue;

            var otherEditor = await solutionEditor.GetDocumentEditorAsync(otherDocument.Id, cancellationToken).ConfigureAwait(false);
            var otherDeclaration = otherPart.GetSyntax(cancellationToken);
            foreach (var attributeList in GetAttributeLists(otherDeclaration))
            {
                if (!IsTargetList(attributeList, targetKeyword))
                    continue;

                foreach (var attribute in attributeList.Attributes)
                {
                    if (IsValueTagAttribute(otherEditor.SemanticModel, attribute, cancellationToken))
                    {
                        otherEditor.RemoveNode(attribute);
                    }
                }
            }
        }

        return solutionEditor.GetChangedSolution();

        static bool IsTargetList(AttributeListSyntax attributeList, SyntaxKind? targetKeyword)
        {
            return targetKeyword is { } targetListKeyword ? attributeList.Target?.Identifier.IsKind(targetListKeyword) is true : attributeList.Target is null;
        }
    }

    /// <summary>
    /// Returns the declarations of the other part of a partial method or property, or of the parameter at the same position.
    /// </summary>
    private static async Task<IEnumerable<SyntaxReference>> GetOtherPartialDeclarationsAsync(Document document, SyntaxNode declaration, CancellationToken cancellationToken)
    {
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (semanticModel is null)
            return [];

        var symbol = semanticModel.GetDeclaredSymbol(declaration, cancellationToken);
        var otherPart = symbol switch
        {
            IParameterSymbol { ContainingSymbol: IMethodSymbol method } parameter => GetParameter(method.PartialImplementationPart ?? method.PartialDefinitionPart, parameter.Ordinal),
            IParameterSymbol { ContainingSymbol: IPropertySymbol property } parameter => GetParameter(property.PartialImplementationPart ?? property.PartialDefinitionPart, parameter.Ordinal),
            IMethodSymbol method => method.PartialImplementationPart ?? method.PartialDefinitionPart,
            IPropertySymbol property => property.PartialImplementationPart ?? property.PartialDefinitionPart,
            _ => null,
        };

        return otherPart?.DeclaringSyntaxReferences ?? [];

        static ISymbol? GetParameter(ISymbol? member, int ordinal)
        {
            var parameters = member switch
            {
                IMethodSymbol method => method.Parameters,
                IPropertySymbol property => property.Parameters,
                _ => [],
            };

            return ordinal < parameters.Length ? parameters[ordinal] : null;
        }
    }

    private static bool IsValueTagAttribute(SemanticModel semanticModel, AttributeSyntax attribute, CancellationToken cancellationToken)
    {
        return semanticModel.GetSymbolInfo(attribute, cancellationToken).Symbol is IMethodSymbol constructor && TagResolver.IsValueTagAttribute(constructor.ContainingType);
    }

    private static SyntaxNode WithAttributeLists(SyntaxNode declaration, List<AttributeListSyntax> lists)
    {
        return declaration switch
        {
            ParameterSyntax parameter => parameter.WithAttributeLists(SyntaxFactory.List(lists)),
            MemberDeclarationSyntax member => member.WithAttributeLists(SyntaxFactory.List(lists)),
            LocalFunctionStatementSyntax localFunction => localFunction.WithAttributeLists(SyntaxFactory.List(lists)),
            _ => declaration,
        };
    }

    /// <summary>
    /// Adds a new attribute list to a declaration, keeping the comments of its other attribute lists.
    /// </summary>
    private static SyntaxNode InsertAttributeList(SyntaxNode declaration, List<AttributeListSyntax> lists, AttributeListSyntax newAttributeList)
    {
        if (declaration is ParameterSyntax parameter)
        {
            // [ValueTag("OrderId")] Guid orderId: the attribute stays on the line of the parameter
            if (lists.Count is 0)
            {
                var parameterLeadingTrivia = parameter.GetLeadingTrivia();
                return parameter.WithoutLeadingTrivia().WithAttributeLists(SyntaxFactory.SingletonList(newAttributeList.WithLeadingTrivia(parameterLeadingTrivia).WithTrailingTrivia(SyntaxFactory.Space)));
            }

            lists.Add(newAttributeList.WithTrailingTrivia(SyntaxFactory.Space));
            return parameter.WithAttributeLists(SyntaxFactory.List(lists));
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

        if (lists.Count > 0)
        {
            // After the last list when it ends its line, and before the first one otherwise, e.g. for [Obsolete] public Guid Id
            if (lists[lists.Count - 1].GetTrailingTrivia().Any(trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia)))
            {
                lists.Add(newAttributeList.WithLeadingTrivia(indentation).WithTrailingTrivia(endOfLine));
            }
            else
            {
                lists.Insert(0, newAttributeList.WithLeadingTrivia(lists[0].GetLeadingTrivia()).WithTrailingTrivia(endOfLine));
                lists[1] = lists[1].WithLeadingTrivia(indentation);
            }

            return WithAttributeLists(declaration, lists);
        }

        SyntaxNode withoutLeadingTrivia = declaration.WithLeadingTrivia(indentation);
        return WithAttributeLists(withoutLeadingTrivia, [newAttributeList.WithLeadingTrivia(leadingTrivia).WithTrailingTrivia(endOfLine)]);
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
