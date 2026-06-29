using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.Assertions;

internal static class TrueFalseConditionMethodSelectionAnalyzerCommon
{
    internal static bool TryCreateSymbols(Compilation compilation, [NotNullWhen(true)] out Symbols? symbols)
    {
        var regexType = compilation.GetTypeByMetadataName("System.Text.RegularExpressions.Regex");
        var stringType = compilation.GetSpecialType(SpecialType.System_String);
        var enumerableType = compilation.GetTypeByMetadataName("System.Linq.Enumerable");
        var genericCollectionType = compilation.GetTypeByMetadataName("System.Collections.Generic.ICollection`1");
        var genericDictionaryType = compilation.GetTypeByMetadataName("System.Collections.Generic.IDictionary`2");
        var genericReadOnlyDictionaryType = compilation.GetTypeByMetadataName("System.Collections.Generic.IReadOnlyDictionary`2");
        var cultureInfoType = compilation.GetTypeByMetadataName("System.Globalization.CultureInfo");

        if (regexType is null || enumerableType is null || genericCollectionType is null ||
            genericDictionaryType is null || genericReadOnlyDictionaryType is null)
        {
            symbols = null;
            return false;
        }

        var regexIsMatchMethods = regexType.GetMembers("IsMatch")
            .OfType<IMethodSymbol>()
            .Where(m => m is { IsStatic: true, Parameters.Length: 2 } &&
                        m.Parameters[0].Type.SpecialType == SpecialType.System_String &&
                        m.Parameters[1].Type.SpecialType == SpecialType.System_String)
            .ToImmutableArray();

        var stringContainsMethods = stringType.GetMembers("Contains").OfType<IMethodSymbol>()
            .Where(m => !m.IsStatic && m.Parameters.Length >= 1 && m.Parameters[0].Type.SpecialType == SpecialType.System_String)
            .ToImmutableArray();

        var stringStartsWithMethods = stringType.GetMembers("StartsWith").OfType<IMethodSymbol>()
            .Where(m => !m.IsStatic && m.Parameters.Length >= 1 && m.Parameters[0].Type.SpecialType == SpecialType.System_String)
            .ToImmutableArray();

        var stringEndsWithMethods = stringType.GetMembers("EndsWith").OfType<IMethodSymbol>()
            .Where(m => !m.IsStatic && m.Parameters.Length >= 1 && m.Parameters[0].Type.SpecialType == SpecialType.System_String)
            .ToImmutableArray();

        var genericCollectionContainsDefinition = genericCollectionType.GetMembers("Contains")
            .OfType<IMethodSymbol>()
            .FirstOrDefault(m => m.Parameters.Length == 1);

        var genericDictionaryContainsKeyDefinition = genericDictionaryType.GetMembers("ContainsKey")
            .OfType<IMethodSymbol>()
            .FirstOrDefault(m => m.Parameters.Length == 1);

        var genericReadOnlyDictionaryContainsKeyDefinition = genericReadOnlyDictionaryType.GetMembers("ContainsKey")
            .OfType<IMethodSymbol>()
            .FirstOrDefault(m => m.Parameters.Length == 1);

        var enumerableContainsMethods = enumerableType.GetMembers("Contains")
            .OfType<IMethodSymbol>()
            .Where(m => m is { IsStatic: true, IsExtensionMethod: true })
            .Select(m => m.OriginalDefinition)
            .ToImmutableArray();

        var enumerableAnyWithPredicateMethods = enumerableType.GetMembers("Any")
            .OfType<IMethodSymbol>()
            .Where(m => m is { IsStatic: true, IsExtensionMethod: true, Parameters.Length: 2 })
            .Select(m => m.OriginalDefinition)
            .ToImmutableArray();

        var enumerableAllMethods = enumerableType.GetMembers("All")
            .OfType<IMethodSymbol>()
            .Where(m => m is { IsStatic: true, IsExtensionMethod: true, Parameters.Length: 2 })
            .Select(m => m.OriginalDefinition)
            .ToImmutableArray();

        if (genericCollectionContainsDefinition is null ||
            genericDictionaryContainsKeyDefinition is null ||
            genericReadOnlyDictionaryContainsKeyDefinition is null ||
            stringContainsMethods.IsDefaultOrEmpty ||
            stringStartsWithMethods.IsDefaultOrEmpty ||
            stringEndsWithMethods.IsDefaultOrEmpty)
        {
            symbols = null;
            return false;
        }

        symbols = new Symbols(
            regexIsMatchMethods,
            stringContainsMethods,
            stringStartsWithMethods,
            stringEndsWithMethods,
            genericCollectionContainsDefinition,
            genericDictionaryContainsKeyDefinition,
            genericReadOnlyDictionaryContainsKeyDefinition,
            enumerableContainsMethods,
            enumerableAnyWithPredicateMethods,
            enumerableAllMethods,
            cultureInfoType);
        return true;
    }

