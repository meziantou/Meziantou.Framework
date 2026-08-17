using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.Assertions;

/// <summary>
/// Detects <c>Assert.True(condition)</c> / <c>Assert.False(condition)</c> calls whose condition can be expressed
/// with a dedicated assertion method. Unlike <see cref="TrueFalseConditionMethodSelectionAnalyzerCommon"/>, the
/// condition is not required to be an invocation, so operators and patterns are supported too.
/// </summary>
internal static class ConditionRewriteAnalyzerCommon
{
    private const string EqualAssertionMethodName = "Equal";
    private const string NotEqualAssertionMethodName = "NotEqual";
    private const string SameAssertionMethodName = "Same";
    private const string NotSameAssertionMethodName = "NotSame";
    private const string InRangeAssertionMethodName = "InRange";
    private const string NotInRangeAssertionMethodName = "NotInRange";
    private const string ProperSubsetAssertionMethodName = "ProperSubset";
    private const string NotProperSubsetAssertionMethodName = "NotProperSubset";
    private const string ProperSupersetAssertionMethodName = "ProperSuperset";
    private const string NotProperSupersetAssertionMethodName = "NotProperSuperset";
    private const string IsTypeAssertionMethodName = "IsType";
    private const string IsNotTypeAssertionMethodName = "IsNotType";
    private const string TrueAssertionMethodName = "True";
    private const string FalseAssertionMethodName = "False";

    internal static bool TryCreateSymbols(Compilation compilation, [NotNullWhen(true)] out Symbols? symbols)
    {
        var enumerableType = compilation.GetTypeByMetadataName("System.Linq.Enumerable");
        var setType = compilation.GetTypeByMetadataName("System.Collections.Generic.ISet`1");
        var nonGenericEnumerableType = compilation.GetTypeByMetadataName("System.Collections.IEnumerable");
        var comparableType = compilation.GetTypeByMetadataName("System.IComparable`1");
        if (enumerableType is null || setType is null || nonGenericEnumerableType is null || comparableType is null)
        {
            symbols = null;
            return false;
        }

        if (!CountAssertionAnalyzerCommon.TryCreateSymbols(compilation, out var countSymbols))
        {
            symbols = null;
            return false;
        }

        var objectType = compilation.GetSpecialType(SpecialType.System_Object);
        var stringType = compilation.GetSpecialType(SpecialType.System_String);

        var sequenceEqualMethods = enumerableType.GetMembers("SequenceEqual")
            .OfType<IMethodSymbol>()
            .Where(m => m is { IsStatic: true, IsExtensionMethod: true, Parameters.Length: 2 })
            .Select(m => m.OriginalDefinition)
            .ToImmutableArray();

        var objectReferenceEqualsMethod = objectType.GetMembers("ReferenceEquals")
            .OfType<IMethodSymbol>()
            .FirstOrDefault(m => m is { IsStatic: true, Parameters.Length: 2 });

        var objectStaticEqualsMethod = objectType.GetMembers("Equals")
            .OfType<IMethodSymbol>()
            .FirstOrDefault(m => m is { IsStatic: true, Parameters.Length: 2 });

        var objectGetTypeMethod = objectType.GetMembers("GetType")
            .OfType<IMethodSymbol>()
            .FirstOrDefault(m => m is { IsStatic: false, Parameters.Length: 0 });

        var stringStaticEqualsMethods = stringType.GetMembers("Equals")
            .OfType<IMethodSymbol>()
            .Where(m => m.IsStatic && m.Parameters.Length is 2 or 3)
            .ToImmutableArray();

        var setProperSubsetMethod = setType.GetMembers("IsProperSubsetOf").OfType<IMethodSymbol>().FirstOrDefault();
        var setProperSupersetMethod = setType.GetMembers("IsProperSupersetOf").OfType<IMethodSymbol>().FirstOrDefault();

        if (objectReferenceEqualsMethod is null || objectStaticEqualsMethod is null || objectGetTypeMethod is null ||
            setProperSubsetMethod is null || setProperSupersetMethod is null)
        {
            symbols = null;
            return false;
        }

        symbols = new Symbols(
            countSymbols,
            sequenceEqualMethods,
            objectReferenceEqualsMethod,
            objectStaticEqualsMethod,
            objectGetTypeMethod,
            stringStaticEqualsMethods,
            setProperSubsetMethod,
            setProperSupersetMethod,
            nonGenericEnumerableType,
            comparableType);
        return true;
    }

