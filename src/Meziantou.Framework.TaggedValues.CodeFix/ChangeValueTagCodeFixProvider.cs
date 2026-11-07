using System.Collections.Immutable;
using Meziantou.Framework.TaggedValues.Analyzer;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Simplification;
using Microsoft.CodeAnalysis.Text;

namespace Meziantou.Framework.TaggedValues.CodeFix;

/// <summary>
/// Changes the tags of the declaration a mismatched value flows to, or of an override, to the expected tags.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ChangeValueTagCodeFixProvider))]
public sealed class ChangeValueTagCodeFixProvider : CodeFixProvider
{
    // The actions share one key, so Fix All applies the change and add actions of every diagnostic of the scope
    private const string EquivalenceKey = nameof(ChangeValueTagCodeFixProvider);

    public override ImmutableArray<string> FixableDiagnosticIds => [ValueTagDiagnostics.FlowMismatchDiagnosticId, ValueTagDiagnostics.InheritedTagMismatchDiagnosticId, ValueTagDiagnostics.MissingReturnTagDiagnosticId, ValueTagDiagnostics.UntaggedValueDiagnosticId];

    public override FixAllProvider GetFixAllProvider()
    {
        return ChangeValueTagFixAllProvider.Instance;
    }

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var models = new SemanticModelCache(context.Document.Project.Solution);
        foreach (var diagnostic in context.Diagnostics)
        {
            if (await CreateEditAsync(models, diagnostic, context.CancellationToken).ConfigureAwait(false) is not { } edit)
                continue;

            context.RegisterCodeFix(
                CodeAction.Create(edit.Title, cancellationToken => ApplyAsync(models, [edit], cancellationToken), equivalenceKey: EquivalenceKey),
                diagnostic);
        }
    }

    /// <summary>
    /// Computes the edit that fixes a diagnostic, from the original syntax of the solution.
    /// </summary>
    private static async Task<Edit?> CreateEditAsync(SemanticModelCache models, Diagnostic diagnostic, CancellationToken cancellationToken)
    {
        if (diagnostic.AdditionalLocations.Count is 0 ||
            !diagnostic.Properties.TryGetValue(ValueTagDiagnostics.TagsProperty, out var serializedTags) || serializedTags is null ||
            !diagnostic.Properties.TryGetValue(ValueTagDiagnostics.TargetKindProperty, out var targetKind) || targetKind is null)
        {
            return null;
        }

        var tags = TagInfo.Deserialize(serializedTags);
        var targetLocation = diagnostic.AdditionalLocations[0];
        var document = models.Solution.GetDocument(targetLocation.SourceTree);
        if (tags.IsEmpty || document is null)
            return null;

        var semanticModel = await models.GetAsync(document, cancellationToken).ConfigureAwait(false);
        if (semanticModel is null)
            return null;

        var root = await semanticModel.SyntaxTree.GetRootAsync(cancellationToken).ConfigureAwait(false);
        var token = root.FindToken(targetLocation.SourceSpan.Start);
        if (targetKind is ValueTagTargetKind.Local)
            return CreateLocalEdit(document, token, tags);

        return await CreateAttributeEditAsync(models, document, semanticModel, token, tags, targetKind, diagnostic.Id, cancellationToken).ConfigureAwait(false);
    }

    private static LocalEdit? CreateLocalEdit(Document document, SyntaxToken identifier, TagInfo tags)
    {
        if (FindLocalComment(identifier) is not { } comment)
            return null;

        // Guid /* ValueTag=OrderId */ a, b: changing the shared comment would also change b, so a comment is added next to a instead
        var isShared = IsSharedComment(comment, identifier);
        var isLineComment = !isShared && comment.IsKind(SyntaxKind.SingleLineCommentTrivia);

        // A comment cannot declare every tag an attribute can, e.g. a tag that contains a space
        if (!ValueTagComment.CanFormat(tags, isLineComment))
            return null;

        var newComment = ValueTagComment.Format(tags, isLineComment);
        var title = "Change the tag of '" + identifier.ValueText + "' to " + newComment;
        var key = new EditKey(document.Id, isShared ? identifier.Span : comment.Span, TargetKeyword: null, IsLocal: true);
        return new LocalEdit(document, title, tags, key, identifier, comment, isShared, newComment);
    }

    private static async Task<AttributeEdit?> CreateAttributeEditAsync(SemanticModelCache models, Document document, SemanticModel semanticModel, SyntaxToken identifier, TagInfo tags, string targetKind, string diagnosticId, CancellationToken cancellationToken)
    {
        var declaration = GetDeclaration(identifier);
        if (declaration is null)
            return null;

        // The attributes of a record primary constructor parameter tag its property, unless the property has its own [property: ValueTag]
        SyntaxKind? targetKeyword = targetKind switch
        {
            ValueTagTargetKind.ReturnValue => SyntaxKind.ReturnKeyword,
            ValueTagTargetKind.RecordProperty when GetAttributeLists(declaration).Any(list => list.Target?.Identifier.IsKind(SyntaxKind.PropertyKeyword) is true && list.Attributes.Any(attribute => IsValueTagAttribute(semanticModel, attribute, cancellationToken))) => SyntaxKind.PropertyKeyword,
            _ => null,
        };

        // The attributes of the parts of a partial member are merged, so the tags of the other parts are removed too
        var otherParts = new List<DeclarationPart>();
        foreach (var otherPart in GetOtherPartialDeclarations(semanticModel, declaration, cancellationToken))
        {
            if (models.Solution.GetDocument(otherPart.SyntaxTree) is not { } otherDocument ||
                await models.GetAsync(otherDocument, cancellationToken).ConfigureAwait(false) is not { } otherSemanticModel ||
                otherSemanticModel.SyntaxTree != otherPart.SyntaxTree)
            {
                continue;
            }

            var otherDeclaration = await otherPart.GetSyntaxAsync(cancellationToken).ConfigureAwait(false);
            otherParts.Add(new DeclarationPart(otherDocument, otherDeclaration, GetValueTagAttributes(otherSemanticModel, otherDeclaration, targetKeyword, cancellationToken)));
        }

        var part = new DeclarationPart(document, declaration, GetValueTagAttributes(semanticModel, declaration, targetKeyword, cancellationToken));

        // Every part of a partial member has the same key, so that the diagnostics that target different parts are grouped
        var canonicalPart = otherParts.Prepend(part)
            .OrderBy(declarationPart => declarationPart.Document.FilePath ?? declarationPart.Document.Name, StringComparer.Ordinal)
            .ThenBy(declarationPart => declarationPart.Declaration.SpanStart)
            .First();
        var key = new EditKey(canonicalPart.Document.Id, canonicalPart.Declaration.Span, targetKeyword, IsLocal: false);

        var isReturnValue = targetKind is ValueTagTargetKind.ReturnValue;
        var title = diagnosticId is ValueTagDiagnostics.MissingReturnTagDiagnosticId or ValueTagDiagnostics.UntaggedValueDiagnosticId
            ? "Add " + tags.ToAttributeString(isReturnValue) + " to '" + identifier.ValueText + "'"
            : "Change the tag of '" + identifier.ValueText + "' to " + tags.ToAttributeString();
        return new AttributeEdit(document, title, tags, key, part, targetKeyword, otherParts);
    }

    private static ImmutableArray<AttributeSyntax> GetValueTagAttributes(SemanticModel semanticModel, SyntaxNode declaration, SyntaxKind? targetKeyword, CancellationToken cancellationToken)
    {
        return [.. GetAttributeLists(declaration)
            .Where(attributeList => IsTargetList(attributeList, targetKeyword))
            .SelectMany(attributeList => attributeList.Attributes)
            .Where(attribute => IsValueTagAttribute(semanticModel, attribute, cancellationToken))];
    }

    /// <summary>
    /// Applies the edits to the solution. The edits of a document are applied one after the other to the same root,
    /// in which annotations locate the nodes, tokens and comments of the original root.
    /// </summary>
    private static async Task<Solution> ApplyAsync(SemanticModelCache models, IReadOnlyList<Edit> edits, CancellationToken cancellationToken)
    {
        var documentEdits = new Dictionary<DocumentId, DocumentEdits>();
        var documentIds = new List<DocumentId>();
        foreach (var edit in edits)
        {
            switch (edit)
            {
                case LocalEdit localEdit:
                    GetDocumentEdits(localEdit.Document).LocalEdits.Add(localEdit);
                    break;

                case AttributeEdit attributeEdit:
                    GetDocumentEdits(attributeEdit.Document).AttributeEdits.Add(attributeEdit);
                    foreach (var otherPart in attributeEdit.OtherParts)
                    {
                        GetDocumentEdits(otherPart.Document).RemovedAttributes.AddRange(otherPart.ValueTagAttributes);
                    }

                    break;
            }
        }

        var solution = models.Solution;
        foreach (var documentId in documentIds)
        {
            var document = solution.GetDocument(documentId);
            if (document is null || await models.GetAsync(document, cancellationToken).ConfigureAwait(false) is not { } semanticModel)
                continue;

            var root = await semanticModel.SyntaxTree.GetRootAsync(cancellationToken).ConfigureAwait(false);
            solution = solution.WithDocumentSyntaxRoot(documentId, ApplyEdits(document, root, documentEdits[documentId]));
        }

        return solution;

        DocumentEdits GetDocumentEdits(Document document)
        {
            if (!documentEdits.TryGetValue(document.Id, out var result))
            {
                result = new DocumentEdits();
                documentEdits.Add(document.Id, result);
                documentIds.Add(document.Id);
            }

            return result;
        }
    }

    private static SyntaxNode ApplyEdits(Document document, SyntaxNode root, DocumentEdits edits)
    {
        var nodeAnnotations = new Dictionary<SyntaxNode, List<SyntaxAnnotation>>();
        var tokenAnnotations = new Dictionary<SyntaxToken, SyntaxAnnotation>();
        var triviaAnnotations = new Dictionary<SyntaxTrivia, SyntaxAnnotation>();
        var replacedAttributeAnnotation = new SyntaxAnnotation();
        var removedAttributeAnnotation = new SyntaxAnnotation();

        foreach (var edit in edits.LocalEdits)
        {
            if (edit.IsShared)
            {
                tokenAnnotations[edit.Identifier] = new SyntaxAnnotation();
            }
            else
            {
                triviaAnnotations[edit.Comment] = new SyntaxAnnotation();
            }
        }

        var declarationAnnotations = new Dictionary<SyntaxNode, SyntaxAnnotation>();
        foreach (var edit in edits.AttributeEdits)
        {
            if (!declarationAnnotations.ContainsKey(edit.Part.Declaration))
            {
                var annotation = new SyntaxAnnotation();
                declarationAnnotations.Add(edit.Part.Declaration, annotation);
                AddNodeAnnotation(edit.Part.Declaration, annotation);
            }

            foreach (var attribute in edit.Part.ValueTagAttributes)
            {
                AddNodeAnnotation(attribute, replacedAttributeAnnotation);
            }
        }

        foreach (var attribute in edits.RemovedAttributes)
        {
            AddNodeAnnotation(attribute, removedAttributeAnnotation);
        }

        root = root.ReplaceSyntax(
            nodeAnnotations.Keys,
            (original, rewritten) => rewritten.WithAdditionalAnnotations(nodeAnnotations[original]),
            tokenAnnotations.Keys,
            (original, rewritten) => rewritten.WithAdditionalAnnotations(tokenAnnotations[original]),
            triviaAnnotations.Keys,
            (original, rewritten) => rewritten.WithAdditionalAnnotations(triviaAnnotations[original]));

        foreach (var edit in edits.LocalEdits)
        {
            if (edit.IsShared)
            {
                var token = root.GetAnnotatedTokens(tokenAnnotations[edit.Identifier]).Single();
                root = root.ReplaceToken(token, token.WithTrailingTrivia(token.TrailingTrivia.InsertRange(0, [SyntaxFactory.Space, SyntaxFactory.Comment(edit.NewComment)])));
            }
            else
            {
                var comment = root.GetAnnotatedTrivia(triviaAnnotations[edit.Comment]).Single();
                root = root.ReplaceTrivia(comment, SyntaxFactory.Comment(edit.NewComment));
            }
        }

        var generator = SyntaxGenerator.GetGenerator(document);
        foreach (var edit in edits.AttributeEdits)
        {
            var declaration = root.GetAnnotatedNodes(declarationAnnotations[edit.Part.Declaration]).Single();
            root = root.ReplaceNode(declaration, ChangeAttribute(generator, declaration, edit.Tags, edit.TargetKeyword, replacedAttributeAnnotation));
        }

        var removedAttributes = root.GetAnnotatedNodes(removedAttributeAnnotation).ToList();
        if (removedAttributes.Count > 0)
        {
            var editor = new SyntaxEditor(root, document.Project.Solution.Services);
            foreach (var attribute in removedAttributes)
            {
                editor.RemoveNode(attribute);
            }

            root = editor.GetChangedRoot();
        }

        return root;

        void AddNodeAnnotation(SyntaxNode node, SyntaxAnnotation annotation)
        {
            if (!nodeAnnotations.TryGetValue(node, out var annotations))
            {
                annotations = [];
                nodeAnnotations.Add(node, annotations);
            }

            annotations.Add(annotation);
        }
    }

    /// <summary>
    /// Replaces the value tags of the target attribute lists of <paramref name="declaration"/>, located by <paramref name="replacedAttributeAnnotation"/>, with a new attribute.
    /// </summary>
    private static SyntaxNode ChangeAttribute(SyntaxGenerator generator, SyntaxNode declaration, TagInfo tags, SyntaxKind? targetKeyword, SyntaxAnnotation replacedAttributeAnnotation)
    {
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
                .Where(attribute => !IsTargetList(attributeList, targetKeyword) || !attribute.HasAnnotation(replacedAttributeAnnotation))
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

        return replaced ? WithAttributeLists(declaration, lists) : InsertAttributeList(declaration, lists, newAttributeList);
    }

    private static bool IsTargetList(AttributeListSyntax attributeList, SyntaxKind? targetKeyword)
    {
        return targetKeyword is { } targetListKeyword ? attributeList.Target?.Identifier.IsKind(targetListKeyword) is true : attributeList.Target is null;
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

    /// <summary>
    /// Returns the declarations of the other part of a partial method or property, or of the parameter at the same position.
    /// </summary>
    private static ImmutableArray<SyntaxReference> GetOtherPartialDeclarations(SemanticModel semanticModel, SyntaxNode declaration, CancellationToken cancellationToken)
    {
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

    /// <summary>
    /// Identifies what an edit changes: the comment of a local, the name of a local next to which a comment is added,
    /// or the attribute lists of a declaration with a given target. Two edits with the same key change the same code.
    /// </summary>
    private readonly record struct EditKey(DocumentId DocumentId, TextSpan Span, SyntaxKind? TargetKeyword, bool IsLocal);

    private abstract record Edit(Document Document, string Title, TagInfo Tags, EditKey Key);

    private sealed record LocalEdit(Document Document, string Title, TagInfo Tags, EditKey Key, SyntaxToken Identifier, SyntaxTrivia Comment, bool IsShared, string NewComment)
        : Edit(Document, Title, Tags, Key);

    private sealed record AttributeEdit(Document Document, string Title, TagInfo Tags, EditKey Key, DeclarationPart Part, SyntaxKind? TargetKeyword, IReadOnlyList<DeclarationPart> OtherParts)
        : Edit(Document, Title, Tags, Key);

    /// <summary>
    /// A declaration, or a part of a partial member, and its <c>[ValueTag]</c> attributes in the attribute lists of the target of the edit.
    /// </summary>
    private sealed record DeclarationPart(Document Document, SyntaxNode Declaration, ImmutableArray<AttributeSyntax> ValueTagAttributes);

    private sealed class DocumentEdits
    {
        public List<LocalEdit> LocalEdits { get; } = [];

        public List<AttributeEdit> AttributeEdits { get; } = [];

        public List<AttributeSyntax> RemovedAttributes { get; } = [];
    }

    /// <summary>
    /// Shares the semantic models of the documents, so that every edit locates the nodes of the same syntax roots.
    /// </summary>
    private sealed class SemanticModelCache(Solution solution)
    {
        private readonly Dictionary<DocumentId, SemanticModel?> _semanticModels = [];

        public Solution Solution => solution;

        public async Task<SemanticModel?> GetAsync(Document document, CancellationToken cancellationToken)
        {
            if (!_semanticModels.TryGetValue(document.Id, out var semanticModel))
            {
                semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
                _semanticModels[document.Id] = semanticModel;
            }

            return semanticModel;
        }
    }

    /// <summary>
    /// Applies the fixes of every diagnostic of the scope at once.
    /// </summary>
    /// <remarks>
    /// The edits are grouped by what they change: a declaration and its target (every part of a partial member being the same declaration),
    /// the comment of a local, or the name of a local next to which a comment is added. The edits of a group that suggest the same tags
    /// are applied once. When they suggest different tags, the group is left unchanged, and its diagnostics remain to be fixed one by one.
    /// </remarks>
    private sealed class ChangeValueTagFixAllProvider : FixAllProvider
    {
        public static ChangeValueTagFixAllProvider Instance { get; } = new();

        public override async Task<CodeAction?> GetFixAsync(FixAllContext fixAllContext)
        {
            var diagnostics = new List<Diagnostic>();
            switch (fixAllContext.Scope)
            {
                case FixAllScope.Document when fixAllContext.Document is not null:
                    diagnostics.AddRange(await fixAllContext.GetDocumentDiagnosticsAsync(fixAllContext.Document).ConfigureAwait(false));
                    break;

                case FixAllScope.Project:
                    diagnostics.AddRange(await fixAllContext.GetAllDiagnosticsAsync(fixAllContext.Project).ConfigureAwait(false));
                    break;

                case FixAllScope.Solution:
                    foreach (var project in fixAllContext.Solution.Projects)
                    {
                        diagnostics.AddRange(await fixAllContext.GetAllDiagnosticsAsync(project).ConfigureAwait(false));
                    }

                    break;
            }

            if (diagnostics.Count is 0)
                return null;

            return CodeAction.Create(
                "Change the value tags",
                cancellationToken => FixAllAsync(fixAllContext.Solution, diagnostics, cancellationToken),
                equivalenceKey: EquivalenceKey);
        }

        private static async Task<Solution> FixAllAsync(Solution solution, List<Diagnostic> diagnostics, CancellationToken cancellationToken)
        {
            var models = new SemanticModelCache(solution);
            var groups = new Dictionary<EditKey, List<Edit>>();
            var keys = new List<EditKey>();
            var orderedDiagnostics = diagnostics
                .OrderBy(diagnostic => diagnostic.Location.SourceTree?.FilePath, StringComparer.Ordinal)
                .ThenBy(diagnostic => diagnostic.Location.SourceSpan.Start)
                .ThenBy(diagnostic => diagnostic.Id, StringComparer.Ordinal);
            foreach (var diagnostic in orderedDiagnostics)
            {
                if (await CreateEditAsync(models, diagnostic, cancellationToken).ConfigureAwait(false) is not { } edit)
                    continue;

                if (!groups.TryGetValue(edit.Key, out var group))
                {
                    group = [];
                    groups.Add(edit.Key, group);
                    keys.Add(edit.Key);
                }

                group.Add(edit);
            }

            var edits = new List<Edit>();
            foreach (var key in keys)
            {
                var group = groups[key];
                var tags = group[0].Tags.Serialize();
                if (group.TrueForAll(edit => edit.Tags.Serialize() == tags))
                {
                    edits.Add(group[0]);
                }
            }

            return await ApplyAsync(models, edits, cancellationToken).ConfigureAwait(false);
        }
    }
}