    internal static bool TryGetMatch(
        IInvocationOperation assertInvocation,
        INamedTypeSymbol assertType,
        Symbols symbols,
        out TrueFalseConditionMatch match)
    {
        if (!TryGetAssertCondition(assertInvocation, assertType, out var conditionOperation, out var conditionExpectedToBeFalse))
        {
            match = default;
            return false;
        }

        conditionOperation = AssertionsAnalyzerHelpers.UnwrapImplicitConversion(conditionOperation);
        if (conditionOperation is not IInvocationOperation innerInvocation)
        {
            match = default;
            return false;
        }

        if (TryGetRegexIsMatchMatch(innerInvocation, symbols, conditionExpectedToBeFalse, out match))
            return true;

        if (TryGetStringOperationMatch(innerInvocation, symbols, conditionExpectedToBeFalse, out match))
            return true;

        if (TryGetCollectionContainsMatch(innerInvocation, symbols, conditionExpectedToBeFalse, out match))
            return true;

        if (TryGetDictionaryContainsKeyMatch(innerInvocation, symbols, conditionExpectedToBeFalse, out match))
            return true;

        if (TryGetCollectionAnyMatch(innerInvocation, symbols, conditionExpectedToBeFalse, out match))
            return true;

        if (TryGetCollectionAllMatch(innerInvocation, symbols, conditionExpectedToBeFalse, out match))
            return true;

        match = default;
        return false;
    }

    private static bool TryGetAssertCondition(
        IInvocationOperation invocationOperation,
        INamedTypeSymbol assertType,
        [NotNullWhen(true)] out IOperation? conditionOperation,
        out bool conditionExpectedToBeFalse)
    {
        if (invocationOperation.TargetMethod is not { IsStatic: true } targetMethod ||
            !SymbolEqualityComparer.Default.Equals(targetMethod.ContainingType, assertType))
        {
            conditionOperation = null;
            conditionExpectedToBeFalse = false;
            return false;
        }

        if (targetMethod.Name == "True")
        {
            conditionExpectedToBeFalse = false;
        }
        else if (targetMethod.Name == "False")
        {
            conditionExpectedToBeFalse = true;
        }
        else
        {
            conditionOperation = null;
            conditionExpectedToBeFalse = false;
            return false;
        }

        var conditionArgument = invocationOperation.Arguments.FirstOrDefault(a => a.Parameter?.Name == "condition");
        if (conditionArgument is null)
        {
            conditionOperation = null;
            return false;
        }

        conditionOperation = conditionArgument.Value;
        return true;
    }

