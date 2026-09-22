using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.TaggedValues.Analyzer;

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
        description: "The operands of an addition or a subtraction, the branches of a conditional expression, a null-coalescing expression, or a switch expression, or the elements of a collection, have different tags. Make every branch or element produce a value with the same tag, or fix the [ValueTag] of the declaration that is tagged incorrectly.");

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

    public static readonly DiagnosticDescriptor MissingReturnTag = new(
        id: ValueTagDiagnostics.MissingReturnTagDiagnosticId,
        title: "Tag the return value with the tag of the returned values",
        messageFormat: "Every value returned by {0} is {1}; add {2} so the tag flows to the callers",
        category: "TaggedValues",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Every return statement of the method, the local function, or the property getter returns values with the same tag, but the return value is not tagged, so the callers lose the tag. Add the [ValueTag] attribute to the return value or to the property.");

    public static readonly DiagnosticDescriptor UntaggedValue = new(
        id: ValueTagDiagnostics.UntaggedValueDiagnosticId,
        title: "Do not mix tagged values with untagged values",
        messageFormat: "{0}",
        category: "TaggedValues",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "In strict mode, enabled with taggedvalues.strict in .editorconfig, a tagged value cannot be compared with an untagged value, or flow from or to an untagged declaration. Tag the untagged declaration. Default values, null, constants, and Guid.Empty are allowed, and so are new values, such as the result of Guid.NewGuid, when they flow to a tagged declaration.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
    [
        ComparedValues,
        FlowMismatch,
        CombinedValues,
        InheritedTagMismatch,
        InvalidAnnotation,
        AmbiguousConvention,
        RedundantTag,
        MissingReturnTag,
        UntaggedValue,
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
            var conventionIdMembers = new ConcurrentQueue<ISymbol>();

            context.RegisterOperationAction(context => AnalyzeBinary(context, resolver), OperationKind.Binary);
            context.RegisterOperationAction(context => AnalyzeTupleBinary(context, resolver), OperationKind.TupleBinary);
            context.RegisterOperationAction(context => AnalyzeInvocation(context, resolver), OperationKind.Invocation);
            context.RegisterOperationAction(context => AnalyzeArgument(context, resolver), OperationKind.Argument);
            context.RegisterOperationAction(context => AnalyzeAssignment(context, resolver), OperationKind.SimpleAssignment, OperationKind.CoalesceAssignment);
            context.RegisterOperationAction(context => AnalyzeDeconstruction(context, resolver), OperationKind.DeconstructionAssignment, OperationKind.Loop);
            context.RegisterOperationAction(context => AnalyzeVariableDeclarator(context, resolver), OperationKind.VariableDeclarator);
            context.RegisterOperationAction(context => AnalyzePatternVariable(context, resolver), OperationKind.DeclarationPattern, OperationKind.RecursivePattern, OperationKind.ListPattern);
            context.RegisterOperationAction(context => AnalyzeMemberInitializer(context, resolver), OperationKind.FieldInitializer, OperationKind.PropertyInitializer);
            context.RegisterOperationAction(context => AnalyzeReturn(context, resolver), OperationKind.Return, OperationKind.YieldReturn);
            context.RegisterOperationAction(context => AnalyzeCompoundAssignment(context, resolver), OperationKind.CompoundAssignment);
            context.RegisterOperationAction(context => AnalyzeCombinedValues(context, resolver), OperationKind.Conditional, OperationKind.Coalesce, OperationKind.SwitchExpression, OperationKind.ArrayInitializer, OperationKind.CollectionExpression, OperationKind.ObjectOrCollectionInitializer);
            context.RegisterOperationAction(context => AnalyzeAttribute(context, resolver), OperationKind.Attribute);
            context.RegisterSemanticModelAction(context => AnalyzeComments(context, resolver));
            context.RegisterOperationBlockAction(context => AnalyzeReturnedValues(context, resolver));
            context.RegisterSymbolStartAction(context => AnalyzePropertyReturnedValues(context, resolver), SymbolKind.NamedType);
            context.RegisterSymbolAction(context => AnalyzeSymbol(context, resolver, conventionIdMembers), SymbolKind.Method, SymbolKind.Property, SymbolKind.Field);
            context.RegisterSymbolAction(AnalyzeInheritedInterfaceImplementations, SymbolKind.NamedType);
            context.RegisterCompilationEndAction(context => ReportAmbiguousConventions(context, resolver, conventionIdMembers));
        });
    }

    private static void AnalyzeBinary(OperationAnalysisContext context, TagResolver resolver)
    {
        var operation = (IBinaryOperation)context.Operation;
        if (operation.OperatorKind is BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals or BinaryOperatorKind.LessThan or BinaryOperatorKind.LessThanOrEqual or BinaryOperatorKind.GreaterThan or BinaryOperatorKind.GreaterThanOrEqual)
        {
            ReportComparison(context, resolver, operation.Syntax, operation.LeftOperand, operation.RightOperand);

            // The operands of a user-defined comparison operator flow to its tagged parameters
            if (operation.OperatorMethod is { Parameters.Length: 2 } comparisonOperator)
            {
                ReportTaggedParameterFlow(context, resolver, operation.LeftOperand, comparisonOperator.Parameters[0]);
                ReportTaggedParameterFlow(context, resolver, operation.RightOperand, comparisonOperator.Parameters[1]);
            }

            return;
        }

        if (TagResolver.IsBuiltInNumericOperator(operation.OperatorMethod, operation.Type))
        {
            // orderId + projectId
            if (operation.OperatorKind is BinaryOperatorKind.Add or BinaryOperatorKind.Subtract)
            {
                ReportCombinedValues(context, resolver, [operation.LeftOperand, operation.RightOperand]);
            }

            return;
        }

        // The operands of a user-defined operator flow to its parameters
        if (operation.OperatorMethod is { Parameters.Length: 2 } operatorMethod)
        {
            ReportFlow(context, resolver, operation.LeftOperand, operatorMethod.Parameters[0], resolver.GetDeclaredTags(operatorMethod.Parameters[0]), ValueTagTargetKind.Symbol);
            ReportFlow(context, resolver, operation.RightOperand, operatorMethod.Parameters[1], resolver.GetDeclaredTags(operatorMethod.Parameters[1]), ValueTagTargetKind.Symbol);
        }
    }

    private static void AnalyzeCompoundAssignment(OperationAnalysisContext context, TagResolver resolver)
    {
        var operation = (ICompoundAssignmentOperation)context.Operation;
        if (TagResolver.IsBuiltInNumericOperator(operation.OperatorMethod, operation.Type))
        {
            // orderId += projectId
            if (operation.OperatorKind is BinaryOperatorKind.Add or BinaryOperatorKind.Subtract)
            {
                ReportCombinedValues(context, resolver, [operation.Target, operation.Value]);
            }

            return;
        }

        // A static operator + receives the target and the value, while an instance operator += only receives the value
        var valueParameter = operation.OperatorMethod switch
        {
            { IsStatic: true, Parameters.Length: 2 } staticOperator => staticOperator.Parameters[1],
            { IsStatic: false, Parameters.Length: 1 } instanceOperator => instanceOperator.Parameters[0],
            _ => null,
        };

        if (valueParameter is not null)
        {
            ReportFlow(context, resolver, operation.Value, valueParameter, resolver.GetDeclaredTags(valueParameter), ValueTagTargetKind.Symbol);
        }

        if (operation.OperatorMethod is { IsStatic: true, Parameters.Length: 2 } binaryOperator)
        {
            // usd += eur: the target flows to the first parameter of the operator, and the result of the operator flows back to the target
            ReportTaggedParameterFlow(context, resolver, operation.Target, binaryOperator.Parameters[0]);

            var resultTags = resolver.GetDeclaredTags(binaryOperator);
            var targetTags = resolver.GetTag(operation.Target);
            if (!resultTags.IsEmpty && !targetTags.IsEmpty && !TagInfo.AreCompatible(resultTags, targetTags))
            {
                context.ReportDiagnostic(FlowMismatch, operation.Syntax, ValueTagDescriptions.DescribeSymbol(binaryOperator), resultTags.ToAttributeString(), ValueTagDescriptions.Describe(operation.Target), targetTags.ToAttributeString());
            }
        }
    }

    /// <summary>
    /// Reports a value that flows to a tagged parameter of an operator. Untagged parameters are not reported in strict mode, as every use of the operator would be.
    /// </summary>
    private static void ReportTaggedParameterFlow(OperationAnalysisContext context, TagResolver resolver, IOperation value, IParameterSymbol parameter)
    {
        var tags = resolver.GetDeclaredTags(parameter);
        if (!tags.IsEmpty)
        {
            ReportFlow(context, resolver, value, parameter, tags, ValueTagTargetKind.Symbol);
        }
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
        var rightTags = resolver.GetTag(right);
        if (leftTags.IsEmpty != rightTags.IsEmpty)
        {
            var (tagged, tags, untagged) = leftTags.IsEmpty ? (right, rightTags, left) : (left, leftTags, right);
            if (resolver.IsStrictModeEnabled(reportNode.SyntaxTree) && IsReportableUntaggedValue(untagged, resolver))
            {
                ReportUntaggedValue(
                    context,
                    resolver,
                    reportNode,
                    ValueTagDescriptions.Describe(tagged) + " is " + tags.ToAttributeString() + " and is compared with " + ValueTagDescriptions.Describe(untagged) + ", which is not tagged",
                    untagged,
                    tags);
            }

            return;
        }

        if (leftTags.IsEmpty || TagInfo.AreCompatible(leftTags, rightTags))
            return;

        context.ReportDiagnostic(ComparedValues, reportNode, ValueTagDescriptions.Describe(left), leftTags.ToAttributeString(), ValueTagDescriptions.Describe(right), rightTags.ToAttributeString());
    }

    /// <summary>
    /// Recognizes <c>a.Equals(b)</c>, <c>a.CompareTo(b)</c>, <c>object.Equals(a, b)</c>, <c>comparer.Equals(a, b)</c>, <c>comparer.Compare(a, b)</c>,
    /// <c>string.Equals(a, b, comparison)</c>, and similar calls.
    /// </summary>
    private static bool TryGetComparedOperands(IInvocationOperation invocation, [NotNullWhen(true)] out IOperation? left, [NotNullWhen(true)] out IOperation? right)
    {
        left = null;
        right = null;
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

        // a.Equals(b, StringComparison.Ordinal): the first argument has the type of the instance, and the next ones configure the comparison
        if (arguments.Length > 1 && invocation.Instance is not null && !method.IsStatic &&
            arguments[0].Parameter is { } otherParameter && SymbolEqualityComparer.Default.Equals(otherParameter.OriginalDefinition.Type, method.ContainingType.OriginalDefinition))
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

        var parameter = argument.Parameter.OriginalDefinition;
        var isDeclared = !resolver.GetDeclaredTags(parameter).IsEmpty;

        // Reported as a comparison, unless the parameters of the comparison are tagged
        if (!isDeclared && argument.Parent is IInvocationOperation invocation && TryGetComparedOperands(invocation, out _, out _))
            return;

        var value = argument.Value.UnwrapImplicitConversions();
        if (value is IDeclarationExpressionOperation { Expression: ILocalReferenceOperation outVariable })
        {
            ReportOutVariableMismatch(context, resolver, argument, outVariable.Local);
            return;
        }

        if (value is IDeclarationExpressionOperation or IDelegateCreationOperation or IAnonymousFunctionOperation or IDiscardOperation)
            return;

        // TryGet(out existing): the value flows from the parameter to the variable
        if (argument.Parameter.RefKind is RefKind.Out)
        {
            ReportOutArgument(context, resolver, argument, value);
            return;
        }

        var expected = resolver.GetExpectedArgumentTags(argument);
        if (expected.IsEmpty && !resolver.IsStrictModeEnabled(argument.Syntax.SyntaxTree))
            return;

        // Delete(orderId, projectId) with params: each element flows to the parameter, whose tags describe its elements
        if (GetParamsElements(argument) is { } elements)
        {
            foreach (var element in elements)
            {
                ReportFlow(context, resolver, element, parameter, expected, isDeclared ? ValueTagTargetKind.Symbol : null);
            }

            return;
        }

        ReportFlow(context, resolver, argument.Value, parameter, expected, isDeclared ? ValueTagTargetKind.Symbol : null);
    }

    /// <summary>
    /// Returns the elements of the array or the collection the compiler creates for a <see langword="params"/> argument.
    /// </summary>
    private static IEnumerable<IOperation>? GetParamsElements(IArgumentOperation argument)
    {
        if (argument.ArgumentKind is not (ArgumentKind.ParamArray or ArgumentKind.ParamCollection))
            return null;

        return argument.Value.UnwrapImplicitConversions() switch
        {
            IArrayCreationOperation { IsImplicit: true, Initializer: { } initializer } => initializer.ElementValues,
            IArrayCreationOperation { IsImplicit: true } => [],
            ICollectionExpressionOperation { IsImplicit: true } collectionExpression => collectionExpression.Elements,
            _ => null,
        };
    }

    /// <summary>
    /// Returns whether a collection is the implicit collection of a <see langword="params"/> argument, which the user did not write as a collection.
    /// </summary>
    private static bool IsParamsCollection(IOperation operation)
    {
        var collection = operation is IArrayInitializerOperation { Parent: IArrayCreationOperation arrayCreation } ? arrayCreation : operation;
        if (!collection.IsImplicit)
            return false;

        var parent = collection.Parent;
        while (parent is IConversionOperation { IsImplicit: true })
        {
            parent = parent.Parent;
        }

        return parent is IArgumentOperation { ArgumentKind: ArgumentKind.ParamArray or ArgumentKind.ParamCollection };
    }

    /// <summary>
    /// Reports <c>TryGet(out existing)</c> when the parameter and the variable are tagged differently: the value flows from the parameter to the variable.
    /// </summary>
    private static void ReportOutArgument(OperationAnalysisContext context, TagResolver resolver, IArgumentOperation argument, IOperation variable)
    {
        if (argument.Parameter is null)
            return;

        var (target, targetKind) = GetAssignmentTarget(resolver, variable);
        var parameter = argument.Parameter.OriginalDefinition;
        var parameterTags = resolver.GetExpectedArgumentTags(argument);
        var variableTags = resolver.GetTag(variable);
        var reportTree = argument.Syntax.SyntaxTree;
        var parameterDescription = ValueTagDescriptions.DescribeSymbol(parameter) + ValueTagDescriptions.GetSite(parameter, reportTree);
        var variableDescription = ValueTagDescriptions.Describe(variable);
        if (parameterTags.IsEmpty)
        {
            // Strict mode: an untagged parameter of the compilation flows to a tagged variable
            if (!variableTags.IsEmpty && resolver.IsStrictModeEnabled(reportTree) && CanDeclareTag(parameter, resolver))
            {
                ReportUntaggedValue(context, resolver, variable.Syntax, parameterDescription + " is not tagged and flows to " + variableDescription + ", which is " + variableTags.ToAttributeString(), parameter, variableTags, ValueTagTargetKind.Symbol);
            }

            return;
        }

        if (variableTags.IsEmpty)
        {
            // Strict mode: a tagged parameter flows to an untagged variable that could be tagged
            if (resolver.IsStrictModeEnabled(reportTree) && target is not null && CanDeclareTag(target, resolver))
            {
                ReportUntaggedValue(context, resolver, variable.Syntax, parameterDescription + " is " + parameterTags.ToAttributeString() + " and flows to " + variableDescription + ", which is not tagged", target is ILocalSymbol ? null : target, parameterTags, ValueTagTargetKind.Symbol);
            }

            return;
        }

        if (TagInfo.AreCompatible(parameterTags, variableTags))
            return;

        var properties = ImmutableDictionary<string, string?>.Empty;
        var additionalLocations = new List<Location>();
        if (targetKind is not null && target is not null && parameterTags.IsExplicit && CanChangeTagOf(target, resolver) && resolver.GetLocationInCompilation(target) is { } targetLocation)
        {
            properties = properties
                .Add(ValueTagDiagnostics.TagsProperty, parameterTags.Serialize())
                .Add(ValueTagDiagnostics.TargetKindProperty, ValueTagTargetKind.ForSymbol(target, targetKind));
            additionalLocations.Add(targetLocation);
        }

        context.ReportDiagnostic(Diagnostic.Create(FlowMismatch, variable.Syntax.GetLocation(), additionalLocations, properties, parameterDescription, parameterTags.ToAttributeString(), variableDescription, variableTags.ToAttributeString()));
    }

    /// <summary>
    /// Reports <c>Get(out var /* ValueTag=ProjectId */ id)</c> when the parameter is tagged differently: the value flows from the parameter to the variable.
    /// </summary>
    private static void ReportOutVariableMismatch(OperationAnalysisContext context, TagResolver resolver, IArgumentOperation argument, ILocalSymbol local)
    {
        var localTags = resolver.GetLocalCommentTags(local);
        if (localTags.IsEmpty || argument.Parameter is null)
            return;

        var parameterTags = resolver.GetExpectedArgumentTags(argument);
        if (parameterTags.IsEmpty || TagInfo.AreCompatible(parameterTags, localTags))
            return;

        var properties = ImmutableDictionary<string, string?>.Empty;
        var additionalLocations = new List<Location>();
        if (parameterTags.IsExplicit && resolver.GetLocationInCompilation(local) is { } localLocation)
        {
            properties = properties
                .Add(ValueTagDiagnostics.TagsProperty, parameterTags.Serialize())
                .Add(ValueTagDiagnostics.TargetKindProperty, ValueTagTargetKind.Local);
            additionalLocations.Add(localLocation);
        }

        var parameter = argument.Parameter.OriginalDefinition;
        context.ReportDiagnostic(Diagnostic.Create(
            FlowMismatch,
            argument.Value.Syntax.GetLocation(),
            additionalLocations,
            properties,
            ValueTagDescriptions.DescribeSymbol(parameter) + ValueTagDescriptions.GetSite(parameter, argument.Syntax.SyntaxTree),
            parameterTags.ToAttributeString(),
            ValueTagDescriptions.DescribeSymbol(local),
            localTags.ToAttributeString()));
    }

    private static void AnalyzeAssignment(OperationAnalysisContext context, TagResolver resolver)
    {
        var (target, value) = context.Operation switch
        {
            ISimpleAssignmentOperation assignment => (assignment.Target, assignment.Value),
            ICoalesceAssignmentOperation coalesceAssignment => (coalesceAssignment.Target, coalesceAssignment.Value),
            _ => default,
        };

        if (value is null || target is null or IDeclarationExpressionOperation or ITupleOperation)
            return;

        ReportAssignment(context, resolver, target, value);
    }

    private static void ReportAssignment(OperationAnalysisContext context, TagResolver resolver, IOperation target, IOperation value)
    {
        if (target is IPropertyReferenceOperation { Property.ContainingType.IsAnonymousType: true } or IDiscardOperation)
            return;

        var expected = resolver.GetTag(target);
        if (expected.IsEmpty && !resolver.IsStrictModeEnabled(target.Syntax.SyntaxTree))
            return;

        var (symbol, kind) = GetAssignmentTarget(resolver, target);

        // list[0] = value: the tags of the indexer come from the list, so describe the expression rather than 'indexer List.this[]'
        var targetDescription = symbol is null || target is IPropertyReferenceOperation { Property.IsIndexer: true } ? ValueTagDescriptions.Describe(target) : null;
        ReportFlow(context, resolver, value, symbol, expected, kind, targetDescription);
    }

    /// <summary>
    /// Returns the declaration an assignment writes to, and what the code fix edits to change its tags. The declaration of a member of a generic type
    /// is its original definition, e.g. <c>T Value</c> for <c>box.Value</c>.
    /// </summary>
    private static (ISymbol? Symbol, string? TargetKind) GetAssignmentTarget(TagResolver resolver, IOperation target)
    {
        return target switch
        {
            IFieldReferenceOperation fieldReference => (fieldReference.Field.OriginalDefinition, ValueTagTargetKind.Symbol),
            IPropertyReferenceOperation propertyReference => (propertyReference.Property.OriginalDefinition, ValueTagTargetKind.Symbol),
            IParameterReferenceOperation parameterReference => (parameterReference.Parameter.OriginalDefinition, ValueTagTargetKind.Symbol),
            ILocalReferenceOperation localReference => (localReference.Local, resolver.GetLocalCommentTags(localReference.Local).IsEmpty ? null : ValueTagTargetKind.Local),
            _ => ((ISymbol?)null, (string?)null),
        };
    }

    /// <summary>
    /// Reports the values of a deconstruction that flow to a target with another tag: <c>(orderId, projectId) = (order.ProjectId, order.Id)</c>,
    /// <c>var (a /* ValueTag=OrderId */, b) = value</c>, and <c>foreach (var (a /* ValueTag=OrderId */, b) in map)</c>.
    /// </summary>
    private static void AnalyzeDeconstruction(OperationAnalysisContext context, TagResolver resolver)
    {
        IOperation target;
        IOperation? value;
        TagInfo? tags;
        ITypeSymbol? type;
        switch (context.Operation)
        {
            case IDeconstructionAssignmentOperation deconstruction:
                (target, value, tags, type) = (deconstruction.Target, deconstruction.Value, null, deconstruction.Value.Type);
                break;

            case IForEachLoopOperation { LoopControlVariable: not (null or IVariableDeclaratorOperation) } forEach when forEach.Syntax is ForEachVariableStatementSyntax forEachSyntax && forEach.SemanticModel is { } semanticModel:
                (target, value, tags, type) = (forEach.LoopControlVariable, null, resolver.GetTag(forEach.Collection), semanticModel.GetForEachStatementInfo(forEachSyntax).ElementType);
                break;

            default:
                return;
        }

        foreach (var element in GetDeconstructionTargets(target))
        {
            var (elementValue, elementTags) = resolver.GetDeconstructedElement(target, element, value, tags, type);
            if (element is ILocalReferenceOperation { Local: var local } && local.DeclaringSyntaxReferences.Any(reference => target.Syntax.Span.Contains(reference.Span)))
            {
                // A variable declared by the deconstruction takes the tags of its value, unless it has a comment
                var expected = resolver.GetLocalCommentTags(local);
                if (expected.IsEmpty)
                    continue;

                if (elementTags is null)
                {
                    if (elementValue is not null)
                    {
                        ReportFlow(context, resolver, elementValue, local, expected, ValueTagTargetKind.Local);
                    }
                }
                else if (!elementTags.IsEmpty && !TagInfo.AreCompatible(elementTags, expected))
                {
                    ReportDeconstructedValueMismatch(context, resolver, element, local, elementTags, expected);
                }

                continue;
            }

            // Only the elements of a tuple literal have an operation to report on
            if (elementTags is null && elementValue is not null)
            {
                ReportAssignment(context, resolver, element, elementValue);
            }
        }
    }

    private static IEnumerable<IOperation> GetDeconstructionTargets(IOperation target)
    {
        switch (target)
        {
            case IDeclarationExpressionOperation declaration:
                return GetDeconstructionTargets(declaration.Expression);

            case ITupleOperation tuple:
                return tuple.Elements.SelectMany(GetDeconstructionTargets);

            case IConversionOperation { IsImplicit: true } conversion:
                return GetDeconstructionTargets(conversion.Operand);

            default:
                return [target];
        }
    }

    /// <summary>
    /// Reports a deconstructed value that has no operation to report on, e.g. a key of a dictionary in <c>foreach (var (key /* ValueTag=OrderId */, value) in map)</c>.
    /// </summary>
    private static void ReportDeconstructedValueMismatch(OperationAnalysisContext context, TagResolver resolver, IOperation element, ILocalSymbol local, TagInfo elementTags, TagInfo expected)
    {
        var properties = ImmutableDictionary<string, string?>.Empty;
        var additionalLocations = new List<Location>();
        if (elementTags.IsExplicit && resolver.GetLocationInCompilation(local) is { } localLocation)
        {
            properties = properties
                .Add(ValueTagDiagnostics.TagsProperty, elementTags.Serialize())
                .Add(ValueTagDiagnostics.TargetKindProperty, ValueTagTargetKind.Local);
            additionalLocations.Add(localLocation);
        }

        context.ReportDiagnostic(Diagnostic.Create(
            FlowMismatch,
            element.Syntax.GetLocation(),
            additionalLocations,
            properties,
            "the deconstructed value",
            elementTags.ToAttributeString(),
            ValueTagDescriptions.DescribeSymbol(local),
            expected.ToAttributeString()));
    }

    private static void AnalyzeVariableDeclarator(OperationAnalysisContext context, TagResolver resolver)
    {
        var operation = (IVariableDeclaratorOperation)context.Operation;

        // foreach (var /* ValueTag=OrderId */ id in ids): the elements of the collection flow to the variable
        var value = operation.Parent is IForEachLoopOperation forEachLoop ? forEachLoop.Collection : operation.GetVariableInitializer()?.Value;
        if (value is null)
            return;

        var expected = resolver.GetLocalCommentTags(operation.Symbol);
        if (expected.IsEmpty)
            return;

        ReportFlow(context, resolver, value, operation.Symbol, expected, ValueTagTargetKind.Local);
    }

    /// <summary>
    /// Reports <c>value is Guid /* ValueTag=OrderId */ id</c> when the matched value has another tag: the value flows to the variable.
    /// </summary>
    private static void AnalyzePatternVariable(OperationAnalysisContext context, TagResolver resolver)
    {
        var pattern = (IPatternOperation)context.Operation;
        var declaredSymbol = pattern switch
        {
            IDeclarationPatternOperation declarationPattern => declarationPattern.DeclaredSymbol,
            IRecursivePatternOperation recursivePattern => recursivePattern.DeclaredSymbol,
            IListPatternOperation listPattern => listPattern.DeclaredSymbol,
            _ => null,
        };

        if (declaredSymbol is not ILocalSymbol local)
            return;

        var expected = resolver.GetLocalCommentTags(local);
        if (expected.IsEmpty)
            return;

        // The tags of a key or a value of a deconstructed KeyValuePair have no operation to report on
        var (value, tags) = resolver.GetPatternInput(pattern);
        if (value is null || tags is not null)
            return;

        ReportFlow(context, resolver, value, local, expected, ValueTagTargetKind.Local);
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
                ReportFlow(context, resolver, operation.ReturnedValue, property, resolver.GetDeclaredTags(property), ValueTagTargetKind.Symbol, reportUntaggedTarget: false);
                break;

            case IMethodSymbol { MethodKind: MethodKind.AnonymousFunction } lambda:
                ReportFlow(context, resolver, operation.ReturnedValue, lambda, TagResolver.GetOwnExplicitTags(lambda), targetKind: null, reportUntaggedTarget: false);
                break;

            case IMethodSymbol method:
                ReportFlow(context, resolver, operation.ReturnedValue, method, resolver.GetDeclaredTags(method), ValueTagTargetKind.ReturnValue, reportUntaggedTarget: false);
                break;
        }
    }

    private static void ReportFlow(OperationAnalysisContext context, TagResolver resolver, IOperation value, ISymbol? target, TagInfo expected, string? targetKind, string? targetDescription = null, bool reportUntaggedTarget = true)
    {
        var reportTree = value.Syntax.SyntaxTree;
        if (expected.IsEmpty)
        {
            // Strict mode: a tagged value flows to a declaration that could be tagged
            if (!reportUntaggedTarget || target is null || !CanDeclareTag(target, resolver) || !resolver.IsStrictModeEnabled(reportTree))
                return;

            var taggedSource = resolver.GetTag(value);
            if (taggedSource.IsEmpty)
                return;

            ReportUntaggedValue(
                context,
                resolver,
                value.Syntax,
                ValueTagDescriptions.Describe(value) + " is " + taggedSource.ToAttributeString() + " and flows to " + (targetDescription ?? ValueTagDescriptions.DescribeSymbol(target) + ValueTagDescriptions.GetSite(target, reportTree)) + ", which is not tagged",
                target is ILocalSymbol ? null : target,
                taggedSource,
                ValueTagTargetKind.Symbol);
            return;
        }

        var source = resolver.GetTag(value);
        if (source.IsEmpty)
        {
            // Strict mode: an existing untagged value flows to a tagged declaration
            if (resolver.IsStrictModeEnabled(reportTree) && IsReportableUntaggedValue(value, resolver) && !IsNewValue(value, resolver))
            {
                ReportUntaggedValue(
                    context,
                    resolver,
                    value.Syntax,
                    ValueTagDescriptions.Describe(value) + " is not tagged and flows to " + (targetDescription ?? (target is null ? "the target" : ValueTagDescriptions.DescribeSymbol(target) + ValueTagDescriptions.GetSite(target, reportTree))) + ", which is " + expected.ToAttributeString(),
                    value,
                    expected);
            }

            return;
        }

        if (TagInfo.AreCompatible(source, expected))
            return;

        targetDescription ??= target is null ? "the target" : ValueTagDescriptions.DescribeSymbol(target) + ValueTagDescriptions.GetSite(target, reportTree);

        var properties = ImmutableDictionary<string, string?>.Empty;
        var additionalLocations = new List<Location>();
        if (targetKind is not null && source.IsExplicit && target is not null && !target.IsImplicitlyDeclared && CanChangeTagOf(target, resolver) && resolver.GetLocationInCompilation(target) is { } targetLocation)
        {
            properties = properties
                .Add(ValueTagDiagnostics.TagsProperty, source.Serialize())
                .Add(ValueTagDiagnostics.TargetKindProperty, ValueTagTargetKind.ForSymbol(target, targetKind));
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

    /// <summary>
    /// Returns whether the code fix can change the tags of a declaration by editing its <c>[ValueTag]</c> attribute. It cannot when the tags are inherited
    /// from an overridden or implemented member, or declared by an <c>[assembly: ValueTag]</c> attribute, as a new attribute would not replace them.
    /// </summary>
    private static bool CanChangeTagOf(ISymbol target, TagResolver resolver)
    {
        if (target is ILocalSymbol)
            return true;

        if (resolver.HasExternalTags(target))
            return false;

        return !TagResolver.GetOwnExplicitTags(target).IsEmpty || TagResolver.GetExplicitAndInheritedTags(target).IsEmpty;
    }

    private static void AnalyzeReturnedValues(OperationBlockAnalysisContext context, TagResolver resolver)
    {
        if (context.OwningSymbol is not IMethodSymbol method)
            return;

        foreach (var block in context.OperationBlocks)
        {
            // Property getters are reported on the property, see AnalyzePropertyReturnedValues. An override or an implementation, such as ToString,
            // gets its tags from the base member, and the callers of the base member would not see a tag added to the override.
            if (method.MethodKind is MethodKind.Ordinary or MethodKind.ExplicitInterfaceImplementation && !method.ReturnsVoid && !TagResolver.GetOverriddenOrImplementedSymbols(method).Any())
            {
                ReportMissingReturnTag(context.ReportDiagnostic, resolver, method, ValueTagTargetKind.ReturnValue, block);
            }

            foreach (var localFunction in block.Descendants().OfType<ILocalFunctionOperation>())
            {
                if (localFunction.Body is not null && !localFunction.Symbol.ReturnsVoid)
                {
                    ReportMissingReturnTag(context.ReportDiagnostic, resolver, localFunction.Symbol, ValueTagTargetKind.ReturnValue, localFunction.Body);
                }
            }
        }
    }

    private static void AnalyzePropertyReturnedValues(SymbolStartAnalysisContext context, TagResolver resolver)
    {
        // The diagnostic is reported on the name of the property, which is outside of the getter, so it is reported when the containing type ends
        var diagnostics = new ConcurrentQueue<Diagnostic>();
        context.RegisterOperationBlockAction(context =>
        {
            if (context.OwningSymbol is not IMethodSymbol { MethodKind: MethodKind.PropertyGet, AssociatedSymbol: IPropertySymbol property } || TagResolver.GetOverriddenOrImplementedSymbols(property).Any())
                return;

            foreach (var block in context.OperationBlocks)
            {
                ReportMissingReturnTag(diagnostics.Enqueue, resolver, property, ValueTagTargetKind.Symbol, block);
            }
        });

        context.RegisterSymbolEndAction(context =>
        {
            foreach (var diagnostic in diagnostics)
            {
                context.ReportDiagnostic(diagnostic);
            }
        });
    }

    private static void ReportMissingReturnTag(Action<Diagnostic> reportDiagnostic, TagResolver resolver, ISymbol target, string targetKind, IOperation body)
    {
        if (target.IsImplicitlyDeclared || !resolver.GetDeclaredTags(target).IsEmpty)
            return;

        var location = resolver.GetLocationInCompilation(target);
        if (location is null)
            return;

        var returnedValues = new List<IOperation>();
        TagResolver.CollectReturnedValues(body, returnedValues);

        TagInfo? tags = null;
        foreach (var returnedValue in returnedValues)
        {
            // Only suggest tags the user wrote, not tags inferred from a naming convention
            var returnedTags = resolver.GetTag(returnedValue);
            if (returnedTags.IsEmpty || !returnedTags.IsExplicit || (tags is not null && !tags.TagsEqual(returnedTags)))
                return;

            tags ??= returnedTags;
        }

        if (tags is null)
            return;

        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(ValueTagDiagnostics.TagsProperty, tags.Serialize())
            .Add(ValueTagDiagnostics.TargetKindProperty, ValueTagTargetKind.ForSymbol(target, targetKind));

        reportDiagnostic(Diagnostic.Create(
            MissingReturnTag,
            location,
            [location],
            properties,
            target switch
            {
                IMethodSymbol { MethodKind: MethodKind.LocalFunction } => "local function '" + target.Name + "'",
                IMethodSymbol => "method '" + target.ContainingType.Name + "." + target.Name + "'",
                _ => ValueTagDescriptions.DescribeSymbol(target),
            },
            tags.ToAttributeString(),
            tags.ToAttributeString(isReturnValue: targetKind is ValueTagTargetKind.ReturnValue)));
    }

    private static void AnalyzeCombinedValues(OperationAnalysisContext context, TagResolver resolver)
    {
        if (context.Operation is IObjectOrCollectionInitializerOperation initializer)
        {
            AnalyzeCollectionInitializer(context, resolver, initializer);
            return;
        }

        // Log("{0} {1}", orderId, projectId): the arguments of params are not a collection the user wrote, and each one flows to the parameter
        if (context.Operation is IArrayInitializerOperation or ICollectionExpressionOperation && IsParamsCollection(context.Operation))
            return;

        IEnumerable<IOperation>? values = context.Operation switch
        {
            IConditionalOperation { WhenFalse: not null } conditional when conditional.Syntax is ConditionalExpressionSyntax => [conditional.WhenTrue, conditional.WhenFalse],
            ICoalesceOperation coalesce => [coalesce.Value, coalesce.WhenNull],
            ISwitchExpressionOperation switchExpression => switchExpression.Arms.Select(arm => arm.Value),
            IArrayInitializerOperation arrayInitializer => arrayInitializer.ElementValues,
            ICollectionExpressionOperation collectionExpression => collectionExpression.Elements,
            _ => null,
        };

        if (values is null || context.Operation is IArrayInitializerOperation or ICollectionExpressionOperation && HasHeterogeneousElements(values))
            return;

        ReportCombinedValues(context, resolver, values);
    }

    /// <summary>
    /// Returns whether the elements of a collection are typed <see langword="object"/> or <see langword="dynamic"/>, such as the elements of an <c>object[]</c>,
    /// which hold values of different kinds by design.
    /// </summary>
    private static bool HasHeterogeneousElements(IEnumerable<IOperation> values)
    {
        var type = values.FirstOrDefault() switch
        {
            ISpreadOperation spread => spread.ElementType,
            { } value => value.Type,
            null => null,
        };

        return type is { SpecialType: SpecialType.System_Object } or { TypeKind: TypeKind.Dynamic };
    }

    /// <summary>
    /// Reports <c>new List&lt;Guid&gt; { orderId, projectId }</c>, like the elements of an array.
    /// </summary>
    private static void AnalyzeCollectionInitializer(OperationAnalysisContext context, TagResolver resolver, IObjectOrCollectionInitializerOperation initializer)
    {
        // When the collection is tagged, each element is checked against its tags as an argument of Add
        if (!resolver.GetCollectionInitializerTargetTags(initializer).IsEmpty)
            return;

        var elements = new List<IOperation>();
        var keys = new List<IOperation>();
        var values = new List<IOperation>();
        TagResolver.GetCollectionInitializerElements(initializer, elements, keys, values);
        List<IOperation>[] groups = [elements, keys, values];
        foreach (var group in groups)
        {
            if (group.Count > 1 && !HasHeterogeneousElements(group))
            {
                ReportCombinedValues(context, resolver, group);
            }
        }
    }

    private static void ReportCombinedValues(OperationAnalysisContext context, TagResolver resolver, IEnumerable<IOperation> values)
    {
        if (resolver.TryFindIncompatibleValues(values, out var first, out var firstTags, out var second, out var secondTags))
        {
            context.ReportDiagnostic(CombinedValues, second.Syntax, ValueTagDescriptions.Describe(second), secondTags.ToAttributeString(), ValueTagDescriptions.Describe(first), firstTags.ToAttributeString());
            return;
        }

        if (!resolver.IsStrictModeEnabled(context.Operation.Syntax.SyntaxTree))
            return;

        // Strict mode: existing untagged values combined with tagged values
        var taggedValues = values.Select(value => (Value: value, Tags: resolver.GetTag(value))).ToArray();
        var tagged = taggedValues.FirstOrDefault(item => !item.Tags.IsEmpty);
        if (tagged.Value is null)
            return;

        foreach (var (value, tags) in taggedValues)
        {
            if (tags.IsEmpty && IsReportableUntaggedValue(value, resolver) && !IsNewValue(value, resolver))
            {
                ReportUntaggedValue(
                    context,
                    resolver,
                    value.Syntax,
                    ValueTagDescriptions.Describe(value) + " is not tagged but " + ValueTagDescriptions.Describe(tagged.Value) + " is " + tagged.Tags.ToAttributeString(),
                    value,
                    tagged.Tags);
            }
        }
    }

    /// <summary>
    /// Returns whether strict mode accepts an untagged value anywhere: a default value, <see langword="null"/>, a constant, <c>Guid.Empty</c>,
    /// <c>string.Empty</c>, or a parameterless struct creation such as <c>new Guid()</c>.
    /// </summary>
    private static bool IsNeutralValue(IOperation operation, TagResolver resolver)
    {
        operation = operation.UnwrapConversions();
        return operation.ConstantValue.HasValue ||
            operation is IDefaultValueOperation ||
            operation is IObjectCreationOperation { Arguments.Length: 0, Initializer: null, Type.IsValueType: true } ||
            (operation is IFieldReferenceOperation { Field: var field } && (resolver.KnownTypes.IsGuidEmpty(field) || field is { Name: "Empty", IsStatic: true, ContainingType.SpecialType: SpecialType.System_String }));
    }

    /// <summary>
    /// Returns whether strict mode reports an untagged value mixed with a tagged value. Neutral values are not reported, and neither are values
    /// that cannot be tagged, such as an element of a tuple, nor values whose parts have incompatible tags, which are reported as combined values (MFTV0003).
    /// </summary>
    private static bool IsReportableUntaggedValue(IOperation operation, TagResolver resolver)
    {
        if (IsNeutralValue(operation, resolver))
            return false;

        var unwrapped = operation.UnwrapConversions();
        if (unwrapped is IFieldReferenceOperation { Field.ContainingType.IsTupleType: true })
            return false;

        IEnumerable<IOperation>? parts = unwrapped switch
        {
            IConditionalOperation { WhenFalse: not null } conditional => [conditional.WhenTrue, conditional.WhenFalse],
            ICoalesceOperation coalesce => [coalesce.Value, coalesce.WhenNull],
            ISwitchExpressionOperation switchExpression => switchExpression.Arms.Select(arm => arm.Value),
            IBinaryOperation { OperatorKind: BinaryOperatorKind.Add or BinaryOperatorKind.Subtract } binary when TagResolver.IsBuiltInNumericOperator(binary.OperatorMethod, binary.Type) => [binary.LeftOperand, binary.RightOperand],
            _ => null,
        };

        return parts is null || !resolver.TryFindIncompatibleValues(parts, out _, out _, out _, out _);
    }

    /// <summary>
    /// Returns whether an untagged value is a new value, which strict mode accepts in a tagged declaration, such as <c>Guid.NewGuid()</c>,
    /// <c>new Guid(bytes)</c>, or <c>Guid.Parse(text)</c>. Reading an untagged field, property, parameter, local, or array element, or calling an untagged method
    /// declared in the compilation, does not create a new value. A conditional, a null-coalescing, or a switch expression creates a new value only when
    /// each of its branches does.
    /// </summary>
    private static bool IsNewValue(IOperation operation, TagResolver resolver)
    {
        return operation.UnwrapConversions() switch
        {
            IFieldReferenceOperation or IPropertyReferenceOperation or IParameterReferenceOperation or ILocalReferenceOperation or IArrayElementReferenceOperation => false,
            IAwaitOperation awaitOperation => IsNewValue(awaitOperation.Operation, resolver),
            IInvocationOperation invocation => !resolver.IsDeclaredInCompilation(invocation.TargetMethod.OriginalDefinition),
            IConditionalAccessOperation conditionalAccess => IsNewValue(conditionalAccess.WhenNotNull, resolver),
            IConditionalOperation { WhenFalse: not null } conditional => IsNewOrNeutralValue(conditional.WhenTrue) && IsNewOrNeutralValue(conditional.WhenFalse),
            ICoalesceOperation coalesce => IsNewOrNeutralValue(coalesce.Value) && IsNewOrNeutralValue(coalesce.WhenNull),
            ISwitchExpressionOperation switchExpression => switchExpression.Arms.All(arm => IsNewOrNeutralValue(arm.Value)),
            _ => true,
        };

        bool IsNewOrNeutralValue(IOperation branch) => IsNeutralValue(branch, resolver) || IsNewValue(branch, resolver);
    }

    /// <summary>
    /// Returns whether a declaration of the compilation can carry a <c>[ValueTag]</c>: it is written in the source of the compilation, it is not an element of a tuple,
    /// and its type, or the type of its elements, is not <see langword="object"/>, <see langword="dynamic"/>, or a type parameter.
    /// </summary>
    private static bool CanDeclareTag(ISymbol symbol, TagResolver resolver)
    {
        if (symbol.IsImplicitlyDeclared || resolver.GetLocationInCompilation(symbol) is null || symbol.ContainingType is { IsAnonymousType: true } || symbol is IFieldSymbol { ContainingType.IsTupleType: true })
            return false;

        var type = symbol switch
        {
            IFieldSymbol field => field.Type,
            IPropertySymbol property => property.Type,
            IParameterSymbol { ContainingSymbol: IMethodSymbol { MethodKind: not MethodKind.AnonymousFunction } } parameter => parameter.Type,
            IMethodSymbol { ReturnsVoid: false, MethodKind: MethodKind.Ordinary or MethodKind.LocalFunction or MethodKind.ExplicitInterfaceImplementation } method => method.ReturnType,
            ILocalSymbol local => local.Type,
            _ => null,
        };

        if (type is null || TagResolver.ContainsTypeParameter(type))
            return false;

        // params object[] args holds values of any kind
        for (var depth = 0; depth < 8; depth++)
        {
            if (type.SpecialType is SpecialType.System_Object || type.TypeKind is TypeKind.Dynamic)
                return false;

            if (type is IArrayTypeSymbol arrayType)
            {
                type = arrayType.ElementType;
            }
            else if (type is INamedTypeSymbol namedType && resolver.KnownTypes.IsCollectionType(namedType) && resolver.KnownTypes.TryGetWrappedType(namedType, out var elementType))
            {
                type = elementType;
            }
            else
            {
                break;
            }
        }

        return true;
    }

    private static void ReportUntaggedValue(OperationAnalysisContext context, TagResolver resolver, SyntaxNode reportNode, string message, IOperation untaggedValue, TagInfo tags)
    {
        var operation = untaggedValue.UnwrapImplicitConversions();
        if (operation is IAwaitOperation awaitOperation)
        {
            operation = awaitOperation.Operation.UnwrapImplicitConversions();
        }

        var (symbol, targetKind) = operation switch
        {
            IFieldReferenceOperation fieldReference => (fieldReference.Field.OriginalDefinition, ValueTagTargetKind.Symbol),
            IPropertyReferenceOperation propertyReference => (propertyReference.Property.OriginalDefinition, ValueTagTargetKind.Symbol),
            IParameterReferenceOperation parameterReference => (parameterReference.Parameter.OriginalDefinition, ValueTagTargetKind.Symbol),
            IInvocationOperation invocation => (invocation.TargetMethod.OriginalDefinition, ValueTagTargetKind.ReturnValue),
            _ => ((ISymbol?)null, (string?)null),
        };

        ReportUntaggedValue(context, resolver, reportNode, message, symbol, tags, targetKind);
    }

    /// <summary>
    /// Reports MFTV0009. The code fix adds <paramref name="tags"/> to <paramref name="untaggedDeclaration"/>.
    /// </summary>
    private static void ReportUntaggedValue(OperationAnalysisContext context, TagResolver resolver, SyntaxNode reportNode, string message, ISymbol? untaggedDeclaration, TagInfo tags, string? targetKind)
    {
        var properties = ImmutableDictionary<string, string?>.Empty;
        var additionalLocations = new List<Location>();
        if (untaggedDeclaration is not null && targetKind is not null && tags.IsExplicit && CanDeclareTag(untaggedDeclaration, resolver) &&
            resolver.GetLocationInCompilation(untaggedDeclaration) is { } location)
        {
            properties = properties
                .Add(ValueTagDiagnostics.TagsProperty, tags.Serialize())
                .Add(ValueTagDiagnostics.TargetKindProperty, ValueTagTargetKind.ForSymbol(untaggedDeclaration, targetKind));
            additionalLocations.Add(location);
        }

        context.ReportDiagnostic(Diagnostic.Create(UntaggedValue, reportNode.GetLocation(), additionalLocations, properties, message));
    }

    private static void AnalyzeAttribute(OperationAnalysisContext context, TagResolver resolver)
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

        // [field: ValueTag] on a property tags its backing field, which is never read directly
        if (attributeList.Target?.Identifier.IsKind(SyntaxKind.FieldKeyword) is true && attributeList.Parent is PropertyDeclarationSyntax or ParameterSyntax)
        {
            ReportInvalidAttribute(attributeList.Parent is ParameterSyntax
                ? "[field: ValueTag] tags the backing field of the property, which is not analyzed; use [ValueTag] or [property: ValueTag] to tag the property"
                : "[field: ValueTag] tags the backing field of the property, which is not analyzed; use [ValueTag] to tag the property", removable: false);
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

        var isKeyValueShaped = resolver.KnownTypes.IsKeyValueShaped(annotatedType);
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
            _ => null,
        };
    }

    private static void AnalyzeComments(SemanticModelAnalysisContext context, TagResolver resolver)
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
            else if (TypeArgumentComments.TryValidate(trivia, tags, context.SemanticModel, resolver.KnownTypes, context.CancellationToken, out var typeArgumentError))
            {
                if (typeArgumentError is null)
                    continue;

                message = typeArgumentError;
            }
            else if (GetLocalDeclarations(trivia) is not { Count: > 0 } declarations)
            {
                message = isLineComment
                    ? "A // ValueTag comment only applies to the local variables declared by the statement that follows it; use the [ValueTag] attribute on fields, properties, parameters, and return values"
                    : "A value tag comment only applies to the declaration of a local variable; use the [ValueTag] attribute on fields, properties, parameters, and return values";
            }
            else if (GetOverridingComment(trivia, tags, declarations) is { } overridingComment)
            {
                message = "'" + text + "' does not tag anything, as '" + overridingComment + "' tags the same variables; remove one of the comments";
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

    /// <summary>
    /// Returns the comment that wins over <paramref name="trivia"/> for every variable it could tag, when their tags differ,
    /// e.g. the second comment of <c>/* ValueTag=OrderId */ Guid /* ValueTag=ProjectId */ id</c>.
    /// </summary>
    private static string? GetOverridingComment(SyntaxTrivia trivia, TagInfo tags, List<SyntaxNode> declarations)
    {
        string? result = null;
        foreach (var declaration in declarations)
        {
            foreach (var candidate in TagResolver.GetLocalCommentTrivia(declaration))
            {
                if (ValueTagComment.Parse(candidate.ToString(), out var candidateTags) is not ValueTagCommentKind.Valid)
                    continue;

                // The comment tags this variable
                if (candidate == trivia)
                    return null;

                if (candidateTags.TagsEqual(tags))
                    return null;

                result ??= candidate.ToString();
                break;
            }
        }

        return result;
    }

    /// <summary>
    /// Returns the declarations of the local variables a comment can tag.
    /// </summary>
    private static List<SyntaxNode> GetLocalDeclarations(SyntaxTrivia trivia)
    {
        var result = new List<SyntaxNode>();
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
                if (!result.Contains(declaration) && TagResolver.GetLocalCommentTrivia(declaration).Contains(trivia))
                {
                    result.Add(declaration);
                }
            }

            if (node is StatementSyntax or MemberDeclarationSyntax)
                return result;
        }

        return result;
    }

    private static void AnalyzeSymbol(SymbolAnalysisContext context, TagResolver resolver, ConcurrentQueue<ISymbol> conventionIdMembers)
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
                foreach (var parameter in property.Parameters)
                {
                    ReportInheritedMismatch(context, parameter, ValueTagTargetKind.Symbol);
                }

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

        // record Order([ValueTag("ProjectId")] Guid Id) : IHasId: the parameter tags the property
        if (ownTags.IsEmpty && symbol is IPropertySymbol property && TagResolver.IsRecordPrimaryConstructorProperty(property))
        {
            ownTags = TagResolver.GetRecordPrimaryConstructorParameterTags(property);
        }

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
                .Add(ValueTagDiagnostics.TargetKindProperty, ValueTagTargetKind.ForSymbol(symbol, targetKind));

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

    /// <summary>
    /// Reports <c>class Derived : Base, IRepository</c> when <c>Base.Load</c> implements <c>IRepository.Load</c> with other tags. The member is declared
    /// in the base type, which does not know the interface, so it is reported on the type that implements the interface.
    /// </summary>
    private static void AnalyzeInheritedInterfaceImplementations(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct) || type.Interfaces.IsEmpty)
            return;

        var location = type.Locations.FirstOrDefault(location => location.IsInSource);
        if (location is null)
            return;

        foreach (var @interface in type.AllInterfaces)
        {
            foreach (var interfaceMember in @interface.GetMembers())
            {
                if (interfaceMember is not (IMethodSymbol { MethodKind: MethodKind.Ordinary } or IPropertySymbol) ||
                    type.FindImplementationForInterfaceMember(interfaceMember) is not { } implementation ||
                    SymbolEqualityComparer.Default.Equals(implementation.ContainingType, type) ||
                    implementation.ContainingType.AllInterfaces.Contains(@interface, SymbolEqualityComparer.Default))
                {
                    continue;
                }

                foreach (var (implementationPart, interfacePart) in GetTaggedParts(implementation, interfaceMember))
                {
                    var ownTags = TagResolver.GetOwnExplicitTags(implementationPart);
                    var interfaceTags = TagResolver.GetExplicitAndInheritedTags(interfacePart);
                    if (ownTags.IsEmpty || interfaceTags.IsEmpty || TagInfo.AreCompatible(ownTags, interfaceTags))
                        continue;

                    context.ReportDiagnostic(Diagnostic.Create(
                        InheritedTagMismatch,
                        location,
                        ValueTagDescriptions.DescribeSymbol(implementationPart),
                        ownTags.ToAttributeString(),
                        ValueTagDescriptions.DescribeSymbol(interfacePart) + ", which it implements for '" + type.Name + "',",
                        interfaceTags.ToAttributeString()));
                    return;
                }
            }
        }

        static IEnumerable<(ISymbol Implementation, ISymbol Interface)> GetTaggedParts(ISymbol implementation, ISymbol interfaceMember)
        {
            yield return (implementation, interfaceMember);

            var (implementationParameters, interfaceParameters) = (implementation, interfaceMember) switch
            {
                (IMethodSymbol method, IMethodSymbol interfaceMethod) => (method.Parameters, interfaceMethod.Parameters),
                (IPropertySymbol property, IPropertySymbol interfaceProperty) => (property.Parameters, interfaceProperty.Parameters),
                _ => ([], []),
            };

            for (var i = 0; i < implementationParameters.Length && i < interfaceParameters.Length; i++)
            {
                yield return (implementationParameters[i], interfaceParameters[i]);
            }
        }
    }

    private static void ReportRedundantTag(SymbolAnalysisContext context, TagResolver resolver, ISymbol symbol)
    {
        // Without the attribute, the tags of the overridden or implemented member would win over the naming convention
        foreach (var baseSymbol in TagResolver.GetOverriddenOrImplementedSymbols(symbol))
        {
            if (!TagResolver.GetExplicitAndInheritedTags(baseSymbol).IsEmpty)
                return;
        }

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

    private static void CollectConventionIdMember(TagResolver resolver, ISymbol symbol, ConcurrentQueue<ISymbol> conventionIdMembers)
    {
        if (symbol.IsOverride || !TagResolver.IsConventionIdName(symbol) || !TagResolver.GetExplicitAndInheritedTags(symbol).IsEmpty)
            return;

        if (!resolver.GetConventionTags(symbol).IsEmpty)
        {
            conventionIdMembers.Enqueue(symbol);
        }
    }

    private static void ReportAmbiguousConventions(CompilationAnalysisContext context, TagResolver resolver, ConcurrentQueue<ISymbol> conventionIdMembers)
    {
        // The members are collected concurrently, so they are sorted for the messages not to depend on the order of the analysis
        var sortedMembers = conventionIdMembers
            .OrderBy(member => member.ToDisplayString(), StringComparer.Ordinal)
            .ThenBy(member => member.Locations.FirstOrDefault()?.SourceTree?.FilePath, StringComparer.Ordinal)
            .ThenBy(member => member.Locations.FirstOrDefault()?.SourceSpan.Start);

        foreach (var group in sortedMembers.GroupBy(member => member.ContainingType.Name, StringComparer.Ordinal))
        {
            var members = group.ToArray();
            if (members.Select(member => member.ContainingType).Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default).Count() < 2)
                continue;

            foreach (var member in members)
            {
                var other = members.First(candidate => !SymbolEqualityComparer.Default.Equals(candidate.ContainingType, member.ContainingType));
                var location = resolver.GetLocationInCompilation(member);
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