    // ---------------------------------------------------------------------------------------------------------
    // Assert.True(a == b) -> Assert.Equal(b, a)
    // ---------------------------------------------------------------------------------------------------------
    internal static bool TryGetEqualityOperatorMatch(IInvocationOperation assertInvocation, INamedTypeSymbol assertType, Symbols symbols, out ConditionRewriteMatch match)
    {
        if (!TryGetCondition(assertInvocation, assertType, out var conditionOperation, out var conditionExpectedToBeFalse))
        {
            match = default;
            return false;
        }

        if (conditionOperation is not IBinaryOperation { OperatorMethod: null } binaryOperation ||
            binaryOperation.OperatorKind is not (BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals))
        {
            match = default;
            return false;
        }

        // Count comparisons belong to the count assertion rules
        if (CountAssertionAnalyzerCommon.TryGetAssertionMatch(assertInvocation, assertType, symbols.CountSymbols, out _))
        {
            match = default;
            return false;
        }

        var left = AssertionsAnalyzerHelpers.UnwrapImplicitConversion(binaryOperation.LeftOperand);
        var right = AssertionsAnalyzerHelpers.UnwrapImplicitConversion(binaryOperation.RightOperand);

        // Null comparisons belong to the null assertion rules
        if (IsNullConstant(left) || IsNullConstant(right))
        {
            match = default;
            return false;
        }

        if (!HasValueEqualitySemantics(left.Type) || !HasValueEqualitySemantics(right.Type))
        {
            match = default;
            return false;
        }

        var negated = binaryOperation.OperatorKind == BinaryOperatorKind.NotEquals ^ conditionExpectedToBeFalse;
        var assertionMethodName = negated ? NotEqualAssertionMethodName : EqualAssertionMethodName;

        // Assert.Equal takes the expected value first, so a constant operand becomes the expected value
        var (expected, actual) = AssertionArgumentOrderAnalyzerCommon.IsConstantOrCollectionContainingConstant(right) &&
                                 !AssertionArgumentOrderAnalyzerCommon.IsConstantOrCollectionContainingConstant(left)
            ? (right, left)
            : (left, right);

        match = new ConditionRewriteMatch(binaryOperation, assertionMethodName, [expected, actual]);
        return true;
    }