    private static bool TryGetRegexIsMatchMatch(
        IInvocationOperation innerInvocation,
        Symbols symbols,
        bool conditionExpectedToBeFalse,
        out TrueFalseConditionMatch match)
    {
        if (innerInvocation.TargetMethod.Name != "IsMatch" ||
            !symbols.RegexIsMatchMethods.Contains(innerInvocation.TargetMethod, SymbolEqualityComparer.Default))
        {
            match = default;
            return false;
        }

        var inputArg = innerInvocation.Arguments.FirstOrDefault(a => a.Parameter?.Name == "input");
        var patternArg = innerInvocation.Arguments.FirstOrDefault(a => a.Parameter?.Name == "pattern");
        if (inputArg is null || patternArg is null)
        {
            match = default;
            return false;
        }

        var assertionMethodName = conditionExpectedToBeFalse ? "DoesNotMatch" : "Matches";
        // Assert.Matches(pattern, actual) — pattern first, actual second
        match = new TrueFalseConditionMatch(
            innerInvocation,
            assertionMethodName,
            [AssertionsAnalyzerHelpers.UnwrapImplicitConversion(patternArg.Value), AssertionsAnalyzerHelpers.UnwrapImplicitConversion(inputArg.Value)]);
        return true;
    }

    private static bool TryGetStringOperationMatch(
        IInvocationOperation innerInvocation,
        Symbols symbols,
        bool conditionExpectedToBeFalse,
        out TrueFalseConditionMatch match)
    {
        if (innerInvocation.Instance is null ||
            innerInvocation.Instance.Type?.SpecialType != SpecialType.System_String)
        {
            match = default;
            return false;
        }

        ImmutableArray<IMethodSymbol> candidates;
        string trueMethodName;
        string falseMethodName;

        switch (innerInvocation.TargetMethod.Name)
        {
            case "Contains":
                candidates = symbols.StringContainsMethods;
                trueMethodName = "Contains";
                falseMethodName = "DoesNotContain";
                break;
            case "StartsWith":
                candidates = symbols.StringStartsWithMethods;
                trueMethodName = "StartsWith";
                falseMethodName = "DoesNotStartWith";
                break;
            case "EndsWith":
                candidates = symbols.StringEndsWith;
                trueMethodName = "EndsWith";
                falseMethodName = "DoesNotEndWith";
                break;
            default:
                match = default;
                return false;
        }

        if (!candidates.Contains(innerInvocation.TargetMethod, SymbolEqualityComparer.Default))
        {
            match = default;
            return false;
        }

        var actualOperation = AssertionsAnalyzerHelpers.UnwrapImplicitConversion(innerInvocation.Instance);
        var valueArg = innerInvocation.Arguments.FirstOrDefault();
        if (valueArg is null)
        {
            match = default;
            return false;
        }

        var valueOperation = AssertionsAnalyzerHelpers.UnwrapImplicitConversion(valueArg.Value);
        var assertionMethodName = conditionExpectedToBeFalse ? falseMethodName : trueMethodName;

        // Check for StringComparison or bool ignoreCase argument
        if (innerInvocation.Arguments.Length >= 2)
        {
            var secondArg = innerInvocation.Arguments[1];
            var secondArgOp = AssertionsAnalyzerHelpers.UnwrapImplicitConversion(secondArg.Value);

            // Handle StringComparison
            if (secondArg.Parameter?.Type?.Name == "StringComparison")
            {
                if (TryGetIgnoreCaseFromStringComparison(secondArgOp, out var ignoreCase))
                {
                    if (ignoreCase)
                    {
                        // Add ignoreCase: true argument
                        match = new TrueFalseConditionMatch(
                            innerInvocation,
                            assertionMethodName,
                            [valueOperation, actualOperation],
                            IgnoreCaseValue: true);
                    }
                    else
                    {
                        // Ordinal is the default — drop the comparison argument
                        match = new TrueFalseConditionMatch(
                            innerInvocation,
                            assertionMethodName,
                            [valueOperation, actualOperation]);
                    }

                    return true;
                }

                // Unsupported StringComparison value — skip
                match = default;
                return false;
            }

            // Handle bool ignoreCase (for StartsWith and EndsWith overloads)
            if (secondArg.Parameter?.Name == "ignoreCase" && secondArg.Parameter.Type.SpecialType == SpecialType.System_Boolean)
            {
                // Third arg: CultureInfo culture — must be null
                if (innerInvocation.Arguments.Length >= 3)
                {
                    var cultureArg = innerInvocation.Arguments[2];
                    var cultureOp = AssertionsAnalyzerHelpers.UnwrapImplicitConversion(cultureArg.Value);
                    if (!IsNullLiteral(cultureOp) && !IsNullCultureInfo(cultureOp, symbols))
                    {
                        // Non-null culture — skip
                        match = default;
                        return false;
                    }
                }

                if (secondArgOp.ConstantValue is { HasValue: true, Value: bool ignoreCaseBool })
                {
                    if (ignoreCaseBool)
                    {
                        match = new TrueFalseConditionMatch(
                            innerInvocation,
                            assertionMethodName,
                            [valueOperation, actualOperation],
                            IgnoreCaseValue: true);
                    }
                    else
                    {
                        match = new TrueFalseConditionMatch(
                            innerInvocation,
                            assertionMethodName,
                            [valueOperation, actualOperation]);
                    }

                    return true;
                }

                // Non-constant bool — skip
                match = default;
                return false;
            }

            // Unrecognized second arg
            match = default;
            return false;
        }

        // No comparison argument — simple case
        match = new TrueFalseConditionMatch(innerInvocation, assertionMethodName, [valueOperation, actualOperation]);
        return true;
    }

