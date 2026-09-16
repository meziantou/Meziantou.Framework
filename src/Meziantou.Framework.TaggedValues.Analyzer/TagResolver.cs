using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Meziantou.Framework.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.TaggedValues.Analyzer;

/// <summary>
/// Computes the tags of symbols and expressions for one compilation.
/// </summary>
internal sealed class TagResolver
{
    private const int MaxDepth = 100;

    /// <summary>
    /// Set when a computation reached <see cref="MaxDepth"/>, so its result depends on the depth it started from.
    /// The computations of a resolver are synchronous, so the flag of the current thread describes the current computation.
    /// </summary>
    [ThreadStatic]
    private static bool s_truncated;

    private readonly Compilation _compilation;
    private readonly AnalyzerConfigOptionsProvider _optionsProvider;
    private readonly ConcurrentDictionary<ISymbol, TagInfo> _declaredTags = new(SymbolEqualityComparer.Default);
    private readonly ConcurrentDictionary<ISymbol, TagInfo> _localTags = new(SymbolEqualityComparer.Default);
    private readonly ConditionalWeakTable<IOperation, OperationTagCache> _operationTags = new();
    private readonly ConcurrentDictionary<SyntaxTree, bool> _conventionsEnabled = new();
    private readonly ConcurrentDictionary<SyntaxTree, bool> _strictModeEnabled = new();
    private readonly Lazy<ExternalTags> _externalTags;

    public TagResolver(Compilation compilation, AnalyzerConfigOptionsProvider optionsProvider)
    {
        _compilation = compilation;
        _optionsProvider = optionsProvider;
        _externalTags = new(LoadExternalTags);
        KnownTypes = new KnownTypes(compilation);
    }

    public KnownTypes KnownTypes { get; }

    public static bool IsValueTagAttribute(INamedTypeSymbol? type)
    {
        return type is { Name: "ValueTagAttribute", ContainingType: null, ContainingNamespace: { Name: "TaggedValues", ContainingNamespace: { Name: "Framework", ContainingNamespace: { Name: "Meziantou", ContainingNamespace.IsGlobalNamespace: true } } } };
    }

    public static bool HasValueTagAttributeType(Compilation compilation)
    {
        return !compilation.GetTypesByMetadataName("Meziantou.Framework.TaggedValues.ValueTagAttribute").IsEmpty;
    }

    public bool AreConventionsEnabled(SyntaxTree tree)
    {
        return IsOptionEnabled(_conventionsEnabled, tree, ValueTagDiagnostics.InferTagsFromNamesOption);
    }

    public bool IsStrictModeEnabled(SyntaxTree tree)
    {
        return IsOptionEnabled(_strictModeEnabled, tree, ValueTagDiagnostics.StrictOption);
    }

    private bool IsOptionEnabled(ConcurrentDictionary<SyntaxTree, bool> cache, SyntaxTree tree, string optionName)
    {
        if (cache.TryGetValue(tree, out var enabled))
            return enabled;

        enabled = _optionsProvider.GetOptions(tree).TryGetValue(optionName, out var value) && bool.TryParse(value, out var parsed) && parsed;
        cache.TryAdd(tree, enabled);
        return enabled;
    }

    /// <summary>
    /// Reads a <c>[ValueTag]</c> attribute applied to a member. Returns <see cref="TagInfo.None"/> for the assembly form.
    /// </summary>
    public static TagInfo ReadMemberAttribute(AttributeData attribute)
    {
        if (!IsValueTagAttribute(attribute.AttributeClass) || attribute.ConstructorArguments.Length is not 1)
            return TagInfo.None;

        return ReadAttribute(attribute, attribute.ConstructorArguments[0]);
    }

    public static TagInfo GetExplicitTags(ImmutableArray<AttributeData> attributes)
    {
        var result = TagInfo.None;
        foreach (var attribute in attributes)
        {
            result = TagInfo.Union(result, ReadMemberAttribute(attribute));
        }

        return result;
    }

    /// <summary>
    /// Returns the tags written on the symbol itself: the attributes of a field, a property or a parameter, or the return value attributes of a method.
    /// </summary>
    public static TagInfo GetOwnExplicitTags(ISymbol symbol)
    {
        return symbol switch
        {
            IMethodSymbol method => GetExplicitTags(method.GetReturnTypeAttributes()),
            IFieldSymbol or IPropertySymbol or IParameterSymbol => GetExplicitTags(symbol.GetAttributes()),
            _ => TagInfo.None,
        };
    }

    /// <summary>
    /// Returns the tags of a field, a property, a parameter, or the return value of a method, ignoring where it is accessed from.
    /// </summary>
    public TagInfo GetDeclaredTags(ISymbol symbol)
    {
        symbol = symbol.OriginalDefinition;
        return _declaredTags.GetOrAdd(symbol, symbol =>
        {
            // The implicit 'value' parameter of a setter has the tags of its property
            if (symbol is IParameterSymbol { ContainingSymbol: IMethodSymbol { MethodKind: MethodKind.PropertySet, AssociatedSymbol: IPropertySymbol property } setter } parameter &&
                parameter.Ordinal == setter.Parameters.Length - 1)
            {
                return GetDeclaredTags(property);
            }

            var tags = GetExplicitAndInheritedTags(symbol);
            if (!tags.IsEmpty)
                return tags;

            if (symbol is IPropertySymbol recordProperty)
            {
                tags = GetRecordPrimaryConstructorParameterTags(recordProperty);
                if (!tags.IsEmpty)
                    return tags;
            }

            return GetConventionTags(symbol);
        });
    }

    /// <summary>
    /// Returns the tags of a field or a property accessed through a receiver of type <paramref name="receiverType"/>,
    /// or the declared tags of any other symbol.
    /// </summary>
    public TagInfo GetMemberTags(ISymbol member, ITypeSymbol? receiverType)
    {
        if (member is not (IFieldSymbol or IPropertySymbol))
            return GetDeclaredTags(member);

        var external = GetExternalTags(member, receiverType);
        if (!external.IsEmpty)
            return external;

        var declared = GetDeclaredTags(member);
        if (declared.IsEmpty || declared.IsExplicit || !IsConventionIdName(member))
            return declared;

        // An inherited 'Id' is also an id of every type between the receiver and the declaring type
        var result = declared;
        for (var type = receiverType as INamedTypeSymbol; type is not null; type = type.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, member.ContainingType?.OriginalDefinition))
                break;