    // ---------------------------------------------------------------------------------------------------------
    // Assert.True(a.Equals(b)) / object.Equals(a, b) / string.Equals(a, b, comparison) -> Assert.Equal
    // ---------------------------------------------------------------------------------------------------------
    internal static bool TryGetEqualsMethodMatch(IInvocationOperation assertInvocation, INamedTypeSymbol assertType, Symbols symbols, out ConditionRewriteMatch match)
    {
        if (!TryGetCondition(assertInvocation, assertType, out var conditionOperation, out var conditionExpectedToBeFalse) ||
            conditionOperation is not IInvocationOperation { TargetMethod.Name: "Equals" } invocation)
        {
            match = default;
            return false;
        }

        var assertionMethodName = conditionExpectedToBeFalse ? NotEqualAssertionMethodName : EqualAssertionMethodName;

        // string.Equals(a, b) / string.Equals(a, b, StringComparison)
        if (symbols.StringStaticEqualsMethods.Contains(invocation.TargetMethod, SymbolEqualityComparer.Default))
        {
            if (invocation.Arguments.Length < 2)
            {
                match = default;
                return false;
            }

            var expectedOperand = AssertionsAnalyzerHelpers.UnwrapImplicitConversion(invocation.Arguments[0].Value);
            var actualOperand = AssertionsAnalyzerHelpers.UnwrapImplicitConversion(invocation.Arguments[1].Value);
            if (invocation.Arguments.Length == 2)
            {
                match = new ConditionRewriteMatch(invocation, assertionMethodName, [expectedOperand, actualOperand]);
                return true;
            }

            if (!TryGetIgnoreCase(invocation.Arguments[2], out var stringIgnoreCase))
            {
                match = default;
                return false;
            }

            match = new ConditionRewriteMatch(invocation, assertionMethodName, [expectedOperand, actualOperand], IgnoreCaseValue: stringIgnoreCase ? true : null);
            return true;
        }

        // object.Equals(a, b)
        if (SymbolEqualityComparer.Default.Equals(invocation.TargetMethod, symbols.ObjectStaticEqualsMethod) && invocation.Arguments.Length == 2)
        {
            var expectedOperand = AssertionsAnalyzerHelpers.UnwrapImplicitConversion(invocation.Arguments[0].Value);
            var actualOperand = AssertionsAnalyzerHelpers.UnwrapImplicitConversion(invocation.Arguments[1].Value);
            if (MayBeComparedAsSequence(expectedOperand.Type, symbols) || MayBeComparedAsSequence(actualOperand.Type, symbols))
            {
                match = default;
                return false;
            }

            match = new ConditionRewriteMatch(invocation, assertionMethodName, [expectedOperand, actualOperand]);
            return true;
        }

        // instance a.Equals(b) — the receiver is the actual value
        if (invocation is { Instance: { } instance, Arguments.Length: 1 } && !invocation.TargetMethod.IsStatic)
        {
            var actualOperand = AssertionsAnalyzerHelpers.UnwrapImplicitConversion(instance);
            var expectedOperand = AssertionsAnalyzerHelpers.UnwrapImplicitConversion(invocation.Arguments[0].Value);

            // Assert.Equal compares sequences element by element, which Equals does not
            if (MayBeComparedAsSequence(actualOperand.Type, symbols) || MayBeComparedAsSequence(expectedOperand.Type, symbols))
            {
                match = default;
                return false;
            }

            match = new ConditionRewriteMatch(invocation, assertionMethodName, [expectedOperand, actualOperand]);
            return true;
        }

        // instance a.Equals(b, StringComparison)
        if (invocation is { Instance: { } stringInstance, Arguments.Length: 2 } &&
            !invocation.TargetMethod.IsStatic &&
            stringInstance.Type?.SpecialType == SpecialType.System_String)
        {
            if (!TryGetIgnoreCase(invocation.Arguments[1], out var ignoreCase))
            {
                match = default;
                return false;
            }

            var actualOperand = AssertionsAnalyzerHelpers.UnwrapImplicitConversion(stringInstance);
            var expectedOperand = AssertionsAnalyzerHelpers.UnwrapImplicitConversion(invocation.Arguments[0].Value);
            match = new ConditionRewriteMatch(invocation, assertionMethodName, [expectedOperand, actualOperand], IgnoreCaseValue: ignoreCase ? true : null);
            return true;
        }

        match = default;
        return false;
    }

    // ---------------------------------------------------------------------------------------------------------
    // Assert.True(actual.SequenceEqual(expected)) -> Assert.Equal(expected, actual)
    // ---------------------------------------------------------------------------------------------------------
    internal static bool TryGetSequenceEqualMatch(IInvocationOperation assertInvocation, INamedTypeSymbol assertType, Symbols symbols, out ConditionRewriteMatch match)
    {
        if (!TryGetCondition(assertInvocation, assertType, out var conditionOperation, out var conditionExpectedToBeFalse) ||
            conditionOperation is not IInvocationOperation { TargetMethod.Name: "SequenceEqual" } invocation)
        {
            match = default;
            return false;
        }

        var originalDefinition = (invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod).OriginalDefinition;
        if (!symbols.SequenceEqualMethods.Contains(originalDefinition, SymbolEqualityComparer.Default))
        {
            match = default;
            return false;
        }

        IOperation actualOperand;
        IOperation expectedOperand;
        if (invocation.Instance is not null)
        {
            actualOperand = AssertionsAnalyzerHelpers.UnwrapImplicitConversion(invocation.Instance);
            expectedOperand = AssertionsAnalyzerHelpers.UnwrapImplicitConversion(invocation.Arguments[0].Value);
        }
        else
        {
            var firstArgument = invocation.Arguments.FirstOrDefault(a => a.Parameter?.Name == "first");
            var secondArgument = invocation.Arguments.FirstOrDefault(a => a.Parameter?.Name == "second");
            if (firstArgument is null || secondArgument is null)
            {
                match = default;
                return false;
            }

            actualOperand = AssertionsAnalyzerHelpers.UnwrapImplicitConversion(firstArgument.Value);
            expectedOperand = AssertionsAnalyzerHelpers.UnwrapImplicitConversion(secondArgument.Value);
        }

        var assertionMethodName = conditionExpectedToBeFalse ? NotEqualAssertionMethodName : EqualAssertionMethodName;
        match = new ConditionRewriteMatch(invocation, assertionMethodName, [expectedOperand, actualOperand]);
        return true;
    }

