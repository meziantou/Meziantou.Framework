using System.Collections.Concurrent;
using System.Collections.Immutable;
using Meziantou.Framework.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.TaggedValues;

/// <summary>
/// Reports values with different <c>[ValueTag]</c> tags that are compared, assigned, passed, returned, or combined.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ValueTagAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor ComparedValues = new(
        id: ValueTagDiagnostics.ComparedValuesDiagnosticId,
        title: "Do not compare values with different tags",
        messageFormat: "{0} is {1} and is compared with {2}, which is {3}",
        category: "TaggedValues",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Two values with different tags are compared, for instance an order id with a project id, which is almost always a bug. Compare the value with a value that has the same tag, or fix the [ValueTag] of the declaration that is tagged incorrectly.");

    public static readonly DiagnosticDescriptor FlowMismatch = new(
        id: ValueTagDiagnostics.FlowMismatchDiagnosticId,
        title: "Use a value with the tag the target expects",
        messageFormat: "{0} is {1} and flows to {2}, which is {3}",
        category: "TaggedValues",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A value is assigned, passed, or returned where a value with a different tag is expected. Use a value with the expected tag, or change the [ValueTag] of the target declaration when the target is the one tagged incorrectly.");

    public static readonly DiagnosticDescriptor CombinedValues = new(
        id: ValueTagDiagnostics.CombinedValuesDiagnosticId,
        title: "Combine only values with the same tag",
        messageFormat: "{0} is {1} but {2} is {3}",
        category: "TaggedValues",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The branches of a conditional expression, a null-coalescing expression, or a switch expression, or the elements of a collection, have different tags. Make every branch or element produce a value with the same tag, or fix the [ValueTag] of the declaration that is tagged incorrectly.");

    public static readonly DiagnosticDescriptor InheritedTagMismatch = new(
        id: ValueTagDiagnostics.InheritedTagMismatchDiagnosticId,
        title: "Use the tags of the overridden or implemented member",
        messageFormat: "{0} is {1} but {2} is {3}",
        category: "TaggedValues",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "An override or an interface implementation must accept and return values with the same tags as the member it overrides or implements. Change its [ValueTag] to the tags of the base member, or remove the attribute to inherit them.");

    public static readonly DiagnosticDescriptor InvalidAnnotation = new(
        id: ValueTagDiagnostics.InvalidAnnotationDiagnosticId,
        title: "Fix or remove the invalid value tag annotation",
        messageFormat: "{0}",
        category: "TaggedValues",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The [ValueTag] attribute or the value tag comment cannot be applied, so the value is not tagged. Fix the annotation as the message explains, or remove it.");

    public static readonly DiagnosticDescriptor AmbiguousConvention = new(
        id: ValueTagDiagnostics.AmbiguousConventionDiagnosticId,
        title: "Add an explicit tag to disambiguate the conventional tag",
        messageFormat: "{0} and {1} both infer the tag '{2}' from their type name; add an explicit [ValueTag] with a distinct tag to at least one of them",
        category: "TaggedValues",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Several types with the same name declare an 'Id' member, so the naming convention gives their ids the same tag. Add an explicit [ValueTag] with a distinct tag to at least one of them.",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    public static readonly DiagnosticDescriptor RedundantTag = new(
        id: ValueTagDiagnostics.RedundantTagDiagnosticId,
        title: "Remove the redundant value tag",
        messageFormat: "{0} on {1} is redundant: the naming convention already infers it",
        category: "TaggedValues",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "The [ValueTag] attribute declares the tag the naming convention already infers. Remove the attribute.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
    [
        ComparedValues,
        FlowMismatch,
        CombinedValues,
        InheritedTagMismatch,
        InvalidAnnotation,
        AmbiguousConvention,
        RedundantTag,
    ];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(context =>
        {
            if (!TagResolver.HasValueTagAttributeType(context.Compilation))
                return;

            var resolver = new TagResolver(context.Compilation, context.Options.AnalyzerConfigOptionsProvider);
            var conventionIdMembers = new ConcurrentBag<ISymbol>();

            context.RegisterOperationAction(context => AnalyzeBinary(context, resolver), OperationKind.Binary);
            context.RegisterOperationAction(context => AnalyzeTupleBinary(context, resolver), OperationKind.TupleBinary);
            context.RegisterOperationAction(context => AnalyzeInvocation(context, resolver), OperationKind.Invocation);
            context.RegisterOperationAction(context => AnalyzeArgument(context, resolver), OperationKind.Argument);
            context.RegisterOperationAction(context => AnalyzeAssignment(context, resolver), OperationKind.SimpleAssignment, OperationKind.CoalesceAssignment);
            context.RegisterOperationAction(context => AnalyzeVariableDeclarator(context, resolver), OperationKind.VariableDeclarator);
            context.RegisterOperationAction(context => AnalyzeMemberInitializer(context, resolver), OperationKind.FieldInitializer, OperationKind.PropertyInitializer);
            context.RegisterOperationAction(context => AnalyzeReturn(context, resolver), OperationKind.Return, OperationKind.YieldReturn);
            context.RegisterOperationAction(context => AnalyzeCombinedValues(context, resolver), OperationKind.Conditional, OperationKind.Coalesce, OperationKind.SwitchExpression, OperationKind.ArrayInitializer, OperationKind.CollectionExpression);
            context.RegisterOperationAction(AnalyzeAttribute, OperationKind.Attribute);
            context.RegisterSemanticModelAction(AnalyzeComments);
            context.RegisterSymbolAction(context => AnalyzeSymbol(context, resolver, conventionIdMembers), SymbolKind.Method, SymbolKind.Property, SymbolKind.Field);
            context.RegisterCompilationEndAction(context => ReportAmbiguousConventions(context, resolver, conventionIdMembers));
        });
    }

    private static void AnalyzeBinary(OperationAnalysisContext context, TagResolver resolver)
    {
        var operation = (IBinaryOperation)context.Operation;
        if (operation.OperatorKind is not (BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals or BinaryOperatorKind.LessThan or BinaryOperatorKind.LessThanOrEqual or BinaryOperatorKind.GreaterThan or BinaryOperatorKind.GreaterThanOrEqual))
            return;

        ReportComparison(context, resolver, operation.Syntax, operation.LeftOperand, operation.RightOperand);
    }

    private static void AnalyzeTupleBinary(OperationAnalysisContext context, TagResolver resolver)
    {
        var operation = (ITupleBinaryOperation)context.Operation;
        CompareTupleElements(context, resolver, operation.LeftOperand, operation.RightOperand);

        static void CompareTupleElements(OperationAnalysisContext context, TagResolver resolver, IOperation left, IOperation right)
        {
            left = left.UnwrapImplicitConversions();
            right = right.UnwrapImplicitConversions();
            if (left is ITupleOperation leftTuple && right is ITupleOperation rightTuple && leftTuple.Elements.Length == rightTuple.Elements.Length)
            {
                for (var i = 0; i < leftTuple.Elements.Length; i++)
                {
                    CompareTupleElements(context, resolver, leftTuple.Elements[i], rightTuple.Elements[i]);
                }

                return;
            }

            if (left.Parent is ITupleOperation || right.Parent is ITupleOperation)
            {
                ReportComparison(context, resolver, left.Syntax, left, right);
            }
        }
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context, TagResolver resolver)
    {
        var operation = (IInvocationOperation)context.Operation;
        if (TryGetComparedOperands(operation, out var left, out var right))
        {
            ReportComparison(context, resolver, operation.Syntax, left, right);
        }
    }

    private static void ReportComparison(OperationAnalysisContext context, TagResolver resolver, SyntaxNode reportNode, IOperation left, IOperation right)
    {
        var leftTags = resolver.GetTag(left);
        if (leftTags.IsEmpty)
            return;

        var rightTags = resolver.GetTag(right);
        if (rightTags.IsEmpty || TagInfo.AreCompatible(leftTags, rightTags))
            return;

        context.ReportDiagnostic(ComparedValues, reportNode, ValueTagDescriptions.Describe(left), leftTags.ToAttributeString(), ValueTagDescriptions.Describe(right), rightTags.ToAttributeString());
    }

    /// <summary>
    /// Recognizes <c>a.Equals(b)</c>, <c>a.CompareTo(b)</c>, <c>object.Equals(a, b)</c>, <c>comparer.Equals(a, b)</c>, <c>comparer.Compare(a, b)</c>,
    /// <c>string.Equals(a, b, comparison)</c>, and similar calls.
    /// </summary>
    private static bool TryGetComparedOperands(IInvocationOperation invocation, out IOperation left, out IOperation right)
    {
        (left, right) = (null!, null!);
        var method = invocation.TargetMethod;
        if (method.Name is not ("Equals" or "ReferenceEquals" or "Compare" or "CompareTo") || method.ReturnType.SpecialType is not (SpecialType.System_Boolean or SpecialType.System_Int32))
            return false;

        var arguments = invocation.Arguments;
        if (arguments.Length >= 2 && (method.IsStatic || invocation.Instance is not null) &&
            arguments[0].Parameter is { } firstParameter && arguments[1].Parameter is { } secondParameter &&
            SymbolEqualityComparer.Default.Equals(firstParameter.OriginalDefinition.Type, secondParameter.OriginalDefinition.Type))
        {
            (left, right) = (arguments[0].Value, arguments[1].Value);
            return true;
        }

        if (arguments.Length is 1 && invocation.Instance is not null && !method.IsStatic)
        {
            (left, right) = (invocation.Instance, arguments[0].Value);
            return true;
        }

        return false;
    }

    private static void AnalyzeArgument(OperationAnalysisContext context, TagResolver resolver)
    {
        var argument = (IArgumentOperation)context.Operation;
        if (argument.Parameter is null || argument.ArgumentKind is ArgumentKind.DefaultValue)
            return;

        // Reported as a comparison
        if (argument.Parent is IInvocationOperation invocation && TryGetComparedOperands(invocation, out _, out _))
            return;

        var value = argument.Value.UnwrapImplicitConversions();
        if (value is IDeclarationExpressionOperation or IDelegateCreationOperation or IAnonymousFunctionOperation)
            return;

        var expected = resolver.GetExpectedArgumentTags(argument);
        if (expected.IsEmpty)
            return;

        var parameter = argument.Parameter.OriginalDefinition;
        var isDeclared = !resolver.GetDeclaredTags(parameter).IsEmpty;
        ReportFlow(context, resolver, argument.Value, parameter, expected, isDeclared ? ValueTagTargetKind.Symbol : null);
    }

    private static void AnalyzeAssignment(OperationAnalysisContext context, TagResolver resolver)
    {
        var (target, value) = context.Operation switch
        {
            ISimpleAssignmentOperation assignment => (assignment.Target, assignment.Value),
            ICoalesceAssignmentOperation coalesceAssignment => (coalesceAssignment.Target, coalesceAssignment.Value),
            _ => default,
        };

        if (value is null || target is null or IDeclarationExpressionOperation or ITupleOperation or IPropertyReferenceOperation { Property.ContainingType.IsAnonymousType: true })
            return;

        var expected = resolver.GetTag(target);
        if (expected.IsEmpty)
            return;

        var (symbol, kind) = target switch
        {
            IFieldReferenceOperation fieldReference => (fieldReference.Field, ValueTagTargetKind.Symbol),
            IPropertyReferenceOperation propertyReference => (propertyReference.Property, ValueTagTargetKind.Symbol),
            IParameterReferenceOperation parameterReference => (parameterReference.Parameter, ValueTagTargetKind.Symbol),
            ILocalReferenceOperation localReference => (localReference.Local, TagResolver.GetLocalCommentTags(localReference.Local).IsEmpty ? null : ValueTagTargetKind.Local),
            _ => ((ISymbol?)null, (string?)null),
        };

        ReportFlow(context, resolver, value, symbol, expected, kind, targetDescription: symbol is null ? ValueTagDescriptions.Describe(target) : null);
    }

    private static void AnalyzeVariableDeclarator(OperationAnalysisContext context, TagResolver resolver)
    {
        var operation = (IVariableDeclaratorOperation)context.Operation;
        var initializer = operation.GetVariableInitializer();
        if (initializer is null)
            return;

        var expected = TagResolver.GetLocalCommentTags(operation.Symbol);
        if (expected.IsEmpty)
            return;

        ReportFlow(context, resolver, initializer.Value, operation.Symbol, expected, ValueTagTargetKind.Local);
    }

    private static void AnalyzeMemberInitializer(OperationAnalysisContext context, TagResolver resolver)
    {
        switch (context.Operation)
        {
            case IFieldInitializerOperation fieldInitializer:
                foreach (var field in fieldInitializer.InitializedFields)
                {
                    ReportFlow(context, resolver, fieldInitializer.Value, field, resolver.GetDeclaredTags(field), ValueTagTargetKind.Symbol);
                }

                break;

            case IPropertyInitializerOperation propertyInitializer:
                foreach (var property in propertyInitializer.InitializedProperties)
                {
                    ReportFlow(context, resolver, propertyInitializer.Value, property, resolver.GetDeclaredTags(property), ValueTagTargetKind.Symbol);
                }

                break;
        }
    }

    private static void AnalyzeReturn(OperationAnalysisContext context, TagResolver resolver)
    {
        var operation = (IReturnOperation)context.Operation;
        if (operation.ReturnedValue is null)
            return;

        ISymbol? owner = null;
        for (var parent = operation.Parent; parent is not null && owner is null; parent = parent.Parent)
        {
            owner = parent switch
            {
                IAnonymousFunctionOperation anonymousFunction => anonymousFunction.Symbol,
                ILocalFunctionOperation localFunction => localFunction.Symbol,
                _ => null,
            };
        }

        owner ??= context.ContainingSymbol;
        switch (owner)
        {
            case IMethodSymbol { MethodKind: MethodKind.PropertyGet, AssociatedSymbol: IPropertySymbol property }:
                ReportFlow(context, resolver, operation.ReturnedValue, property, resolver.GetDeclaredTags(property), ValueTagTargetKind.Symbol);
                break;

            case IMethodSymbol { MethodKind: MethodKind.AnonymousFunction } lambda:
                ReportFlow(context, resolver, operation.ReturnedValue, lambda, TagResolver.GetOwnExplicitTags(lambda), targetKind: null);
                break;

            case IMethodSymbol method:
                ReportFlow(context, resolver, operation.ReturnedValue, method, resolver.GetDeclaredTags(method), ValueTagTargetKind.ReturnValue);
                break;
        }
    }

    private static void ReportFlow(OperationAnalysisContext context, TagResolver resolver, IOperation value, ISymbol? target, TagInfo expected, string? targetKind, string? targetDescription = null)
    {
        if (expected.IsEmpty)
            return;

        var source = resolver.GetTag(value);
        if (source.IsEmpty || TagInfo.AreCompatible(source, expected))
            return;

        var reportTree = value.Syntax.SyntaxTree;
        targetDescription ??= ValueTagDescriptions.DescribeSymbol(target!) + ValueTagDescriptions.GetSite(target!, reportTree);

        var properties = ImmutableDictionary<string, string?>.Empty;
        var additionalLocations = new List<Location>();
        if (targetKind is not null && source.IsExplicit && target is not null && !target.IsImplicitlyDeclared && target.Locations.FirstOrDefault(location => location.IsInSource) is { } targetLocation)
        {
            properties = properties
                .Add(ValueTagDiagnostics.TagsProperty, source.Serialize())
                .Add(ValueTagDiagnostics.TargetKindProperty, targetKind);
            additionalLocations.Add(targetLocation);
        }

        context.ReportDiagnostic(Diagnostic.Create(
            FlowMismatch,
            value.Syntax.GetLocation(),
            additionalLocations,
            properties,
            ValueTagDescriptions.Describe(value),
            source.ToAttributeString(),
            targetDescription,
            expected.ToAttributeString()));
    }

    private static void AnalyzeCombinedValues(OperationAnalysisContext context, TagResolver resolver)
    {
        IEnumerable<IOperation>? values = context.Operation switch
        {
            IConditionalOperation { WhenFalse: not null } conditional when conditional.Syntax is ConditionalExpressionSyntax => [conditional.WhenTrue, conditional.WhenFalse],
            ICoalesceOperation coalesce => [coalesce.Value, coalesce.WhenNull],
            ISwitchExpressionOperation switchExpression => switchExpression.Arms.Select(arm => arm.Value),
            IArrayInitializerOperation arrayInitializer => arrayInitializer.ElementValues,
            ICollectionExpressionOperation collectionExpression => collectionExpression.Elements,
            _ => null,
        };

        if (values is null || !resolver.TryFindIncompatibleValues(values, out var first, out var firstTags, out var second, out var secondTags))
            return;

        context.ReportDiagnostic(CombinedValues, second.Syntax, ValueTagDescriptions.Describe(second), secondTags.ToAttributeString(), ValueTagDescriptions.Describe(first), firstTags.ToAttributeString());
    }

    private static void AnalyzeAttribute(OperationAnalysisContext context)
    {
        var operation = (IAttributeOperation)context.Operation;
        if (operation.Operation is not IObjectCreationOperation { Constructor: { } constructor } creation || !TagResolver.IsValueTagAttribute(constructor.ContainingType))
            return;

        if (operation.Syntax is not AttributeSyntax { Parent: AttributeListSyntax attributeList } attributeSyntax)
            return;

        var isAssemblyTarget = attributeList.Target?.Identifier.Kind() is SyntaxKind.AssemblyKeyword or SyntaxKind.ModuleKeyword;
        var isExternalForm = constructor.Parameters.Length is 3;

        var tags = new List<string?>();
        if (creation.Arguments.Length > 0 && creation.Arguments[creation.Arguments.Length - 1].Value is IArrayCreationOperation { Initializer: { } initializer })
        {
            foreach (var element in initializer.ElementValues)
            {
                tags.Add(element.ConstantValue.HasValue ? element.ConstantValue.Value as string : null);
            }
        }

        var hasKeyOrValue = false;
        var hasEmptyKeyOrValue = false;
        if (creation.Initializer is not null)
        {
            foreach (var initializerOperation in creation.Initializer.Initializers)
            {
                if (initializerOperation is ISimpleAssignmentOperation { Target: IPropertyReferenceOperation { Property.Name: "Key" or "Value" } } namedArgument)
                {
                    hasKeyOrValue = true;
                    hasEmptyKeyOrValue |= string.IsNullOrWhiteSpace(namedArgument.Value.ConstantValue.Value as string);
                }
            }
        }

        if (isAssemblyTarget && !isExternalForm)
        {
            ReportInvalidAttribute("[assembly: ValueTag] must name the member it tags, e.g. [assembly: ValueTag(typeof(Type), nameof(Type.Member), \"Tag\")]", removable: true);
            return;
        }

        if (!isAssemblyTarget && isExternalForm)
        {
            ReportInvalidAttribute("ValueTag(typeof(...), \"Member\", ...) only applies to the assembly; use [ValueTag(\"Tag\")] to tag this declaration", removable: true);
            return;
        }

        if (tags.Any(string.IsNullOrWhiteSpace) || hasEmptyKeyOrValue)
        {
            ReportInvalidAttribute("[ValueTag] has an empty tag; supply a non-empty tag, e.g. [ValueTag(\"OrderId\")]", removable: true);
            return;
        }

        if (tags.Count is 0 && !hasKeyOrValue)
        {
            ReportInvalidAttribute("[ValueTag] declares no tag; supply a tag, e.g. [ValueTag(\"OrderId\")]", removable: true);
            return;
        }

        if (isExternalForm)
        {
            if (creation.Arguments[0].Value is ITypeOfOperation { TypeOperand: { } type } && creation.Arguments[1].Value.ConstantValue.Value is string memberName && !HasPropertyOrField(type, memberName))
            {
                ReportInvalidAttribute("[assembly: ValueTag] names no property or field '" + memberName + "' of '" + type.ToDisplayString() + "' or its base types; use nameof to reference an existing member", removable: false);
            }

            if (hasKeyOrValue)
            {
                ReportInvalidAttribute("[assembly: ValueTag] does not support Key and Value", removable: false);
            }

            return;
        }

        if (tags.Count > 0 && hasKeyOrValue)
        {
            ReportInvalidAttribute("[ValueTag] cannot combine tags with Key and Value; use Key and Value for a dictionary, and tags otherwise", removable: false);
            return;
        }

        var annotatedType = GetAnnotatedType(attributeList, operation.SemanticModel, context.CancellationToken);
        if (annotatedType is null)
            return;

        var isKeyValueShaped = TagResolver.IsKeyValueShaped(annotatedType);
        if (hasKeyOrValue && !isKeyValueShaped)
        {
            ReportInvalidAttribute("Key and Value only apply to dictionaries, and '" + annotatedType.ToDisplayString() + "' is not a dictionary; use [ValueTag(\"Tag\")] instead", removable: true);
        }
        else if (tags.Count > 0 && isKeyValueShaped)
        {
            ReportInvalidAttribute("'" + annotatedType.ToDisplayString() + "' is a dictionary; use [ValueTag(Key = \"Tag\", Value = \"Tag\")] to tag its keys and values", removable: false);
        }

        void ReportInvalidAttribute(string message, bool removable)
        {
            var properties = removable ? ImmutableDictionary<string, string?>.Empty.Add(ValueTagDiagnostics.RemovableProperty, "true") : ImmutableDictionary<string, string?>.Empty;
            context.ReportDiagnostic(Diagnostic.Create(InvalidAnnotation, attributeSyntax.GetLocation(), properties, message));
        }
    }

    private static bool HasPropertyOrField(ITypeSymbol type, string memberName)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.GetMembers(memberName).Any(member => member is IPropertySymbol or IFieldSymbol))
                return true;
        }

        return type.AllInterfaces.Any(@interface => @interface.GetMembers(memberName).Any(member => member is IPropertySymbol));
    }

    private static ITypeSymbol? GetAnnotatedType(AttributeListSyntax attributeList, SemanticModel? semanticModel, CancellationToken cancellationToken)
    {
        if (semanticModel is null)
            return null;

        var isReturnTarget = attributeList.Target?.Identifier.IsKind(SyntaxKind.ReturnKeyword) is true;
        return attributeList.Parent switch
        {
            ParameterSyntax parameter => semanticModel.GetDeclaredSymbol(parameter, cancellationToken)?.Type,
            PropertyDeclarationSyntax property => semanticModel.GetDeclaredSymbol(property, cancellationToken)?.Type,
            IndexerDeclarationSyntax indexer => semanticModel.GetDeclaredSymbol(indexer, cancellationToken)?.Type,
            FieldDeclarationSyntax field => semanticModel.GetTypeInfo(field.Declaration.Type, cancellationToken).Type,
            MethodDeclarationSyntax method when isReturnTarget => semanticModel.GetDeclaredSymbol(method, cancellationToken)?.ReturnType,
            LocalFunctionStatementSyntax localFunction when isReturnTarget => (semanticModel.GetDeclaredSymbol(localFunction, cancellationToken) as IMethodSymbol)?.ReturnType,
            _ => null,
        };
    }

    private static void AnalyzeComments(SemanticModelAnalysisContext context)
    {
        var tree = context.SemanticModel.SyntaxTree;
        var root = tree.GetRoot(context.CancellationToken);
        foreach (var trivia in root.DescendantTrivia())
        {
            var isLineComment = trivia.IsKind(SyntaxKind.SingleLineCommentTrivia);
            if (!isLineComment && !trivia.IsKind(SyntaxKind.MultiLineCommentTrivia))
                continue;

            var text = trivia.ToString();
            var kind = ValueTagComment.Parse(text, out var tags);
            string message;
            if (kind is ValueTagCommentKind.NotValueTag)
                continue;

            if (kind is ValueTagCommentKind.Invalid)
            {
                message = isLineComment
                    ? "'" + text + "' is not a valid value tag comment; use // ValueTag=Tag, // ValueTag=Tag1, Tag2, or // ValueTag Key=Tag Value=Tag"
                    : "'" + text + "' is not a valid value tag comment; use /* ValueTag=Tag */, /* ValueTag=Tag1, Tag2 */, or /* ValueTag Key=Tag Value=Tag */";
            }
            else if (TypeArgumentComments.TryValidate(trivia, tags, context.SemanticModel, context.CancellationToken, out var typeArgumentError))
            {
                if (typeArgumentError is null)
                    continue;

                message = typeArgumentError;
            }
            else if (!IsLocalDeclarationComment(trivia))
            {
                message = isLineComment
                    ? "A // ValueTag comment only applies to the local variables declared by the statement that follows it; use the [ValueTag] attribute on fields, properties, parameters, and return values"
                    : "A value tag comment only applies to the declaration of a local variable; use the [ValueTag] attribute on fields, properties, parameters, and return values";
            }
            else
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                InvalidAnnotation,
                Location.Create(tree, trivia.Span),
                ImmutableDictionary<string, string?>.Empty.Add(ValueTagDiagnostics.RemovableProperty, "true"),
                message));
        }
    }

    private static bool IsLocalDeclarationComment(SyntaxTrivia trivia)
    {
        for (var node = trivia.Token.Parent; node is not null; node = node.Parent)
        {
            IEnumerable<SyntaxNode> declarations = node switch
            {
                VariableDeclarationSyntax variableDeclaration => variableDeclaration.Variables,
                LocalDeclarationStatementSyntax localDeclaration => localDeclaration.Declaration.Variables,
                ForStatementSyntax { Declaration: not null } forStatement => forStatement.Declaration.Variables,
                UsingStatementSyntax { Declaration: not null } usingStatement => usingStatement.Declaration.Variables,
                FixedStatementSyntax fixedStatement => fixedStatement.Declaration.Variables,
                ForEachStatementSyntax forEach => [forEach],
                DeclarationExpressionSyntax { Designation: SingleVariableDesignationSyntax designation } => [designation],
                DeclarationPatternSyntax { Designation: SingleVariableDesignationSyntax designation } => [designation],
                SingleVariableDesignationSyntax designation => [designation],
                _ => [],
            };

            foreach (var declaration in declarations)
            {
                if (TagResolver.GetLocalCommentTrivia(declaration).Contains(trivia))
                    return true;
            }

            if (node is StatementSyntax or MemberDeclarationSyntax)
                return false;
        }

        return false;
    }

    private static void AnalyzeSymbol(SymbolAnalysisContext context, TagResolver resolver, ConcurrentBag<ISymbol> conventionIdMembers)
    {
        switch (context.Symbol)
        {
            case IMethodSymbol { MethodKind: MethodKind.Ordinary or MethodKind.ExplicitInterfaceImplementation } method:
                ReportInheritedMismatch(context, method, ValueTagTargetKind.ReturnValue);
                foreach (var parameter in method.Parameters)
                {
                    ReportInheritedMismatch(context, parameter, ValueTagTargetKind.Symbol);
                    ReportRedundantTag(context, resolver, parameter);
                }

                break;

            case IMethodSymbol { MethodKind: MethodKind.Constructor } constructor:
                foreach (var parameter in constructor.Parameters)
                {
                    ReportRedundantTag(context, resolver, parameter);
                }

                break;

            case IPropertySymbol property:
                ReportInheritedMismatch(context, property, ValueTagTargetKind.Symbol);
                ReportRedundantTag(context, resolver, property);
                CollectConventionIdMember(resolver, property, conventionIdMembers);
                break;

            case IFieldSymbol field:
                ReportRedundantTag(context, resolver, field);
                CollectConventionIdMember(resolver, field, conventionIdMembers);
                break;
        }
    }

    private static void ReportInheritedMismatch(SymbolAnalysisContext context, ISymbol symbol, string targetKind)
    {
        var ownTags = TagResolver.GetOwnExplicitTags(symbol);
        if (ownTags.IsEmpty)
            return;

        var location = symbol.Locations.FirstOrDefault(location => location.IsInSource);
        if (location is null)
            return;

        foreach (var baseSymbol in TagResolver.GetOverriddenOrImplementedSymbols(symbol))
        {
            var baseTags = TagResolver.GetExplicitAndInheritedTags(baseSymbol);
            if (baseTags.IsEmpty || TagInfo.AreCompatible(ownTags, baseTags))
                continue;

            var properties = ImmutableDictionary<string, string?>.Empty
                .Add(ValueTagDiagnostics.TagsProperty, baseTags.Serialize())
                .Add(ValueTagDiagnostics.TargetKindProperty, targetKind);

            context.ReportDiagnostic(Diagnostic.Create(
                InheritedTagMismatch,
                location,
                [location],
                properties,
                ValueTagDescriptions.DescribeSymbol(symbol),
                ownTags.ToAttributeString(),
                ValueTagDescriptions.DescribeSymbol(baseSymbol) + ValueTagDescriptions.GetSite(baseSymbol, location.SourceTree),
                baseTags.ToAttributeString()));
            return;
        }
    }

    private static void ReportRedundantTag(SymbolAnalysisContext context, TagResolver resolver, ISymbol symbol)
    {
        AttributeData? valueTagAttribute = null;
        foreach (var attribute in symbol.GetAttributes())
        {
            if (!TagResolver.IsValueTagAttribute(attribute.AttributeClass))
                continue;

            if (valueTagAttribute is not null)
                return;

            valueTagAttribute = attribute;
        }

        if (valueTagAttribute?.ApplicationSyntaxReference is null)
            return;

        var explicitTags = TagResolver.ReadMemberAttribute(valueTagAttribute);
        if (explicitTags.HasKeyOrValue || explicitTags.Tags.Length is not 1)
            return;

        var conventionTags = resolver.GetConventionTags(symbol);
        if (conventionTags.IsEmpty || !conventionTags.TagsEqual(explicitTags))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            RedundantTag,
            valueTagAttribute.ApplicationSyntaxReference.GetSyntax(context.CancellationToken).GetLocation(),
            ImmutableDictionary<string, string?>.Empty.Add(ValueTagDiagnostics.RemovableProperty, "true"),
            explicitTags.ToAttributeString(),
            ValueTagDescriptions.DescribeSymbol(symbol)));
    }

    private static void CollectConventionIdMember(TagResolver resolver, ISymbol symbol, ConcurrentBag<ISymbol> conventionIdMembers)
    {
        if (symbol.IsOverride || TagResolver.GetConventionName(symbol) is not "Id" || !TagResolver.GetExplicitAndInheritedTags(symbol).IsEmpty)
            return;

        if (!resolver.GetConventionTags(symbol).IsEmpty)
        {
            conventionIdMembers.Add(symbol);
        }
    }

    private static void ReportAmbiguousConventions(CompilationAnalysisContext context, TagResolver resolver, ConcurrentBag<ISymbol> conventionIdMembers)
    {
        foreach (var group in conventionIdMembers.GroupBy(member => member.ContainingType.Name, StringComparer.Ordinal))
        {
            var members = group.ToArray();
            if (members.Select(member => member.ContainingType).Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default).Count() < 2)
                continue;

            foreach (var member in members)
            {
                var other = members.First(candidate => !SymbolEqualityComparer.Default.Equals(candidate.ContainingType, member.ContainingType));
                var location = member.Locations.FirstOrDefault(location => location.IsInSource);
                if (location is null)
                    continue;

                context.ReportDiagnostic(Diagnostic.Create(
                    AmbiguousConvention,
                    location,
                    ValueTagDescriptions.DescribeSymbol(member, qualified: true),
                    ValueTagDescriptions.DescribeSymbol(other, qualified: true),
                    resolver.GetConventionTags(member).Tags.FirstOrDefault()));
            }
        }
    }
}
