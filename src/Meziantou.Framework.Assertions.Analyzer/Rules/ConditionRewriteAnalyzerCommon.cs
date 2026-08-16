using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
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

    internal static bool TryCreateSymbols(Compilation compilation, [NotNullWhen(true)] out Symbols? symbols)
    {
        var enumerableType = compilation.GetTypeByMetadataName("System.Linq.Enumerable");
        var nonGenericEnumerableType = compilation.GetTypeByMetadataName("System.Collections.IEnumerable");
        if (enumerableType is null || nonGenericEnumerableType is null)
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

        var stringStaticEqualsMethods = stringType.GetMembers("Equals")
            .OfType<IMethodSymbol>()
            .Where(m => m.IsStatic && m.Parameters.Length is 2 or 3)
            .ToImmutableArray();

        if (objectReferenceEqualsMethod is null || objectStaticEqualsMethod is null)
        {
            symbols = null;
            return false;
        }

        symbols = new Symbols(
            countSymbols,
            sequenceEqualMethods,
            objectReferenceEqualsMethod,
            objectStaticEqualsMethod,
            stringStaticEqualsMethods,
            nonGenericEnumerableType);
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
        ImmutableArray<IMethodSymbol> StringStaticEqualsMethods,
        INamedTypeSymbol NonGenericEnumerableType);

    internal readonly record struct ConditionRewriteMatch(
        IOperation ReportOperation,
        string AssertionMethodName,
        IOperation[] Arguments,
        bool? IgnoreCaseValue = null);
}