    // ---------------------------------------------------------------------------------------------------------
    // Assert.True(ReferenceEquals(a, b)) -> Assert.Same(a, b)
    // ---------------------------------------------------------------------------------------------------------
    internal static bool TryGetReferenceEqualsMatch(IInvocationOperation assertInvocation, INamedTypeSymbol assertType, Symbols symbols, out ConditionRewriteMatch match)
    {
        if (!TryGetCondition(assertInvocation, assertType, out var conditionOperation, out var conditionExpectedToBeFalse) ||
            conditionOperation is not IInvocationOperation { TargetMethod.Name: "ReferenceEquals" } invocation ||
            !SymbolEqualityComparer.Default.Equals(invocation.TargetMethod, symbols.ObjectReferenceEqualsMethod) ||
            invocation.Arguments.Length != 2)
        {
            match = default;
            return false;
        }

        var expectedOperand = AssertionsAnalyzerHelpers.UnwrapImplicitConversion(invocation.Arguments[0].Value);
        var actualOperand = AssertionsAnalyzerHelpers.UnwrapImplicitConversion(invocation.Arguments[1].Value);

        // Assert.Same reports an error for value types (MFAS0010/MFAS0011)
        if (AssertionsAnalyzerHelpers.IsValueType(expectedOperand.Type) || AssertionsAnalyzerHelpers.IsValueType(actualOperand.Type))
        {
            match = default;
            return false;
        }

        var assertionMethodName = conditionExpectedToBeFalse ? NotSameAssertionMethodName : SameAssertionMethodName;
        match = new ConditionRewriteMatch(invocation, assertionMethodName, [expectedOperand, actualOperand]);
        return true;
    }

    // ---------------------------------------------------------------------------------------------------------
    // Assert.True(low <= x && x <= high) -> Assert.InRange(x, low, high)
    // ---------------------------------------------------------------------------------------------------------
    internal static bool TryGetRangeMatch(IInvocationOperation assertInvocation, INamedTypeSymbol assertType, Symbols symbols, out ConditionRewriteMatch match)
    {
        if (!TryGetCondition(assertInvocation, assertType, out var conditionOperation, out var conditionExpectedToBeFalse) ||
            conditionOperation is not IBinaryOperation { OperatorKind: BinaryOperatorKind.ConditionalAnd } andOperation)
        {
            match = default;
            return false;
        }

        if (!TryGetComparison(andOperation.LeftOperand, symbols, out var first) ||
            !TryGetComparison(andOperation.RightOperand, symbols, out var second))
        {
            match = default;
            return false;
        }

        // The value under test is the operand the two comparisons have in common, which works whether the bounds
        // are constants or variables and whichever side of each comparison the value is written on
        if (!TryGetSharedValue(first, second, out var value, out var lowOperand, out var highOperand))
        {
            match = default;
            return false;
        }

        var assertionMethodName = conditionExpectedToBeFalse ? NotInRangeAssertionMethodName : InRangeAssertionMethodName;
        match = new ConditionRewriteMatch(andOperation, assertionMethodName, [value, lowOperand, highOperand]);
        return true;
    }

    private static bool TryGetComparison(IOperation operation, Symbols symbols, out Comparison comparison)
    {
        operation = AssertionsAnalyzerHelpers.UnwrapImplicitConversion(operation);

        // Assert.InRange is inclusive on both ends, so strict comparisons are not equivalent
        if (operation is IBinaryOperation binaryOperation &&
            binaryOperation.OperatorKind is BinaryOperatorKind.GreaterThanOrEqual or BinaryOperatorKind.LessThanOrEqual &&
            IsComparableWithDefaultComparer(binaryOperation, symbols))
        {
            comparison = new Comparison(
                AssertionsAnalyzerHelpers.UnwrapImplicitConversion(binaryOperation.LeftOperand),
                AssertionsAnalyzerHelpers.UnwrapImplicitConversion(binaryOperation.RightOperand),
                binaryOperation.OperatorKind);
            return true;
        }

        comparison = default;
        return false;
    }