    private static bool TryGetIgnoreCaseFromStringComparison(IOperation operation, out bool ignoreCase)
    {
        if (operation.ConstantValue is { HasValue: true, Value: int intValue })
        {
            if (intValue == (int)StringComparison.Ordinal)
            {
                ignoreCase = false;
                return true;
            }

            if (intValue == (int)StringComparison.OrdinalIgnoreCase)
            {
                ignoreCase = true;
                return true;
            }
        }

        ignoreCase = false;
        return false;
    }

    private static bool IsNullLiteral(IOperation operation)
    {
        return operation.ConstantValue is { HasValue: true, Value: null };
    }

    private static bool IsNullCultureInfo(IOperation operation, Symbols symbols)
    {
        _ = symbols;
        return IsNullLiteral(operation);
    }

    private static bool TryGetCollectionContainsMatch(
        IInvocationOperation innerInvocation,
        Symbols symbols,
        bool conditionExpectedToBeFalse,
        out TrueFalseConditionMatch match)
    {
        if (innerInvocation.TargetMethod.Name != "Contains")
        {
            match = default;
            return false;
        }

        // Check: is it ICollection<T>.Contains (instance method) or Enumerable.Contains (extension)?
        var targetMethod = innerInvocation.TargetMethod;

        // Skip string.Contains — handled by TryGetStringOperationMatch
        if (innerInvocation.Instance?.Type?.SpecialType == SpecialType.System_String ||
            (innerInvocation.Arguments.Length > 0 && innerInvocation.Arguments[0].Value.Type?.SpecialType == SpecialType.System_String &&
             innerInvocation.Instance is null && !targetMethod.IsExtensionMethod))
        {
            match = default;
            return false;
        }

        IOperation? collectionOperation = null;
        IOperation? itemOperation = null;

        // ICollection<T>.Contains instance method
        if (!targetMethod.IsStatic && innerInvocation.Instance is not null && innerInvocation.Arguments.Length == 1)
        {
            if (IsCollectionContainsMethod(targetMethod, symbols))
            {
                collectionOperation = AssertionsAnalyzerHelpers.UnwrapImplicitConversion(innerInvocation.Instance);
                itemOperation = AssertionsAnalyzerHelpers.UnwrapImplicitConversion(innerInvocation.Arguments[0].Value);
            }
        }

        // Enumerable.Contains extension method
        if (collectionOperation is null && targetMethod.IsExtensionMethod && innerInvocation.Arguments.Length >= 1)
        {
            var originalDef = (targetMethod.ReducedFrom ?? targetMethod).OriginalDefinition;
            if (symbols.EnumerableContainsMethods.Contains(originalDef, SymbolEqualityComparer.Default))
            {
                if (innerInvocation.Instance is not null)
                {
                    collectionOperation = AssertionsAnalyzerHelpers.UnwrapImplicitConversion(innerInvocation.Instance);
                    itemOperation = AssertionsAnalyzerHelpers.UnwrapImplicitConversion(innerInvocation.Arguments[0].Value);
                }
                else
                {
                    var sourceArg = innerInvocation.Arguments.FirstOrDefault(a => a.Parameter?.Name == "source");
                    var valueArg = innerInvocation.Arguments.FirstOrDefault(a => a.Parameter?.Name == "value");
                    if (sourceArg is not null && valueArg is not null)
                    {
                        collectionOperation = AssertionsAnalyzerHelpers.UnwrapImplicitConversion(sourceArg.Value);
                        itemOperation = AssertionsAnalyzerHelpers.UnwrapImplicitConversion(valueArg.Value);
                    }
                }
            }
        }

        if (collectionOperation is null || itemOperation is null)
        {
            match = default;
            return false;
        }

        // Confirm the receiver is not a string (it might have already been handled)
        if (collectionOperation.Type?.SpecialType == SpecialType.System_String)
        {
            match = default;
            return false;
        }

        var assertionMethodName = conditionExpectedToBeFalse ? "DoesNotContain" : "Contains";
        // Assert.Contains(item, collection) — item first, collection second
        match = new TrueFalseConditionMatch(innerInvocation, assertionMethodName, [itemOperation, collectionOperation]);
        return true;
    }