            if (IsConventionType(type))
            {
                result = TagInfo.Union(result, TagInfo.Create([type.Name + "Id"], isExplicit: false));
            }
        }

        return result;
    }

    /// <summary>
    /// Returns whether the naming convention reads the symbol as an <c>Id</c>, such as <c>Id</c>, <c>_id</c>, or <c>id</c>.
    /// </summary>
    public static bool IsConventionIdName(ISymbol symbol)
    {
        return string.Equals(GetConventionName(symbol), "Id", StringComparison.OrdinalIgnoreCase);
    }

    public static string? GetConventionName(ISymbol symbol)
    {
        switch (symbol)
        {
            case IFieldSymbol field:
                // s_orderId and _orderId both read as orderId
                var name = field.Name.StartsWith("s_", StringComparison.Ordinal) ? field.Name.Substring(2) : field.Name;
                name = name.TrimStart('_');
                return name.Length is 0 ? null : name;

            case IPropertySymbol { IsIndexer: false } property:
                return property.Name;

            case IParameterSymbol parameter:
                return parameter.Name.Length is 0 ? null : parameter.Name;

            default:
                return null;
        }
    }

    /// <summary>
    /// Returns the tag the naming convention infers for the symbol, when conventions are enabled where it is declared.
    /// </summary>
    public TagInfo GetConventionTags(ISymbol symbol)
    {
        if (symbol.IsImplicitlyDeclared && !IsRecordPrimaryConstructorProperty(symbol))
            return TagInfo.None;

        if (symbol.DeclaringSyntaxReferences.IsEmpty || !AreConventionsEnabled(symbol.DeclaringSyntaxReferences[0].SyntaxTree))
            return TagInfo.None;

        var (type, containingType) = symbol switch
        {
            IFieldSymbol field => (field.Type, field.ContainingType),
            IPropertySymbol property => (property.Type, property.ContainingType),
            IParameterSymbol parameter => (parameter.Type, parameter.ContainingType),
            _ => default,
        };

        if (type is null || containingType is null || containingType.IsAnonymousType || KnownTypes.IsCollectionType(type))
            return TagInfo.None;

        var name = GetConventionName(symbol);
        if (name is null)
            return TagInfo.None;

        if (string.Equals(name, "Id", StringComparison.OrdinalIgnoreCase))
        {
            // Foo(Bar id) is a BarId, while Id on Sample is a SampleId
            if (symbol is IParameterSymbol)
                return GetIdParameterConventionTags(type);

            return TagInfo.Create([containingType.Name + "Id"], isExplicit: false);
        }

        if (name.Length > 2 && name.EndsWith("Id", StringComparison.Ordinal))
            return TagInfo.Create([name], isExplicit: false);

        return TagInfo.None;
    }

    private TagInfo GetIdParameterConventionTags(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullableType)
        {
            type = nullableType.TypeArguments[0];
        }

        // A primitive such as Guid id does not say what it identifies
        if (type is not INamedTypeSymbol { SpecialType: SpecialType.None, TypeKind: TypeKind.Class or TypeKind.Struct or TypeKind.Interface, IsAnonymousType: false } namedType || KnownTypes.IsGuid(namedType))
            return TagInfo.None;

        // UserId id is a UserId, not a UserIdId
        var tag = namedType.Name.Length > 2 && namedType.Name.EndsWith("Id", StringComparison.Ordinal) ? namedType.Name : namedType.Name + "Id";
        return TagInfo.Create([tag], isExplicit: false);
    }

    private bool IsConventionType(INamedTypeSymbol type)
    {
        return !type.DeclaringSyntaxReferences.IsEmpty && AreConventionsEnabled(type.DeclaringSyntaxReferences[0].SyntaxTree);
    }

    /// <summary>
    /// Returns whether the symbol is a property generated from a parameter of the primary constructor of a record, e.g. <c>Id</c> in <c>record Order(Guid Id)</c>.
    /// </summary>
    public static bool IsRecordPrimaryConstructorProperty(ISymbol symbol)
    {
        return symbol is IPropertySymbol { ContainingType.IsRecord: true } property &&
            property.DeclaringSyntaxReferences.Length > 0 &&
            property.DeclaringSyntaxReferences[0].GetSyntax() is ParameterSyntax;
    }

    private static TagInfo ReadAttribute(AttributeData attribute, TypedConstant tagsArgument)
    {
        var tags = new List<string>();
        if (tagsArgument.Kind is TypedConstantKind.Array && !tagsArgument.IsNull)
        {
            foreach (var value in tagsArgument.Values)
            {
                if (value.Value is string tag)
                {
                    tags.Add(tag);
                }
            }
        }

        string? key = null;
        string? tagValue = null;
        foreach (var namedArgument in attribute.NamedArguments)
        {
            switch (namedArgument.Key)
            {
                case "Key":
                    key = namedArgument.Value.Value as string;
                    break;
                case "Value":
                    tagValue = namedArgument.Value.Value as string;
                    break;
            }
        }

        return TagInfo.Create(tags, key is null ? [] : [key], tagValue is null ? [] : [tagValue], isExplicit: true);
    }

    public static TagInfo GetExplicitAndInheritedTags(ISymbol symbol)
    {
        var tags = GetOwnExplicitTags(symbol);
        if (!tags.IsEmpty)
            return tags;

        foreach (var baseSymbol in GetOverriddenOrImplementedSymbols(symbol))
        {
            tags = TagInfo.Union(tags, GetExplicitAndInheritedTags(baseSymbol));
        }

        return tags;
    }

    /// <summary>
    /// Returns the members a field, property, method or parameter overrides or implements.
    /// </summary>
    public static IEnumerable<ISymbol> GetOverriddenOrImplementedSymbols(ISymbol symbol)
    {
        switch (symbol)
        {
            case IPropertySymbol property:
                if (property.OverriddenProperty is not null)
                    return [property.OverriddenProperty];

                return GetImplementedInterfaceMembers(property, property.ExplicitInterfaceImplementations);

            case IMethodSymbol method:
                if (method.OverriddenMethod is not null)
                    return [method.OverriddenMethod];

                return GetImplementedInterfaceMembers(method, method.ExplicitInterfaceImplementations);

            case IParameterSymbol { ContainingSymbol: IMethodSymbol containingMethod } parameter:
                return GetOverriddenOrImplementedSymbols(containingMethod)
                    .OfType<IMethodSymbol>()
                    .Where(baseMethod => parameter.Ordinal < baseMethod.Parameters.Length)
                    .Select(baseMethod => (ISymbol)baseMethod.Parameters[parameter.Ordinal])
                    .ToArray();

            default:
                return [];
        }
    }

    private static ISymbol[] GetImplementedInterfaceMembers<TSymbol>(TSymbol symbol, ImmutableArray<TSymbol> explicitImplementations)
        where TSymbol : class, ISymbol
    {
        if (!explicitImplementations.IsEmpty)
            return explicitImplementations.Cast<ISymbol>().ToArray();

        var containingType = symbol.ContainingType;
        if (containingType is null || symbol.IsStatic)
            return [];

        List<ISymbol>? result = null;
        foreach (var @interface in containingType.AllInterfaces)
        {
            foreach (var member in @interface.GetMembers(symbol.Name))
            {
                if (member is TSymbol && SymbolEqualityComparer.Default.Equals(containingType.FindImplementationForInterfaceMember(member), symbol))
                {
                    result ??= [];
                    result.Add(member);
                }
            }
        }

        return result?.ToArray() ?? [];
    }

    private static TagInfo GetRecordPrimaryConstructorParameterTags(IPropertySymbol property)
    {
        if (!property.ContainingType.IsRecord)
            return TagInfo.None;

        // In source, the property and its parameter share their declaration, so only the primary constructor matches.
        // In metadata, the primary constructor cannot be told apart from the other constructors, so the parameters are matched by name.
        var propertySyntax = property.DeclaringSyntaxReferences.FirstOrDefault();
        foreach (var constructor in property.ContainingType.InstanceConstructors)
        {
            foreach (var parameter in constructor.Parameters)
            {
                var isPrimaryConstructorParameter = propertySyntax is null
                    ? parameter.Name == property.Name && SymbolEqualityComparer.Default.Equals(parameter.Type, property.Type)
                    : parameter.DeclaringSyntaxReferences.Any(reference => reference.SyntaxTree == propertySyntax.SyntaxTree && reference.Span == propertySyntax.Span);

                if (isPrimaryConstructorParameter)
                {
                    var tags = GetExplicitTags(parameter.GetAttributes());
                    if (!tags.IsEmpty)
                        return tags;
                }
            }
        }

        return TagInfo.None;
    }

    private ExternalTags LoadExternalTags()
    {
        var result = new ExternalTags();
        AddAssembly(_compilation.Assembly);
        foreach (var assembly in _compilation.SourceModule.ReferencedAssemblySymbols)
        {
            AddAssembly(assembly);
        }

        return result;

        void AddAssembly(IAssemblySymbol assembly)
        {
            foreach (var attribute in assembly.GetAttributes())
            {
                if (TryReadExternalAttribute(attribute, out var type, out var memberName, out var tags) && !tags.IsEmpty)
                {
                    var key = (type.OriginalDefinition, memberName);
                    result.Tags[key] = result.Tags.TryGetValue(key, out var existing) ? TagInfo.Union(existing, tags) : tags;
                    result.MemberNames.Add(memberName);
                }
            }
        }
    }

    public static bool TryReadExternalAttribute(AttributeData attribute, [NotNullWhen(true)] out ITypeSymbol? type, [NotNullWhen(true)] out string? memberName, out TagInfo tags)
    {
        type = null;
        memberName = null;
        tags = TagInfo.None;
        if (!IsValueTagAttribute(attribute.AttributeClass) || attribute.ConstructorArguments.Length is not 3)
            return false;

        if (attribute.ConstructorArguments[0].Value is not ITypeSymbol typeValue || attribute.ConstructorArguments[1].Value is not string memberNameValue)
            return false;

        type = typeValue;
        memberName = memberNameValue;
        tags = ReadAttribute(attribute, attribute.ConstructorArguments[2]);
        return true;
    }

    private TagInfo GetExternalTags(ISymbol member, ITypeSymbol? receiverType)
    {
        // Most members are not tagged, so check the name before walking the type hierarchy
        var externalTags = _externalTags.Value;
        if (!externalTags.MemberNames.Contains(member.Name))
            return TagInfo.None;

        var result = TagInfo.None;
        var startType = receiverType as INamedTypeSymbol ?? member.ContainingType;
        for (var type = startType; type is not null; type = type.BaseType)
        {
            if (externalTags.Tags.TryGetValue((type.OriginalDefinition, member.Name), out var tags))
            {
                result = TagInfo.Union(result, tags);
            }
        }

        if (startType is not null)
        {
            foreach (var @interface in startType.AllInterfaces)
            {
                if (externalTags.Tags.TryGetValue((@interface.OriginalDefinition, member.Name), out var tags))
                {
                    result = TagInfo.Union(result, tags);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Returns the tags of a local variable: its <c>/* ValueTag=... */</c> comment, or the tags inferred from its initializer.
    /// </summary>
    public TagInfo GetLocalTags(ILocalSymbol local, SemanticModel? semanticModel, int depth = 0)
    {
        if (_localTags.TryGetValue(local, out var cached))
            return cached;

        var tags = GetLocalCommentTags(local);
        if (tags.IsEmpty)
        {
            if (semanticModel is null)
                return tags;

            if (depth >= MaxDepth)
            {
                s_truncated = true;
                return tags;
            }

            // A truncated inference depends on the depth it started from, so it is not cached
            var wasTruncated = s_truncated;
            s_truncated = false;
            tags = InferLocalTags(local, semanticModel, depth);
            var truncated = s_truncated;
            s_truncated = wasTruncated || truncated;
            if (truncated)
                return tags;
        }

        _localTags.TryAdd(local, tags);
        return tags;
    }

    public TagInfo GetLocalCommentTags(ILocalSymbol local)
    {
        foreach (var reference in local.DeclaringSyntaxReferences)
        {
            foreach (var trivia in GetLocalCommentTrivia(reference.GetSyntax()))
            {
                if (ValueTagComment.Parse(trivia.ToString(), out var tags) is ValueTagCommentKind.Valid)
                    return tags;
            }
        }

        // Comments in the type arguments of the declared type, e.g. Dictionary</* ValueTag=OrderId */ Guid, Guid> map
        foreach (var reference in local.DeclaringSyntaxReferences)
        {
            var typeSyntax = GetLocalDeclarationType(reference.GetSyntax());
            if (typeSyntax is not null)
            {
                var tags = TypeArgumentComments.GetTags(typeSyntax, local.Type, KnownTypes);
                if (!tags.IsEmpty)
                    return tags;
            }
        }

        return TagInfo.None;
    }

    private static TypeSyntax? GetLocalDeclarationType(SyntaxNode declaration)
    {
        return declaration switch
        {
            VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax variableDeclaration } => variableDeclaration.Type,
            ForEachStatementSyntax forEach => forEach.Type,
            SingleVariableDesignationSyntax { Parent: DeclarationExpressionSyntax declarationExpression } => declarationExpression.Type,
            SingleVariableDesignationSyntax { Parent: DeclarationPatternSyntax declarationPattern } => declarationPattern.Type,
            _ => null,
        };
    }

    /// <summary>
    /// Returns the comments that can tag the local declared by <paramref name="declaration"/>, the most specific first.
    /// </summary>
    /// <remarks>
    /// <c>/* ... */</c> comments are accepted anywhere between the start of the declaration and the name of the variable.
    /// <c>// ...</c> comments are only accepted on the lines before a statement that declares variables.
    /// </remarks>
    public static IEnumerable<SyntaxTrivia> GetLocalCommentTrivia(SyntaxNode declaration)
    {
        switch (declaration)
        {
            case VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax { Parent: (LocalDeclarationStatementSyntax or ForStatementSyntax or UsingStatementSyntax or FixedStatementSyntax) and { } statement } variableDeclaration } declarator:
                var typeArgumentTrivia = TypeArgumentComments.GetAllTrivia(variableDeclaration.Type);
                return GetTokenTrivia(declarator.Identifier)
                    .Concat(statement.DescendantTokens().TakeWhile(token => token.SpanStart < variableDeclaration.Variables[0].SpanStart).SelectMany(GetTokenTrivia).Where(trivia => !typeArgumentTrivia.Contains(trivia)))
                    .Concat(GetLineComments(statement.GetFirstToken()));

            case ForEachStatementSyntax forEach:
                var forEachTypeArgumentTrivia = TypeArgumentComments.GetAllTrivia(forEach.Type);
                return GetTokenTrivia(forEach.Identifier)
                    .Concat(forEach.Type.DescendantTokens().SelectMany(GetTokenTrivia).Where(trivia => !forEachTypeArgumentTrivia.Contains(trivia)))
                    .Concat(forEach.OpenParenToken.TrailingTrivia.Where(trivia => trivia.IsKind(SyntaxKind.MultiLineCommentTrivia)))
                    .Concat(GetLineComments(forEach.GetFirstToken()));

            case SingleVariableDesignationSyntax designation:
                var result = GetTokenTrivia(designation.Identifier);
                var type = designation.Parent switch
                {
                    DeclarationExpressionSyntax declarationExpression => declarationExpression.Type,
                    DeclarationPatternSyntax declarationPattern => declarationPattern.Type,
                    _ => null,
                };

                if (type is null)
                    return result;

                var designationTypeArgumentTrivia = TypeArgumentComments.GetAllTrivia(type);
                return result.Concat(type.DescendantTokens().SelectMany(GetTokenTrivia).Where(trivia => !designationTypeArgumentTrivia.Contains(trivia)));

            default:
                return [];
        }

        static IEnumerable<SyntaxTrivia> GetTokenTrivia(SyntaxToken token)
        {
            return token.LeadingTrivia.Concat(token.TrailingTrivia).Where(trivia => trivia.IsKind(SyntaxKind.MultiLineCommentTrivia));
        }

        static IEnumerable<SyntaxTrivia> GetLineComments(SyntaxToken firstToken)
        {
            // The closest comment first
            return firstToken.LeadingTrivia.Reverse().Where(trivia => trivia.IsKind(SyntaxKind.SingleLineCommentTrivia));
        }
    }

    private TagInfo InferLocalTags(ILocalSymbol local, SemanticModel semanticModel, int depth)
    {
        foreach (var reference in local.DeclaringSyntaxReferences)
        {
            if (reference.SyntaxTree != semanticModel.SyntaxTree)
                continue;

            var syntax = reference.GetSyntax();
            switch (syntax)
            {
                case VariableDeclaratorSyntax declarator when semanticModel.GetOperation(declarator) is IVariableDeclaratorOperation declaratorOperation:
                    return GetTag(declaratorOperation.GetVariableInitializer()?.Value, depth + 1);

                case ForEachStatementSyntax forEach when semanticModel.GetOperation(forEach) is IForEachLoopOperation forEachOperation:
                    return GetTag(forEachOperation.Collection, depth + 1);

                case SingleVariableDesignationSyntax { Parent: DeclarationExpressionSyntax declarationExpression } when semanticModel.GetOperation(declarationExpression) is { Parent: IArgumentOperation argument }:
                    return GetExpectedArgumentTags(argument, depth + 1);

                case SingleVariableDesignationSyntax designation:
                    return InferDesignationTags(local, designation, semanticModel, depth);
            }
        }

        return TagInfo.None;
    }

    /// <summary>
    /// Returns the tags of a variable declared by a pattern, e.g. <c>value is { } id</c> or <c>order is { Id: var id }</c>,
    /// or by a deconstruction, e.g. <c>var (orderId, projectId) = ...</c> or <c>foreach (var (key, value) in map)</c>.
    /// </summary>
    private TagInfo InferDesignationTags(ILocalSymbol local, SingleVariableDesignationSyntax designation, SemanticModel semanticModel, int depth)
    {
        for (var node = designation.Parent; node is not null; node = node.Parent)
        {
            switch (node)
            {
                case PatternSyntax patternSyntax:
                    if (semanticModel.GetOperation(patternSyntax) is not IPatternOperation patternOperation)
                        return TagInfo.None;

                    foreach (var pattern in patternOperation.DescendantsAndSelf().OfType<IPatternOperation>())
                    {
                        if (SymbolEqualityComparer.Default.Equals(GetDeclaredSymbol(pattern), local))
                        {
                            var input = GetPatternInput(pattern, depth + 1);
                            return input.Tags ?? GetTag(input.Value, depth + 1);
                        }
                    }

                    return TagInfo.None;

                case AssignmentExpressionSyntax assignment when semanticModel.GetOperation(assignment) is IDeconstructionAssignmentOperation deconstruction:
                    return GetDeconstructedVariableTags(deconstruction.Target, local, deconstruction.Value, tags: null, deconstruction.Value.Type, depth);

                case ForEachVariableStatementSyntax forEach when semanticModel.GetOperation(forEach) is IForEachLoopOperation { LoopControlVariable: { } loopControlVariable } loop:
                    return GetDeconstructedVariableTags(loopControlVariable, local, value: null, GetTag(loop.Collection, depth + 1), semanticModel.GetForEachStatementInfo(forEach).ElementType, depth);

                case StatementSyntax or AnonymousFunctionExpressionSyntax:
                    return TagInfo.None;
            }
        }

        return TagInfo.None;

        static ISymbol? GetDeclaredSymbol(IPatternOperation pattern)
        {
            return pattern switch
            {
                IDeclarationPatternOperation declarationPattern => declarationPattern.DeclaredSymbol,
                IRecursivePatternOperation recursivePattern => recursivePattern.DeclaredSymbol,
                IListPatternOperation listPattern => listPattern.DeclaredSymbol,
                _ => null,
            };
        }
    }

    /// <summary>
    /// Returns the tags of <paramref name="local"/> in the target of a deconstruction, e.g. <c>b</c> in <c>var (a, (b, c)) = value</c>.
    /// </summary>
    private TagInfo GetDeconstructedVariableTags(IOperation target, ILocalSymbol local, IOperation? value, TagInfo? tags, ITypeSymbol? type, int depth)
    {
        var reference = target.DescendantsAndSelf().OfType<ILocalReferenceOperation>().FirstOrDefault(reference => SymbolEqualityComparer.Default.Equals(reference.Local, local));
        if (reference is null)
            return TagInfo.None;

        var path = new List<int>();
        for (IOperation current = reference; current != target && current.Parent is { } parent; current = parent)
        {
            if (parent is ITupleOperation tuple)
            {
                path.Add(tuple.Elements.IndexOf(current));
            }
        }

        for (var i = path.Count - 1; i >= 0; i--)
        {
            (value, tags, type) = GetDeconstructedElement(value, tags, type, path[i], depth + 1);
        }

        return tags ?? GetTag(value, depth + 1);
    }

    /// <summary>
    /// Returns the element at <paramref name="index"/> of a deconstructed value: an element of a tuple literal, or the key or the value of a <c>KeyValuePair</c>.
    /// </summary>
    /// <param name="value">The deconstructed value, when it is an operation.</param>
    /// <param name="tags">The tags of the deconstructed value, or <see langword="null"/> to read them from <paramref name="value"/>.</param>
    /// <param name="type">The type of the deconstructed value.</param>
    private (IOperation? Value, TagInfo? Tags, ITypeSymbol? Type) GetDeconstructedElement(IOperation? value, TagInfo? tags, ITypeSymbol? type, int index, int depth)
    {
        if (tags is null && value?.UnwrapImplicitConversions() is ITupleOperation tuple)
            return index >= 0 && index < tuple.Elements.Length ? (tuple.Elements[index], null, tuple.Elements[index].Type) : (null, TagInfo.None, null);

        if (type is INamedTypeSymbol namedType && KnownTypes.IsKeyValuePair(namedType) && index is 0 or 1)
        {
            var keyValueTags = tags ?? GetTag(value, depth + 1);
            return (null, index is 0 ? keyValueTags.GetKey() : keyValueTags.GetValue(), namedType.TypeArguments[index]);
        }

        return (null, TagInfo.None, null);
    }

    /// <summary>
    /// Returns the value a pattern matches: the operand of <c>is</c> or <c>switch</c>, a property of a property pattern, an element of a list pattern,
    /// or an element of a positional pattern.
    /// </summary>
    /// <returns>The matched value, and its tags when they cannot be read from the value.</returns>
    public (IOperation? Value, TagInfo? Tags) GetPatternInput(IPatternOperation pattern)
    {
        return GetPatternInput(pattern, depth: 0);
    }

    private (IOperation? Value, TagInfo? Tags) GetPatternInput(IPatternOperation pattern, int depth)
    {
        if (depth > MaxDepth)
        {
            s_truncated = true;
            return (null, TagInfo.None);
        }

        switch (pattern.Parent)
        {
            case IIsPatternOperation isPattern:
                return (isPattern.Value, null);

            case ISwitchExpressionArmOperation { Parent: ISwitchExpressionOperation switchExpression }:
                return (switchExpression.Value, null);

            case IPatternCaseClauseOperation { Parent: ISwitchCaseOperation { Parent: ISwitchOperation switchStatement } }:
                return (switchStatement.Value, null);

            // not x, x and y, and the slice of a list pattern match the same value
            case INegatedPatternOperation or IBinaryPatternOperation or ISlicePatternOperation:
                return GetPatternInput((IPatternOperation)pattern.Parent, depth + 1);

            // The tags of a collection describe its elements, so an element and a slice have the tags of the collection
            case IListPatternOperation listPattern:
                return GetPatternInput(listPattern, depth + 1);

            // The member reads the pattern input, see the PatternInput instance reference in GetTag
            case IPropertySubpatternOperation propertySubpattern:
                return (propertySubpattern.Member, null);

            case IRecursivePatternOperation recursivePattern:
                var index = recursivePattern.DeconstructionSubpatterns.IndexOf(pattern);
                if (index < 0)
                    return (null, TagInfo.None);

                var input = GetPatternInput(recursivePattern, depth + 1);
                var element = GetDeconstructedElement(input.Value, input.Tags, recursivePattern.MatchedType, index, depth + 1);
                return (element.Value, element.Tags);

            default:
                return (null, TagInfo.None);
        }
    }

    public TagInfo GetTag(IOperation? operation)
    {
        return GetTag(operation, depth: 0);
    }

    /// <remarks>
    /// The tags of an operation are cached, as the tags of a call chain such as <c>ids.Select(x =&gt; x).Select(x =&gt; x)</c> read the tags
    /// of the receiver more than once. A computation that reaches <see cref="MaxDepth"/> is cached with the depth it had left, and reused only
    /// by computations that have no more depth left.
    /// </remarks>
    private TagInfo GetTag(IOperation? operation, int depth)
    {
        if (operation is null)
            return TagInfo.None;

        if (depth > MaxDepth)
        {
            s_truncated = true;
            return TagInfo.None;
        }

        var budget = MaxDepth - depth;
        var cache = _operationTags.GetValue(operation, _ => new OperationTagCache());
        if (cache.Result is { } cached && cached.Budget >= budget)
        {
            s_truncated |= cached.Budget is not int.MaxValue;
            return cached.Tags;
        }

        var wasTruncated = s_truncated;
        s_truncated = false;
        var tags = ComputeOperationTag(operation, depth);
        var truncated = s_truncated;
        s_truncated = wasTruncated || truncated;
        cache.Result = new OperationTagResult(tags, truncated ? budget : int.MaxValue);
        return tags;
    }

    private TagInfo ComputeOperationTag(IOperation operation, int depth)
    {
        switch (operation)
        {
            case IConversionOperation conversion:
                // A user-defined conversion creates a new value, tagged by the return value of the operator
                if (conversion.Conversion.IsUserDefined)
                    return conversion.Conversion.MethodSymbol is { } conversionMethod ? GetDeclaredTags(conversionMethod) : TagInfo.None;

                // (Guid)(object)value is the explicit way to drop the tag
                if (!conversion.IsImplicit && (IsObjectOrDynamic(conversion.Type) || IsObjectOrDynamic(conversion.Operand.Type)))
                    return TagInfo.None;

                return GetTag(conversion.Operand, depth + 1);

            case IAwaitOperation awaitOperation:
                return GetTag(awaitOperation.Operation, depth + 1);

            case ISimpleAssignmentOperation assignment:
                return GetTag(assignment.Value, depth + 1);

            // A user-defined operator creates a new value, tagged by the return value of the operator
            case IBinaryOperation binary when !IsBuiltInNumericOperator(binary.OperatorMethod, binary.Type):
                return binary.OperatorMethod is null ? TagInfo.None : GetDeclaredTags(binary.OperatorMethod);

            case IBinaryOperation { OperatorKind: BinaryOperatorKind.Add or BinaryOperatorKind.Subtract } additive:
                return Combine([additive.LeftOperand, additive.RightOperand], depth);

            // price * 2 is still a price, but meters / seconds is neither
            case IBinaryOperation { OperatorKind: BinaryOperatorKind.Multiply or BinaryOperatorKind.Divide } multiplicative:
                var leftTags = GetTag(multiplicative.LeftOperand, depth + 1);
                var rightTags = GetTag(multiplicative.RightOperand, depth + 1);
                return leftTags.IsEmpty ? rightTags : rightTags.IsEmpty ? leftTags : TagInfo.None;

            case IUnaryOperation unary when !IsBuiltInNumericOperator(unary.OperatorMethod, unary.Type):
                return unary.OperatorMethod is null ? TagInfo.None : GetDeclaredTags(unary.OperatorMethod);

            case IUnaryOperation { OperatorKind: UnaryOperatorKind.Plus or UnaryOperatorKind.Minus } unary:
                return GetTag(unary.Operand, depth + 1);

            case IIncrementOrDecrementOperation increment when !IsBuiltInNumericOperator(increment.OperatorMethod, increment.Type):
                return increment.OperatorMethod is null ? TagInfo.None : GetDeclaredTags(increment.OperatorMethod);

            case IIncrementOrDecrementOperation increment:
                return GetTag(increment.Target, depth + 1);

            // The value of orderId += 1 is the new value of orderId
            case ICompoundAssignmentOperation compoundAssignment:
                return GetTag(compoundAssignment.Target, depth + 1);

            case IFieldReferenceOperation fieldReference:
                return GetMemberTags(fieldReference.Field, fieldReference.Instance?.Type);

            case IPropertyReferenceOperation propertyReference:
                var propertyTags = GetAnonymousTypePropertyTags(propertyReference, depth);
                if (!propertyTags.IsEmpty)
                    return propertyTags;

                propertyTags = GetMemberTags(propertyReference.Property, propertyReference.Instance?.Type);
                if (!propertyTags.IsEmpty)
                    return propertyTags;

                return GetBoundTag(propertyReference.Property.OriginalDefinition.Type, propertyReference.Property, propertyReference.Instance, propertyReference.Arguments, depth);

            case IParameterReferenceOperation parameterReference:
                var parameterTags = GetDeclaredTags(parameterReference.Parameter);
                if (!parameterTags.IsEmpty)
                    return parameterTags;

                return GetLambdaParameterTags(parameterReference, depth);

            case ILocalReferenceOperation localReference:
                return GetLocalTags(localReference.Local, localReference.SemanticModel, depth);

            case IInvocationOperation invocation:
                var returnTags = GetDeclaredTags(invocation.TargetMethod);
                if (!returnTags.IsEmpty)
                    return returnTags;

                return GetBoundTag(invocation.TargetMethod.OriginalDefinition.ReturnType, invocation.TargetMethod, invocation.Instance, invocation.Arguments, depth);

            case IObjectCreationOperation { Constructor: not null } objectCreation:
                var creationTags = GetObjectCreationTypeArgumentTags(objectCreation);
                if (!creationTags.IsEmpty)
                    return creationTags;

                creationTags = GetBoundTag(objectCreation.Constructor.OriginalDefinition.ContainingType, objectCreation.Constructor, instance: null, objectCreation.Arguments, depth);
                if (!creationTags.IsEmpty)
                    return creationTags;

                return GetCollectionInitializerTags(objectCreation, depth);

            // The value matched by a property pattern, e.g. order in order is { Id: var id }
            case IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.PatternInput } patternInput:
                for (var parent = patternInput.Parent; parent is not null; parent = parent.Parent)
                {
                    if (parent is IRecursivePatternOperation recursivePattern)
                    {
                        var input = GetPatternInput(recursivePattern, depth + 1);
                        return input.Tags ?? GetTag(input.Value, depth + 1);
                    }
                }

                return TagInfo.None;

            // The receiver of the Add calls of a collection initializer, and of the assignments of an object initializer
            case IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ImplicitReceiver } implicitReceiver:
                for (var parent = implicitReceiver.Parent; parent is not null; parent = parent.Parent)
                {
                    switch (parent)
                    {
                        // new Order { Lines = { line } } adds to Lines
                        case IMemberInitializerOperation memberInitializer when implicitReceiver.Parent != memberInitializer.InitializedMember:
                            return GetTag(memberInitializer.InitializedMember, depth + 1);

                        case IObjectCreationOperation creation:
                            return GetObjectCreationTypeArgumentTags(creation);
                    }
                }

                return TagInfo.None;

            case IArrayElementReferenceOperation arrayElementReference:
                return GetTag(arrayElementReference.ArrayReference, depth + 1);

            case IConditionalOperation { WhenFalse: not null } conditional when conditional.Syntax is ConditionalExpressionSyntax:
                return Combine([conditional.WhenTrue, conditional.WhenFalse], depth);

            case ICoalesceOperation coalesce:
                return Combine([coalesce.Value, coalesce.WhenNull], depth);

            case ICoalesceAssignmentOperation coalesceAssignment:
                return Combine([coalesceAssignment.Target, coalesceAssignment.Value], depth);

            case ISwitchExpressionOperation switchExpression:
                return Combine(switchExpression.Arms.Select(arm => arm.Value), depth);

            case IArrayCreationOperation { Initializer: not null } arrayCreation:
                return GetTag(arrayCreation.Initializer, depth + 1);

            case IArrayInitializerOperation arrayInitializer:
                return Combine(arrayInitializer.ElementValues, depth);

            case ICollectionExpressionOperation collectionExpression:
                return Combine(collectionExpression.Elements, depth);

            case ISpreadOperation spread:
                return GetTag(spread.Operand, depth + 1);

            case IConditionalAccessOperation conditionalAccess:
                return GetTag(conditionalAccess.WhenNotNull, depth + 1);

            case IConditionalAccessInstanceOperation conditionalAccessInstance:
                IOperation child = conditionalAccessInstance;
                for (var parent = child.Parent; parent is not null; parent = parent.Parent)
                {
                    if (parent is IConditionalAccessOperation access && access.WhenNotNull == child)
                        return GetTag(access.Operation, depth + 1);

                    child = parent;
                }

                return TagInfo.None;

            default:
                return TagInfo.None;
        }
    }

    /// <summary>
    /// Returns the tags of the combined values, or <see cref="TagInfo.None"/> when two of them are incompatible.
    /// </summary>
    private TagInfo Combine(IEnumerable<IOperation> operations, int depth)
    {
        var tags = new List<TagInfo>();
        foreach (var operation in operations)
        {
            var tag = GetTag(operation, depth + 1);
            if (tag.IsEmpty)
                continue;

            foreach (var existing in tags)
            {
                if (!TagInfo.AreCompatible(existing, tag))
                    return TagInfo.None;
            }

            tags.Add(tag);
        }

        return tags.Aggregate(TagInfo.None, TagInfo.Union);
    }

    /// <summary>
    /// Returns the first pair of combined values whose tags are incompatible.
    /// </summary>
    public bool TryFindIncompatibleValues(IEnumerable<IOperation> operations, [NotNullWhen(true)] out IOperation? first, out TagInfo firstTags, [NotNullWhen(true)] out IOperation? second, out TagInfo secondTags)
    {
        var tagged = new List<(IOperation Operation, TagInfo Tags)>();
        foreach (var operation in operations)
        {
            var tag = GetTag(operation);
            if (tag.IsEmpty)
                continue;

            foreach (var existing in tagged)
            {
                if (!TagInfo.AreCompatible(existing.Tags, tag))
                {
                    first = existing.Operation;
                    firstTags = existing.Tags;
                    second = operation;
                    secondTags = tag;
                    return true;
                }
            }

            tagged.Add((operation, tag));
        }

        first = null;
        firstTags = TagInfo.None;
        second = null;
        secondTags = TagInfo.None;
        return false;
    }

    /// <summary>
    /// Returns the tags of a collection created with a collection initializer, from the tags of the elements it adds, e.g. <c>new List&lt;Guid&gt; { orderId }</c>,
    /// or of the keys and the values of a dictionary, e.g. <c>new Dictionary&lt;Guid, Guid&gt; { [orderId] = projectId }</c>.
    /// </summary>
    private TagInfo GetCollectionInitializerTags(IObjectCreationOperation objectCreation, int depth)
    {
        if (objectCreation.Initializer is null || objectCreation.Type is null || !KnownTypes.IsCollectionType(objectCreation.Type))
            return TagInfo.None;

        var elements = new List<IOperation>();
        var keys = new List<IOperation>();
        var values = new List<IOperation>();
        GetCollectionInitializerElements(objectCreation.Initializer, elements, keys, values);
        if (keys.Count is 0)
            return Combine(elements, depth);

        if (elements.Count is 0 && KnownTypes.IsKeyValueShaped(objectCreation.Type))
            return TagInfo.KeyValue(Combine(keys, depth), Combine(values, depth));

        return TagInfo.None;
    }

    /// <summary>
    /// Collects the values a collection initializer adds: the argument of <c>Add(value)</c>, and the keys and the values of <c>Add(key, value)</c> and of <c>[key] = value</c>.
    /// </summary>
    public static void GetCollectionInitializerElements(IObjectOrCollectionInitializerOperation initializer, List<IOperation> elements, List<IOperation> keys, List<IOperation> values)
    {
        foreach (var operation in initializer.Initializers)
        {
            switch (operation)
            {
                case IInvocationOperation invocation:
                    // The receiver is the first argument of an extension Add method
                    var arguments = invocation.Arguments.Where(argument => argument.Value is not IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ImplicitReceiver }).ToArray();
                    if (arguments.Length is 1)
                    {
                        elements.Add(arguments[0].Value);
                    }
                    else if (arguments.Length is 2)
                    {
                        keys.Add(arguments[0].Value);
                        values.Add(arguments[1].Value);
                    }

                    break;

                case ISimpleAssignmentOperation { Target: IPropertyReferenceOperation { Property.IsIndexer: true, Arguments.Length: 1 } indexer } assignment:
                    keys.Add(indexer.Arguments[0].Value);
                    values.Add(assignment.Value);
                    break;
            }
        }
    }

    /// <summary>
    /// Returns the tags declared for the collection a collection initializer adds to: the comments in the type arguments of the <c>new</c> expression,
    /// or the tags of the member a nested initializer adds to, e.g. <c>Lines</c> in <c>new Order { Lines = { line } }</c>.
    /// </summary>
    public TagInfo GetCollectionInitializerTargetTags(IObjectOrCollectionInitializerOperation initializer)
    {
        return initializer.Parent switch
        {
            IObjectCreationOperation objectCreation => GetObjectCreationTypeArgumentTags(objectCreation),
            IMemberInitializerOperation memberInitializer => GetTag(memberInitializer.InitializedMember),
            _ => TagInfo.None,
        };
    }

    private TagInfo GetObjectCreationTypeArgumentTags(IObjectCreationOperation objectCreation)
    {
        return objectCreation.Syntax is ObjectCreationExpressionSyntax objectCreationSyntax
            ? TypeArgumentComments.GetTags(objectCreationSyntax.Type, objectCreation.Type, KnownTypes)
            : TagInfo.None;
    }

    private TagInfo GetAnonymousTypePropertyTags(IPropertyReferenceOperation propertyReference, int depth)
    {
        var property = propertyReference.Property;
        if (!property.ContainingType.IsAnonymousType || propertyReference.SemanticModel is not { } semanticModel)
            return TagInfo.None;

        // Anonymous types are shared by every creation with the same shape, so every initializer in this file must agree
        var initializers = new List<IOperation>();
        foreach (var reference in property.DeclaringSyntaxReferences)
        {
            if (reference.SyntaxTree != semanticModel.SyntaxTree)
                continue;

            if (reference.GetSyntax() is AnonymousObjectMemberDeclaratorSyntax declarator && semanticModel.GetOperation(declarator.Expression) is { } initializer)
            {
                initializers.Add(initializer);
            }
        }

        return Combine(initializers, depth);
    }

    private TagInfo GetLambdaParameterTags(IParameterReferenceOperation parameterReference, int depth)
    {
        var parameter = parameterReference.Parameter;
        if (parameter.ContainingSymbol is not IMethodSymbol { MethodKind: MethodKind.AnonymousFunction } lambdaSymbol)
            return TagInfo.None;

        for (var operation = parameterReference.Parent; operation is not null; operation = operation.Parent)
        {
            if (operation is IAnonymousFunctionOperation anonymousFunction && SymbolEqualityComparer.Default.Equals(anonymousFunction.Symbol, lambdaSymbol))
                return GetAnonymousFunctionParameterTags(anonymousFunction, parameter.Ordinal, depth);
        }

        return TagInfo.None;
    }

    private TagInfo GetAnonymousFunctionParameterTags(IAnonymousFunctionOperation anonymousFunction, int ordinal, int depth)
    {
        var current = anonymousFunction.Parent;
        while (current is IDelegateCreationOperation or IConversionOperation)
        {
            current = current.Parent;
        }

        if (current is not IArgumentOperation { Parameter: not null } argument || !TryGetArgumentOwner(argument, out var member, out var instance, out var arguments))
            return TagInfo.None;

        var delegateType = KnownTypes.GetDelegateType(argument.Parameter.OriginalDefinition.Type);
        if (delegateType?.DelegateInvokeMethod is not { } invokeMethod || ordinal >= invokeMethod.Parameters.Length)
            return TagInfo.None;

        var parameterType = invokeMethod.Parameters[ordinal].Type;
        if (!ContainsTypeParameter(parameterType))
            return TagInfo.None;

        var typeParameters = BindTypeParameters(member, instance, arguments, arguments.IndexOf(argument), depth);
        return ComputeTag(parameterType, typeParameters);
    }

    private TagInfo GetAnonymousFunctionReturnTags(IAnonymousFunctionOperation anonymousFunction, int depth)
    {
        var explicitTags = GetOwnExplicitTags(anonymousFunction.Symbol);
        if (!explicitTags.IsEmpty)
            return explicitTags;

        var returnedValues = new List<IOperation>();
        CollectReturnedValues(anonymousFunction.Body, returnedValues);
        return Combine(returnedValues, depth);
    }

    /// <summary>
    /// Collects the values of the <c>return</c> and <c>yield return</c> statements of a body, ignoring nested lambdas and local functions.
    /// </summary>
    public static void CollectReturnedValues(IOperation operation, List<IOperation> returnedValues)
    {
        foreach (var child in operation.ChildOperations)
        {
            switch (child)
            {
                case IAnonymousFunctionOperation or ILocalFunctionOperation:
                    continue;

                case IReturnOperation { ReturnedValue: not null } returnOperation:
                    returnedValues.Add(returnOperation.ReturnedValue);
                    break;
            }

            CollectReturnedValues(child, returnedValues);
        }
    }

    /// <summary>
    /// Returns the tags expected for an argument: the tags of its parameter, or the tags bound to the generic type of the parameter
    /// by the receiver and the previous arguments.
    /// </summary>
    public TagInfo GetExpectedArgumentTags(IArgumentOperation argument)
    {
        return GetExpectedArgumentTags(argument, depth: 0);
    }

    private TagInfo GetExpectedArgumentTags(IArgumentOperation argument, int depth)
    {
        if (argument.Parameter is null)
            return TagInfo.None;

        var tags = GetDeclaredTags(argument.Parameter);
        if (!tags.IsEmpty)
            return tags;

        var parameterType = argument.Parameter.OriginalDefinition.Type;
        if (!ContainsTypeParameter(parameterType) || !TryGetArgumentOwner(argument, out var member, out var instance, out var arguments))
            return TagInfo.None;

        var typeParameters = BindTypeParameters(member, instance, arguments, arguments.IndexOf(argument), depth);
        return ComputeTag(parameterType, typeParameters);
    }

    public static bool TryGetArgumentOwner(IArgumentOperation argument, [NotNullWhen(true)] out ISymbol? member, out IOperation? instance, out ImmutableArray<IArgumentOperation> arguments)
    {
        switch (argument.Parent)
        {
            case IInvocationOperation invocation:
                (member, instance, arguments) = (invocation.TargetMethod, invocation.Instance, invocation.Arguments);
                return true;

            case IObjectCreationOperation { Constructor: not null } objectCreation:
                (member, instance, arguments) = (objectCreation.Constructor, null, objectCreation.Arguments);
                return true;

            case IPropertyReferenceOperation propertyReference:
                (member, instance, arguments) = (propertyReference.Property, propertyReference.Instance, propertyReference.Arguments);
                return true;

            default:
                member = null;
                instance = null;
                arguments = [];
                return false;
        }
    }

    private TagInfo GetBoundTag(ITypeSymbol resultType, ISymbol member, IOperation? instance, ImmutableArray<IArgumentOperation> arguments, int depth)
    {
        if (!ContainsTypeParameter(resultType))
            return TagInfo.None;

        var typeParameters = BindTypeParameters(member, instance, arguments, arguments.Length, depth);
        return ComputeTag(resultType, typeParameters);
    }

    /// <summary>
    /// Binds the type parameters of <paramref name="member"/> and of its containing types to the tags of the receiver and of the first
    /// <paramref name="argumentCount"/> arguments. The first binding of a type parameter wins.
    /// </summary>
    private Dictionary<ITypeParameterSymbol, TagInfo>? BindTypeParameters(ISymbol member, IOperation? instance, ImmutableArray<IArgumentOperation> arguments, int argumentCount, int depth)
    {
        if (depth > MaxDepth)
        {
            s_truncated = true;
            return null;
        }

        Dictionary<ITypeParameterSymbol, TagInfo>? typeParameters = null;
        var definition = member.OriginalDefinition;
        if (instance is not null && !member.IsStatic && definition.ContainingType is { } containingType && ContainsTypeParameter(containingType))
        {
            Bind(containingType, GetTag(instance, depth + 1), ref typeParameters);
        }

        // new List</* ValueTag=OrderId */ Guid>(values) expects values tagged OrderId
        if (arguments.Length > 0 && arguments[0].Parent is IObjectCreationOperation objectCreation && definition.ContainingType is { } createdType)
        {
            Bind(createdType, GetObjectCreationTypeArgumentTags(objectCreation), ref typeParameters);
        }

        for (var i = 0; i < argumentCount && i < arguments.Length; i++)
        {
            var argument = arguments[i];
            var parameter = argument.Parameter?.OriginalDefinition;
            if (parameter is null || !ContainsTypeParameter(parameter.Type))
                continue;

            var value = argument.Value;
            while (value is IConversionOperation { IsImplicit: true } or IDelegateCreationOperation)
            {
                value = value is IConversionOperation conversion ? conversion.Operand : ((IDelegateCreationOperation)value).Target;
            }

            switch (value)
            {
                case IAnonymousFunctionOperation anonymousFunction:
                    // The returned values only matter when they bind a type parameter, e.g. Select(x => x.Id) but not Where(x => x.IsValid)
                    if (KnownTypes.GetDelegateType(parameter.Type)?.DelegateInvokeMethod is { } invokeMethod && ContainsTypeParameter(invokeMethod.ReturnType))
                    {
                        Bind(invokeMethod.ReturnType, GetAnonymousFunctionReturnTags(anonymousFunction, depth + 1), ref typeParameters);
                    }

                    break;

                case IMethodReferenceOperation methodReference:
                    if (KnownTypes.GetDelegateType(parameter.Type)?.DelegateInvokeMethod is { } methodReferenceInvokeMethod)
                    {
                        Bind(methodReferenceInvokeMethod.ReturnType, GetDeclaredTags(methodReference.Method), ref typeParameters);
                    }

                    break;

                default:
                    Bind(parameter.Type, GetTag(argument.Value, depth + 1), ref typeParameters);
                    break;
            }
        }

        return typeParameters;
    }

    private void Bind(ITypeSymbol type, TagInfo tags, ref Dictionary<ITypeParameterSymbol, TagInfo>? typeParameters)
    {
        if (tags.IsEmpty)
            return;

        switch (type)
        {
            case ITypeParameterSymbol typeParameter:
                typeParameters ??= new(SymbolEqualityComparer.Default);
                if (!typeParameters.ContainsKey(typeParameter))
                {
                    typeParameters.Add(typeParameter, tags);
                }

                break;

            case IArrayTypeSymbol arrayType:
                Bind(arrayType.ElementType, tags, ref typeParameters);
                break;

            case INamedTypeSymbol namedType when KnownTypes.IsKeyValuePair(namedType):
                Bind(namedType.TypeArguments[0], tags.GetKey(), ref typeParameters);
                Bind(namedType.TypeArguments[1], tags.GetValue(), ref typeParameters);
                break;

            case INamedTypeSymbol namedType when KnownTypes.TryGetWrappedType(namedType, out var wrappedType):
                Bind(wrappedType, tags, ref typeParameters);
                break;
        }
    }

    private TagInfo ComputeTag(ITypeSymbol type, Dictionary<ITypeParameterSymbol, TagInfo>? typeParameters)
    {
        if (typeParameters is null)
            return TagInfo.None;

        switch (type)
        {
            case ITypeParameterSymbol typeParameter:
                return typeParameters.TryGetValue(typeParameter, out var tags) ? tags : TagInfo.None;

            case IArrayTypeSymbol arrayType:
                return ComputeTag(arrayType.ElementType, typeParameters);

            case INamedTypeSymbol namedType when KnownTypes.IsKeyValuePair(namedType):
                return TagInfo.KeyValue(ComputeTag(namedType.TypeArguments[0], typeParameters), ComputeTag(namedType.TypeArguments[1], typeParameters));

            case INamedTypeSymbol namedType when KnownTypes.TryGetWrappedType(namedType, out var wrappedType):
                return ComputeTag(wrappedType, typeParameters);

            default:
                return TagInfo.None;
        }
    }

    public static bool ContainsTypeParameter(ITypeSymbol? type)
    {
        switch (type)
        {
            case ITypeParameterSymbol:
                return true;

            case IArrayTypeSymbol arrayType:
                return ContainsTypeParameter(arrayType.ElementType);

            case INamedTypeSymbol namedType:
                foreach (var typeArgument in namedType.TypeArguments)
                {
                    if (ContainsTypeParameter(typeArgument))
                        return true;
                }

                return ContainsTypeParameter(namedType.ContainingType);

            default:
                return false;
        }
    }

    /// <summary>
    /// Returns whether an operator is a built-in arithmetic operator on numbers, which keeps the tag of its operands.
    /// Other operators, such as <c>DateTime - DateTime</c>, create a value of another kind.
    /// </summary>
    public static bool IsBuiltInNumericOperator(IMethodSymbol? operatorMethod, ITypeSymbol? resultType)
    {
        if (resultType is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullableType)
        {
            resultType = nullableType.TypeArguments[0];
        }

        if (resultType?.SpecialType is not (SpecialType.System_SByte or SpecialType.System_Byte or SpecialType.System_Int16 or SpecialType.System_UInt16 or
            SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_UInt64 or
            SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal or SpecialType.System_IntPtr or SpecialType.System_UIntPtr))
        {
            return false;
        }

        // decimal operators are exposed as methods of System.Decimal
        return operatorMethod is null || operatorMethod.ContainingType.SpecialType == resultType.SpecialType;
    }

    private static bool IsObjectOrDynamic(ITypeSymbol? type)
    {
        return type is { SpecialType: SpecialType.System_Object } or { TypeKind: TypeKind.Dynamic };
    }

    /// <summary>
    /// The tags declared by <c>[assembly: ValueTag(typeof(Type), "Member", "Tag")]</c> attributes.
    /// </summary>
    private sealed class ExternalTags
    {
        public Dictionary<(ITypeSymbol Type, string MemberName), TagInfo> Tags { get; } = new(ExternalTagKeyComparer.Instance);

        public HashSet<string> MemberNames { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>
    /// The tags of an operation, and whether they are complete.
    /// </summary>
    /// <param name="Tags">The tags of the operation.</param>
    /// <param name="Budget">The depth that was left when the tags were computed, or <see cref="int.MaxValue"/> when the computation did not reach <see cref="MaxDepth"/>.</param>
    private sealed record OperationTagResult(TagInfo Tags, int Budget);

    private sealed class OperationTagCache
    {
        // Replaced as a whole, so a concurrent reader sees either result. A stale read only computes the tags again.
        public OperationTagResult? Result { get; set; }
    }

    private sealed class ExternalTagKeyComparer : IEqualityComparer<(ITypeSymbol Type, string MemberName)>
    {
        public static readonly ExternalTagKeyComparer Instance = new();

        public bool Equals((ITypeSymbol Type, string MemberName) x, (ITypeSymbol Type, string MemberName) y)
        {
            return SymbolEqualityComparer.Default.Equals(x.Type, y.Type) && string.Equals(x.MemberName, y.MemberName, StringComparison.Ordinal);
        }

        public int GetHashCode((ITypeSymbol Type, string MemberName) obj)
        {
            return (SymbolEqualityComparer.Default.GetHashCode(obj.Type) * 397) ^ StringComparer.Ordinal.GetHashCode(obj.MemberName);
        }
    }
}