    private static bool TryGetSharedValue(Comparison first, Comparison second, out IOperation value, out IOperation lowOperand, out IOperation highOperand)
    {
        foreach (var valueOnFirstLeft in Booleans)
        {
            var candidate = valueOnFirstLeft ? first.Left : first.Right;
            foreach (var valueOnSecondLeft in Booleans)
            {
                var other = valueOnSecondLeft ? second.Left : second.Right;
                if (!SyntaxFactory.AreEquivalent(candidate.Syntax, other.Syntax))
                    continue;

                var firstIsLowerBound = IsLowerBound(first.OperatorKind, valueOnFirstLeft);
                var secondIsLowerBound = IsLowerBound(second.OperatorKind, valueOnSecondLeft);

                // One comparison must give the lower bound and the other the upper bound
                if (firstIsLowerBound == secondIsLowerBound)
                    continue;

                var firstBound = valueOnFirstLeft ? first.Right : first.Left;
                var secondBound = valueOnSecondLeft ? second.Right : second.Left;

                value = candidate;
                lowOperand = firstIsLowerBound ? firstBound : secondBound;
                highOperand = firstIsLowerBound ? secondBound : firstBound;
                return true;
            }
        }

        value = null!;
        lowOperand = null!;
        highOperand = null!;
        return false;
    }

    /// <summary>'value &gt;= bound' and 'bound &lt;= value' both express a lower bound.</summary>
    private static bool IsLowerBound(BinaryOperatorKind operatorKind, bool valueOnLeft)
        => valueOnLeft
            ? operatorKind == BinaryOperatorKind.GreaterThanOrEqual
            : operatorKind == BinaryOperatorKind.LessThanOrEqual;

    private static readonly bool[] Booleans = [true, false];

    private readonly record struct Comparison(IOperation Left, IOperation Right, BinaryOperatorKind OperatorKind);

    private static bool IsComparableWithDefaultComparer(IBinaryOperation binaryOperation, Symbols symbols)
    {
        // Built-in relational operators always agree with Comparer<T>.Default
        if (binaryOperation.OperatorMethod is null)
            return true;

        var type = AssertionsAnalyzerHelpers.UnwrapImplicitConversion(binaryOperation.LeftOperand).Type;
        if (type is null)
            return false;

        foreach (var interfaceType in type.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(interfaceType.OriginalDefinition, symbols.ComparableType) &&
                interfaceType.TypeArguments is [var typeArgument] &&
                SymbolEqualityComparer.Default.Equals(typeArgument, type))
            {
                return true;
            }
        }