    private static bool IsCollectionContainsMethod(IMethodSymbol method, Symbols symbols)
    {
        foreach (var interfaceType in method.ContainingType.AllInterfaces)
        {
            if (!SymbolEqualityComparer.Default.Equals(interfaceType.OriginalDefinition, symbols.GenericCollectionContainsDefinition.ContainingType))
                continue;

            var interfaceContains = interfaceType.GetMembers("Contains").OfType<IMethodSymbol>()
                .FirstOrDefault(m => m.Parameters.Length == 1);
            if (interfaceContains is null)
                continue;

            if (SymbolEqualityComparer.Default.Equals(
                method.ContainingType.FindImplementationForInterfaceMember(interfaceContains),
                method))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetDictionaryContainsKeyMatch(
        IInvocationOperation innerInvocation,
        Symbols symbols,
        bool conditionExpectedToBeFalse,
        out TrueFalseConditionMatch match)
    {
        if (innerInvocation.TargetMethod.Name != "ContainsKey" ||
            innerInvocation.Instance is null ||
            innerInvocation.Arguments.Length != 1)
        {
            match = default;
            return false;
        }

        var targetMethod = innerInvocation.TargetMethod;
        if (!IsDictionaryContainsKeyMethod(targetMethod, symbols))
        {
            match = default;
            return false;
        }

        var dictOperation = AssertionsAnalyzerHelpers.UnwrapImplicitConversion(innerInvocation.Instance);
        var keyOperation = AssertionsAnalyzerHelpers.UnwrapImplicitConversion(innerInvocation.Arguments[0].Value);

        var assertionMethodName = conditionExpectedToBeFalse ? "DoesNotContain" : "Contains";
        // Assert.Contains(key, dictionary) — key first, dictionary second
        match = new TrueFalseConditionMatch(innerInvocation, assertionMethodName, [keyOperation, dictOperation]);
        return true;
    }

    private static bool IsDictionaryContainsKeyMethod(IMethodSymbol method, Symbols symbols)
    {
        foreach (var interfaceType in method.ContainingType.AllInterfaces)
        {
            if (!SymbolEqualityComparer.Default.Equals(interfaceType.OriginalDefinition, symbols.GenericDictionaryContainsKeyDefinition.ContainingType) &&
                !SymbolEqualityComparer.Default.Equals(interfaceType.OriginalDefinition, symbols.GenericReadOnlyDictionaryContainsKeyDefinition.ContainingType))
            {
                continue;
            }

            var interfaceContainsKey = interfaceType.GetMembers("ContainsKey").OfType<IMethodSymbol>()
                .FirstOrDefault(m => m.Parameters.Length == 1);
            if (interfaceContainsKey is null)
                continue;

            if (SymbolEqualityComparer.Default.Equals(
                method.ContainingType.FindImplementationForInterfaceMember(interfaceContainsKey),
                method))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetCollectionAnyMatch(
        IInvocationOperation innerInvocation,
        Symbols symbols,
        bool conditionExpectedToBeFalse,
        out TrueFalseConditionMatch match)
    {
        if (innerInvocation.TargetMethod.Name != "Any" ||
            symbols.EnumerableAnyWithPredicateMethods.IsDefaultOrEmpty)
        {
            match = default;
            return false;
        }

        var originalDef = (innerInvocation.TargetMethod.ReducedFrom ?? innerInvocation.TargetMethod).OriginalDefinition;
        if (!symbols.EnumerableAnyWithPredicateMethods.Contains(originalDef, SymbolEqualityComparer.Default))
        {
            match = default;
            return false;
        }

        IOperation collectionOperation;
        IOperation predicateOperation;

        if (innerInvocation.Instance is not null)
        {
            collectionOperation = AssertionsAnalyzerHelpers.UnwrapImplicitConversion(innerInvocation.Instance);
            predicateOperation = AssertionsAnalyzerHelpers.UnwrapImplicitConversion(innerInvocation.Arguments[0].Value);
        }
        else
        {
            var sourceArg = innerInvocation.Arguments.FirstOrDefault(a => a.Parameter?.Name == "source");
            var predicateArg = innerInvocation.Arguments.FirstOrDefault(a => a.Parameter?.Name == "predicate");
            if (sourceArg is null || predicateArg is null)
            {
                match = default;
                return false;
            }

            collectionOperation = AssertionsAnalyzerHelpers.UnwrapImplicitConversion(sourceArg.Value);
            predicateOperation = AssertionsAnalyzerHelpers.UnwrapImplicitConversion(predicateArg.Value);
        }

        var assertionMethodName = conditionExpectedToBeFalse ? "DoesNotContain" : "Contains";
        // Assert.Contains(collection, predicate) — collection first, predicate second (matches existing overload)
        match = new TrueFalseConditionMatch(innerInvocation, assertionMethodName, [collectionOperation, predicateOperation]);
        return true;
    }

    private static bool TryGetCollectionAllMatch(
        IInvocationOperation innerInvocation,
        Symbols symbols,
        bool conditionExpectedToBeFalse,
        out TrueFalseConditionMatch match)
    {
        if (innerInvocation.TargetMethod.Name != "All" ||
            symbols.EnumerableAllMethods.IsDefaultOrEmpty)
        {
            match = default;
            return false;
        }

        var originalDef = (innerInvocation.TargetMethod.ReducedFrom ?? innerInvocation.TargetMethod).OriginalDefinition;
        if (!symbols.EnumerableAllMethods.Contains(originalDef, SymbolEqualityComparer.Default))
        {
            match = default;
            return false;
        }

        IOperation collectionOperation;
        IOperation predicateOperation;

        if (innerInvocation.Instance is not null)
        {
            collectionOperation = AssertionsAnalyzerHelpers.UnwrapImplicitConversion(innerInvocation.Instance);
            predicateOperation = AssertionsAnalyzerHelpers.UnwrapImplicitConversion(innerInvocation.Arguments[0].Value);
        }
        else
        {
            var sourceArg = innerInvocation.Arguments.FirstOrDefault(a => a.Parameter?.Name == "source");
            var predicateArg = innerInvocation.Arguments.FirstOrDefault(a => a.Parameter?.Name == "predicate");
            if (sourceArg is null || predicateArg is null)
            {
                match = default;
                return false;
            }

            collectionOperation = AssertionsAnalyzerHelpers.UnwrapImplicitConversion(sourceArg.Value);
            predicateOperation = AssertionsAnalyzerHelpers.UnwrapImplicitConversion(predicateArg.Value);
        }

        var assertionMethodName = conditionExpectedToBeFalse ? "DoesNotAll" : "All";
        // Assert.All(collection, predicate) — collection first, predicate second
        match = new TrueFalseConditionMatch(innerInvocation, assertionMethodName, [collectionOperation, predicateOperation]);
        return true;
    }

    internal static string GetDiagnosticId(TrueFalseConditionMatch match)
    {
        return match.AssertionMethodName switch
        {
            "Matches" => RuleIdentifiers.UseMatchesDiagnosticId,
            "DoesNotMatch" => RuleIdentifiers.UseDoesNotMatchDiagnosticId,
            "Contains" when IsStringContainsMatch(match) => RuleIdentifiers.UseStringContainsDiagnosticId,
            "DoesNotContain" when IsStringContainsMatch(match) => RuleIdentifiers.UseStringDoesNotContainDiagnosticId,
            "Contains" when IsCollectionAnyMatch(match) => RuleIdentifiers.UseCollectionAnyContainsDiagnosticId,
            "DoesNotContain" when IsCollectionAnyMatch(match) => RuleIdentifiers.UseCollectionAnyDoesNotContainDiagnosticId,
            "Contains" => RuleIdentifiers.UseCollectionContainsDiagnosticId,
            "DoesNotContain" => RuleIdentifiers.UseCollectionDoesNotContainDiagnosticId,
            "StartsWith" => RuleIdentifiers.UseStringStartsWithDiagnosticId,
            "DoesNotStartWith" => RuleIdentifiers.UseStringDoesNotStartWithDiagnosticId,
            "EndsWith" => RuleIdentifiers.UseStringEndsWithDiagnosticId,
            "DoesNotEndWith" => RuleIdentifiers.UseStringDoesNotEndWithDiagnosticId,
            "All" => RuleIdentifiers.UseCollectionAllDiagnosticId,
            "DoesNotAll" => RuleIdentifiers.UseCollectionDoesNotAllDiagnosticId,
            _ => throw new System.InvalidOperationException($"Unexpected assertion method: {match.AssertionMethodName}"),
        };
    }

    private static bool IsStringContainsMatch(TrueFalseConditionMatch match)
    {
        return match.InnerInvocation.Instance?.Type?.SpecialType == SpecialType.System_String;
    }

    private static bool IsCollectionAnyMatch(TrueFalseConditionMatch match)
    {
        return match.InnerInvocation.TargetMethod.Name == "Any";
    }

    internal readonly record struct Symbols(
        ImmutableArray<IMethodSymbol> RegexIsMatchMethods,
        ImmutableArray<IMethodSymbol> StringContainsMethods,
        ImmutableArray<IMethodSymbol> StringStartsWithMethods,
        ImmutableArray<IMethodSymbol> StringEndsWith,
        IMethodSymbol GenericCollectionContainsDefinition,
        IMethodSymbol GenericDictionaryContainsKeyDefinition,
        IMethodSymbol GenericReadOnlyDictionaryContainsKeyDefinition,
        ImmutableArray<IMethodSymbol> EnumerableContainsMethods,
        ImmutableArray<IMethodSymbol> EnumerableAnyWithPredicateMethods,
        ImmutableArray<IMethodSymbol> EnumerableAllMethods,
        INamedTypeSymbol? CultureInfoType);

    internal readonly record struct TrueFalseConditionMatch(
        IInvocationOperation InnerInvocation,
        string AssertionMethodName,
        IOperation[] Arguments,
        bool? IgnoreCaseValue = null)
    {
        public bool HasIgnoreCase => IgnoreCaseValue.HasValue;
    }
}