        return false;
    }

    // ---------------------------------------------------------------------------------------------------------
    // Assert.True(set.IsProperSubsetOf(other)) -> Assert.ProperSubset(set, other)
    // ---------------------------------------------------------------------------------------------------------
    internal static bool TryGetSetMatch(IInvocationOperation assertInvocation, INamedTypeSymbol assertType, Symbols symbols, out ConditionRewriteMatch match)
    {
        if (!TryGetCondition(assertInvocation, assertType, out var conditionOperation, out var conditionExpectedToBeFalse) ||
            conditionOperation is not IInvocationOperation { Instance: { } instance, Arguments.Length: 1 } invocation)
        {
            match = default;
            return false;
        }

        var isSubset = invocation.TargetMethod.Name == "IsProperSubsetOf";
        var isSuperset = invocation.TargetMethod.Name == "IsProperSupersetOf";
        if (!isSubset && !isSuperset)
        {
            match = default;
            return false;
        }

        var interfaceMethod = isSubset ? symbols.SetProperSubsetMethod : symbols.SetProperSupersetMethod;
        if (!ImplementsSetMethod(invocation.TargetMethod, interfaceMethod))
        {
            match = default;
            return false;
        }

        // Assert.ProperSubset(expected, actual) asserts that 'expected' is a proper subset of 'actual',
        // which matches the receiver/argument order of ISet<T>.IsProperSubsetOf
        var assertionMethodName = (isSubset, conditionExpectedToBeFalse) switch
        {
            (true, false) => ProperSubsetAssertionMethodName,
            (true, true) => NotProperSubsetAssertionMethodName,
            (false, false) => ProperSupersetAssertionMethodName,
            (false, true) => NotProperSupersetAssertionMethodName,
        };

        match = new ConditionRewriteMatch(
            invocation,
            assertionMethodName,
            [AssertionsAnalyzerHelpers.UnwrapImplicitConversion(instance), AssertionsAnalyzerHelpers.UnwrapImplicitConversion(invocation.Arguments[0].Value)]);
        return true;
    }

    private static bool ImplementsSetMethod(IMethodSymbol method, IMethodSymbol interfaceMethodDefinition)
    {
        if (SymbolEqualityComparer.Default.Equals(method.OriginalDefinition, interfaceMethodDefinition))
            return true;

        foreach (var interfaceType in method.ContainingType.AllInterfaces)
        {
            if (!SymbolEqualityComparer.Default.Equals(interfaceType.OriginalDefinition, interfaceMethodDefinition.ContainingType))
                continue;

            var interfaceMethod = interfaceType.GetMembers(interfaceMethodDefinition.Name).OfType<IMethodSymbol>().FirstOrDefault();
            if (interfaceMethod is null)
                continue;

            if (SymbolEqualityComparer.Default.Equals(method.ContainingType.FindImplementationForInterfaceMember(interfaceMethod), method))
                return true;
        }

        return false;
    }

    // ---------------------------------------------------------------------------------------------------------
    // Assert.True(!condition) -> Assert.False(condition)
    // ---------------------------------------------------------------------------------------------------------
    internal static bool TryGetNegatedConditionMatch(IInvocationOperation assertInvocation, INamedTypeSymbol assertType, Symbols symbols, out ConditionRewriteMatch match)
    {
        _ = symbols;
        if (!TryGetCondition(assertInvocation, assertType, out var conditionOperation, out var conditionExpectedToBeFalse) ||
            conditionOperation is not IUnaryOperation { OperatorKind: UnaryOperatorKind.Not, OperatorMethod: null } unaryOperation)
        {
            match = default;
            return false;
        }

        var operand = AssertionsAnalyzerHelpers.UnwrapImplicitConversion(unaryOperation.Operand);
        if (operand.Type?.SpecialType != SpecialType.System_Boolean)
        {
            match = default;
            return false;
        }

        var assertionMethodName = conditionExpectedToBeFalse ? TrueAssertionMethodName : FalseAssertionMethodName;
        match = new ConditionRewriteMatch(unaryOperation, assertionMethodName, [operand]);
        return true;
    }

    // ---------------------------------------------------------------------------------------------------------
    // Assert.True(x.GetType() == typeof(T)) -> Assert.IsType<T>(x)
    // ---------------------------------------------------------------------------------------------------------
    internal static bool TryGetRuntimeTypeMatch(IInvocationOperation assertInvocation, INamedTypeSymbol assertType, Symbols symbols, out ConditionRewriteMatch match)
    {
        if (!TryGetCondition(assertInvocation, assertType, out var conditionOperation, out var conditionExpectedToBeFalse) ||
            conditionOperation is not IBinaryOperation binaryOperation ||
            binaryOperation.OperatorKind is not (BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals))
        {
            match = default;
            return false;
        }

        var left = AssertionsAnalyzerHelpers.UnwrapImplicitConversion(binaryOperation.LeftOperand);
        var right = AssertionsAnalyzerHelpers.UnwrapImplicitConversion(binaryOperation.RightOperand);

        if (!TryGetGetTypeReceiverAndType(left, right, symbols, out var receiver, out var type) &&
            !TryGetGetTypeReceiverAndType(right, left, symbols, out receiver, out type))
        {
            match = default;
            return false;
        }

        var negated = binaryOperation.OperatorKind == BinaryOperatorKind.NotEquals ^ conditionExpectedToBeFalse;
        var assertionMethodName = negated ? IsNotTypeAssertionMethodName : IsTypeAssertionMethodName;
        match = new ConditionRewriteMatch(binaryOperation, assertionMethodName, [receiver], TypeArgument: type);
        return true;
    }

    private static bool TryGetGetTypeReceiverAndType(IOperation getTypeCandidate, IOperation typeOfCandidate, Symbols symbols, out IOperation receiver, out ITypeSymbol type)
    {
        if (getTypeCandidate is IInvocationOperation { TargetMethod.Name: "GetType", Instance: { } instance } getTypeInvocation &&
            SymbolEqualityComparer.Default.Equals(getTypeInvocation.TargetMethod.OriginalDefinition, symbols.ObjectGetTypeMethod) &&
            typeOfCandidate is ITypeOfOperation { TypeOperand: { } typeOperand })
        {
            receiver = AssertionsAnalyzerHelpers.UnwrapImplicitConversion(instance);
            type = typeOperand;
            return true;
        }

        receiver = null!;
        type = null!;
        return false;
    }

    // ---------------------------------------------------------------------------------------------------------

    private static bool TryGetCondition(IInvocationOperation assertInvocation, INamedTypeSymbol assertType, out IOperation conditionOperation, out bool conditionExpectedToBeFalse)
    {
        if (TrueFalseConditionMethodSelectionAnalyzerCommon.TryGetAssertCondition(assertInvocation, assertType, out var operation, out conditionExpectedToBeFalse))
        {
            conditionOperation = operation;
            return true;
        }

        conditionOperation = null!;
        return false;
    }

    private static bool IsNullConstant(IOperation operation)
        => operation.ConstantValue is { HasValue: true, Value: null };

    private static bool TryGetIgnoreCase(IArgumentOperation argument, out bool ignoreCase)
    {
        var operation = AssertionsAnalyzerHelpers.UnwrapImplicitConversion(argument.Value);
        if (argument.Parameter?.Type?.Name == "StringComparison" && operation.ConstantValue is { HasValue: true, Value: int comparisonValue })
        {
            if (comparisonValue == (int)StringComparison.Ordinal)
            {
                ignoreCase = false;
                return true;
            }

            if (comparisonValue == (int)StringComparison.OrdinalIgnoreCase)
            {
                ignoreCase = true;
                return true;
            }
        }

        ignoreCase = false;
        return false;
    }

    /// <summary>
    /// <c>Assert.Equal</c> inspects its arguments at run time and compares sequences element by element, which
    /// <c>Equals</c> never does. The rewrite is therefore only safe when the value provably cannot be a sequence:
    /// a value type or a sealed type that does not implement <see cref="System.Collections.IEnumerable"/>. An
    /// <c>object</c>, an interface or an open class could hold a collection at run time, and rewriting those can
    /// turn a failing assertion into a passing one.
    /// </summary>
    private static bool MayBeComparedAsSequence(ITypeSymbol? type, Symbols symbols)
    {
        if (type is null)
            return true;

        // Assert.Equal compares strings as strings, never as sequences of characters
        if (type.SpecialType == SpecialType.System_String)
            return false;

        if (SymbolEqualityComparer.Default.Equals(type, symbols.NonGenericEnumerableType))
            return true;

        foreach (var interfaceType in type.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(interfaceType, symbols.NonGenericEnumerableType))
                return true;
        }

        return type is { IsValueType: false, IsSealed: false };
    }

    /// <summary>
    /// Types whose <c>==</c> operator performs the same comparison as <c>Assert.Equal</c>. Floating point types are
    /// excluded because <c>double.NaN == double.NaN</c> is false while <c>double.NaN.Equals(double.NaN)</c> is true.
    /// </summary>
    private static bool HasValueEqualitySemantics(ITypeSymbol? type)
    {
        if (type is null)
            return false;

        if (type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            return type is INamedTypeSymbol { TypeArguments: [var underlyingType] } && HasValueEqualitySemantics(underlyingType);

        if (type.TypeKind == TypeKind.Enum)
            return true;

        return type.SpecialType is
            SpecialType.System_Boolean or
            SpecialType.System_Char or
            SpecialType.System_SByte or
            SpecialType.System_Byte or
            SpecialType.System_Int16 or
            SpecialType.System_UInt16 or
            SpecialType.System_Int32 or
            SpecialType.System_UInt32 or
            SpecialType.System_Int64 or
            SpecialType.System_UInt64 or
            SpecialType.System_Decimal or
            SpecialType.System_String or
            SpecialType.System_IntPtr or
            SpecialType.System_UIntPtr;
    }

    internal readonly record struct Symbols(
        CountAssertionAnalyzerCommon.Symbols CountSymbols,
        ImmutableArray<IMethodSymbol> SequenceEqualMethods,
        IMethodSymbol ObjectReferenceEqualsMethod,
        IMethodSymbol ObjectStaticEqualsMethod,
        IMethodSymbol ObjectGetTypeMethod,
        ImmutableArray<IMethodSymbol> StringStaticEqualsMethods,
        IMethodSymbol SetProperSubsetMethod,
        IMethodSymbol SetProperSupersetMethod,
        INamedTypeSymbol NonGenericEnumerableType,
        INamedTypeSymbol ComparableType);

    internal readonly record struct ConditionRewriteMatch(
        IOperation ReportOperation,
        string AssertionMethodName,
        IOperation[] Arguments,
        bool? IgnoreCaseValue = null,
        ITypeSymbol? TypeArgument = null);
}
