using System.Reflection;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

#pragma warning disable CA1822, CA1852 // Formatter methods intentionally share an instance-based overridable shape.
internal class AssertionFormatter
{
    private const char CombiningLowLine = '\u0332';

    private static readonly MethodInfo FormatBoxedMemoryValueMethod = typeof(AssertionFormatter).GetMethod(nameof(FormatBoxedMemoryValue), BindingFlags.NonPublic | BindingFlags.Instance)!;

    // Exceptions thrown by a lazy sequence while the formatter reads more of it, keyed by the snapshot's item list.
    private static readonly ConditionalWeakTable<object, Exception> ObservationExceptions = new();

    public static AssertionFormatter Default { get; } = new AssertionFormatter();

    private static AssertionMessageBuilder CreateMessage(string header, string? message)
    {
        return new AssertionMessageBuilder(header).AppendUserMessage("Message", message);
    }

    internal FormatterOptions Options
    {
        get;
        set => field = value ?? throw new ArgumentNullException(nameof(value));
    } = new();

    /// <summary>Gets or sets the number of items to format from the start of an enumerable before truncating it.</summary>
    /// <remarks>
    /// When there is no highlighted item, or when the highlighted item is within this leading range, the formatter writes items from the beginning of the enumerable.
    /// If <see cref="SuffixItemCount"/> requires more items after a highlighted item, the formatter can write more than this value.
    /// When the highlighted item index is greater than or equal to this value, the formatter switches to focused mode: it writes <see cref="PrefixItemCount"/> items from the beginning, an ellipsis, and a window around the highlighted item controlled by <see cref="HighlightedContextItemCount"/>.
    /// </remarks>
    public int MaxFormattedItems
    {
        get => Options.MaxFormattedItems;
        set => Options.MaxFormattedItems = value;
    }

    /// <summary>Gets or sets the number of items to keep from the start of an enumerable when a highlighted item is outside the leading range.</summary>
    /// <remarks>
    /// This value is used only in focused mode, when the highlighted item index is greater than or equal to <see cref="MaxFormattedItems"/>.
    /// It preserves the beginning of the enumerable before the ellipsis and the highlighted-item context window.
    /// </remarks>
    public int PrefixItemCount
    {
        get => Options.PrefixItemCount;
        set => Options.PrefixItemCount = value;
    }

    /// <summary>Gets or sets the minimum number of items to format after a highlighted item in the leading range.</summary>
    /// <remarks>
    /// This value is used when the highlighted item index is less than <see cref="MaxFormattedItems"/>.
    /// In that case, the formatter writes at least <see cref="MaxFormattedItems"/> items, and can continue up to the highlighted item plus this many following items.
    /// This lets assertion failures found near the beginning of a snapshot show extra items after the difference.
    /// </remarks>
    public int SuffixItemCount
    {
        get => Options.SuffixItemCount;
        set => Options.SuffixItemCount = value;
    }

    /// <summary>Gets or sets the number of neighboring items to format before and after a highlighted item in focused mode.</summary>
    /// <remarks>
    /// This value is used only when the highlighted item index is greater than or equal to <see cref="MaxFormattedItems"/>.
    /// The formatter then writes <see cref="PrefixItemCount"/> items from the beginning, an ellipsis when items were skipped, and up to this many items on each side of the highlighted item.
    /// </remarks>
    public int HighlightedContextItemCount
    {
        get => Options.HighlightedContextItemCount;
        set => Options.HighlightedContextItemCount = value;
    }

    public string Format(FailAssertionError error)
    {
        return CreateMessage("Assert.Fail() assertion failed.", error.Message).ToString();
    }

    public virtual string Format(TrueAssertionError error)
    {
        return CreateMessage("Assert.True() assertion failed.", error.Message)
            .Append("Expression", error.Expression)
            .AppendGroup(
                ("Expected", "true"),
                ("Actual", FormatBoolean(error.Actual)))
            .ToString();
    }

    public virtual string Format(FalseAssertionError error)
    {
        return CreateMessage("Assert.False() assertion failed.", error.Message)
            .Append("Expression", error.Expression)
            .AppendGroup(
                ("Expected", "false"),
                ("Actual", FormatBoolean(error.Actual)))
            .ToString();
    }

    private static string FormatBoolean(bool? value)
    {
        return value switch
        {
            true => "true",
            false => "false",
            null => "<null>",
        };
    }

    public virtual string Format(NegativeExpressionAssertionError error)
    {
        return CreateMessage($"Assert.{error.AssertionName}() assertion failed.", error.Message)
            .AppendGroup(
                ("Not expected", error.NotExpectedText),
                ("Actual", error.Expression))
            .ToString();
    }

    public virtual string Format(NegativeExceptionAssertionError error)
    {
        var builder = CreateMessage($"Assert.{error.AssertionName}() assertion failed.", error.Message)
            .Append("Expression", error.Expression)
            .Append("Not expected", error.NotExpectedText)
            .Append("Exception", error.ExceptionType.FullName ?? string.Empty);

        if (!string.IsNullOrEmpty(error.ExceptionMessage))
        {
            builder.Append("Exception message", error.ExceptionMessage);
        }

        return builder.ToString();
    }

    public virtual string Format<T>(NegativeReadOnlySpanActualValueAssertionError<T> error)
    {
        return CreateMessage($"Assert.{error.AssertionName}() assertion failed.", error.Message)
            .Append("Expression", error.ActualExpression)
            .AppendGroup(
                ("Not expected", error.NotExpectedText),
                ("Actual", FormatReadOnlySpanValue(error.ActualValue)))
            .ToString();
    }

    public virtual string Format<TExpected, TActual>(NegativeReadOnlySpanValueAssertionError<TExpected, TActual> error)
    {
        return CreateMessage($"Assert.{error.AssertionName}() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .AppendGroup(
                (error.NotExpectedLabel, FormatReadOnlySpanValue(error.ExpectedValue)),
                ("Actual", FormatReadOnlySpanValue(error.ActualValue)))
            .ToString();
    }

    public virtual string Format<TExpected, TActual>(NegativeReadOnlySpanExpectedActualValueAssertionError<TExpected, TActual> error)
    {
        return CreateMessage($"Assert.{error.AssertionName}() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .AppendGroup(
                (error.NotExpectedLabel, FormatValue(error.ExpectedValue)),
                ("Actual", FormatReadOnlySpanValue(error.ActualValue)))
            .ToString();
    }

    public virtual string Format<T>(NegativeReadOnlySpanCountAssertionError<T> error)
    {
        return CreateMessage($"Assert.{error.AssertionName}() assertion failed.", error.Message)
            .Append("Expression", error.ActualExpression)
            .AppendGroup(
                ("Not expected count", error.NotExpectedCount.ToString(CultureInfo.InvariantCulture)),
                ("Actual count", error.ActualCount.ToString(CultureInfo.InvariantCulture)))
            .Append("Actual", FormatReadOnlySpanValue(error.ActualValue))
            .ToString();
    }

    private string FormatNegativeValue(string assertionName, string? expectedExpression, string? actualExpression, string notExpectedLabel, object? expectedValue, object? actualValue, string? message)
    {
        return CreateMessage($"Assert.{assertionName}() assertion failed.", message)
            .AppendGroup(
                ("Expected expression", expectedExpression ?? string.Empty),
                ("Actual expression", actualExpression ?? string.Empty))
            .AppendGroup(
                (notExpectedLabel, FormatValue(expectedValue)),
                ("Actual", FormatValue(actualValue)))
            .ToString();
    }

    public virtual string Format<TExpected, TActual>(DoesNotContainAssertionError<TExpected, TActual> error)
    {
        return FormatNegativeValue(nameof(Assert.DoesNotContain), error.ExpectedExpression, error.ActualExpression, error.NotExpectedLabel, error.ExpectedValue, error.ActualValue, error.Message);
    }

    public virtual string Format<TExpected, TActual>(DoesNotStartWithAssertionError<TExpected, TActual> error)
    {
        return FormatNegativeValue(nameof(Assert.DoesNotStartWith), error.ExpectedExpression, error.ActualExpression, error.NotExpectedLabel, error.ExpectedValue, error.ActualValue, error.Message);
    }

    public virtual string Format<TExpected, TActual>(DoesNotEndWithAssertionError<TExpected, TActual> error)
    {
        return FormatNegativeValue(nameof(Assert.DoesNotEndWith), error.ExpectedExpression, error.ActualExpression, error.NotExpectedLabel, error.ExpectedValue, error.ActualValue, error.Message);
    }

    public virtual string Format<TExpected, TActual>(NotEqualAssertionError<TExpected, TActual> error)
    {
        return FormatNegativeValue(nameof(Assert.NotEqual), error.ExpectedExpression, error.ActualExpression, error.NotExpectedLabel, error.ExpectedValue, error.ActualValue, error.Message);
    }

    public virtual string Format<TExpected, TActual>(NotEqualUnorderedAssertionError<TExpected, TActual> error)
    {
        return FormatNegativeValue(nameof(Assert.NotEqualUnordered), error.ExpectedExpression, error.ActualExpression, error.NotExpectedLabel, error.ExpectedValue, error.ActualValue, error.Message);
    }

    public virtual string Format(NotEquivalentAssertionError error)
    {
        return FormatNegativeValue(nameof(Assert.NotEquivalent), error.ExpectedExpression, error.ActualExpression, "Not expected", error.ExpectedValue, error.ActualValue, error.Message);
    }

    public virtual string Format(DoesNotMatchAssertionError error)
    {
        return FormatNegativeValue(nameof(Assert.DoesNotMatch), error.ExpectedExpression, error.ActualExpression, error.NotExpectedLabel, error.ExpectedValue, error.ActualValue, error.Message);
    }

    public virtual string Format<TActual>(NegativeActualValueAssertionError<TActual> error)
    {
        return CreateMessage($"Assert.{error.AssertionName}() assertion failed.", error.Message)
            .Append("Expression", error.ActualExpression)
            .AppendGroup(
                ("Not expected", error.NotExpectedText),
                ("Actual", FormatValue(error.ActualValue)))
            .ToString();
    }

    public virtual string Format(NegativeSameAssertionError error)
    {
        return CreateMessage("Assert.NotSame() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .AppendGroup(
                ("Not expected", "same instance as " + FormatValue(error.ExpectedValue)),
                ("Actual", FormatValue(error.ActualValue)))
            .ToString();
    }

    public virtual string Format<T>(NegativeRangeAssertionError<T> error)
    {
        return CreateMessage($"Assert.{error.AssertionName}() assertion failed.", error.Message)
            .Append("Expression", error.ActualExpression)
            .AppendGroup(
                ("Not expected", $"in range [{FormatValue(error.LowValue)}, {FormatValue(error.HighValue)}]"),
                ("Actual", FormatValue(error.ActualValue)))
            .ToString();
    }

    public virtual string Format(NegativeTypeAssertionError error)
    {
        return CreateMessage($"Assert.{error.AssertionName}() assertion failed.", error.Message)
            .Append("Expression", error.ActualExpression)
            .AppendGroup(
                (error.NotExpectedTypeLabel, FormatType(error.ExpectedType)),
                ("Actual type", FormatType(error.ActualValue?.GetType())))
            .Append("Actual value", FormatValue(error.ActualValue))
            .ToString();
    }

    public virtual string Format(NegativeSetAssertionError error)
    {
        var setName = error.IsSuperset ? "superset" : "subset";
        return CreateMessage($"Assert.NotProper{(error.IsSuperset ? "Superset" : "Subset")}() assertion failed.", error.Message)
            .AppendGroup(
                ($"Expected {setName} expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .AppendGroup(
                ($"Not expected {setName}", FormatValue(error.ExpectedValue)),
                ("Actual", FormatValue(error.ActualValue)))
            .ToString();
    }

    public virtual string Format<T>(NegativeCountAssertionError<T> error)
    {
        return CreateMessage($"Assert.{error.AssertionName}() assertion failed.", error.Message)
            .Append("Expression", error.ActualExpression)
            .AppendGroup(
                ("Not expected count", error.NotExpectedCount.ToString(CultureInfo.InvariantCulture)),
                ("Actual count", error.ActualCount.ToString(CultureInfo.InvariantCulture)))
            .Append("Actual", FormatValue(error.ActualValue))
            .ToString();
    }

    public virtual string Format<T>(NegativeEqualWithToleranceAssertionError<T> error)
    {
        return CreateMessage("Assert.NotEqual() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .AppendGroup(
                ("Not expected", FormatValue(error.ExpectedValue)),
                ("Actual", FormatValue(error.ActualValue)))
            .Append("Tolerance", FormatValue(error.Tolerance))
            .ToString();
    }

    public virtual string Format(NullAssertionError error)
    {
        return CreateMessage("Assert.Null() assertion failed.", error.Message)
            .Append("Expression", error.ActualExpression)
            .AppendGroup(
                ("Expected", "<null>"),
                ("Actual", FormatValue(error.ActualValue)))
            .ToString();
    }

    public virtual string Format(IsTypeAssertionError error)
    {
        return CreateMessage("Assert.IsType() assertion failed.", error.Message)
            .Append("Expression", error.ActualExpression)
            .AppendGroup(
                ("Expected type", FormatType(error.ExpectedType)),
                ("Actual type", FormatType(error.ActualValue?.GetType())))
            .Append("Actual value", FormatValue(error.ActualValue))
            .ToString();
    }

    public virtual string Format(IsAssignableToAssertionError error)
    {
        return CreateMessage("Assert.IsAssignableTo() assertion failed.", error.Message)
            .Append("Expression", error.ActualExpression)
            .AppendGroup(
                ("Expected type", FormatType(error.ExpectedType)),
                ("Actual type", FormatType(error.ActualValue?.GetType())))
            .Append("Actual value", FormatValue(error.ActualValue))
            .ToString();
    }

    public virtual string Format(SameAssertionError error)
    {
        return CreateMessage("Assert.Same() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .AppendGroup(
                ("Expected", "same instance as " + FormatValue(error.ExpectedValue)),
                ("Actual", FormatValue(error.ActualValue)))
            .ToString();
    }

    public virtual string Format<T>(InRangeAssertionError<T> error)
    {
        return CreateMessage("Assert.InRange() assertion failed.", error.Message)
            .Append("Expression", error.ActualExpression)
            .AppendGroup(
                ("Expected", $"in range [{FormatValue(error.LowValue)}, {FormatValue(error.HighValue)}]"),
                ("Actual", FormatValue(error.ActualValue)))
            .ToString();
    }

    public virtual string Format(ThrowsAssertionError error)
    {
        return CreateMessage($"Assert.{(error.AllowDerivedTypes ? "ThrowsAny" : "Throws")}() assertion failed.", error.Message)
            .Append("Expression", error.ActionExpression)
            .AppendGroup(
                ("Expected exception type", FormatType(error.ExpectedExceptionType)),
                ("Actual exception type", FormatType(error.ActualException?.GetType())))
            .Append("Exception", FormatException(error.ActualException))
            .ToString();
    }

    public virtual string Format(RegexMatchesAssertionError error)
    {
        return CreateMessage("Assert.Match() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .AppendGroup(
                ("Expected pattern", FormatValue(error.ExpectedPattern)),
                ("Actual", FormatValue(error.ActualValue)))
            .ToString();
    }

    public virtual string Format(RaiseAssertionError error)
    {
        return CreateMessage($"Assert.{(error.AllowDerivedTypes ? "RaiseAny" : "Raise")}() assertion failed.", error.Message)
            .Append("Expression", error.ActionExpression)
            .AppendGroup(
                ("Expected event args type", FormatType(error.ExpectedEventArgsType)),
                ("Actual event args type", FormatType(error.ActualEventArgsType)))
            .ToString();
    }

    public virtual string Format<T>(CollectionSetAssertionError<T> error)
    {
        var setName = error.IsSuperset ? "superset" : "subset";
        return CreateMessage($"Assert.{(error.IsSuperset ? "ProperSuperset" : "ProperSubset")}() assertion failed.", error.Message)
            .AppendGroup(
                ($"Expected {setName} expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .AppendGroup(
                ($"Expected {setName}", FormatValue(error.ExpectedValue.Items)),
                ("Actual", FormatValue(error.ActualValue.Items)))
            .ToString();
    }

    public virtual string Format<TExpected, TActual>(EqualAssertionError<TExpected, TActual> error)
    {
        var builder = CreateMessage("Assert.Equal() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression));

        if (error.FirstDifferenceIndex is not null)
        {
            builder.Append("Index of first difference", error.FirstDifferenceIndex.Value.ToString(CultureInfo.InvariantCulture));
        }

        return builder
            .AppendGroup(
                ("Expected", FormatValue(error.ExpectedValue, error.FirstDifferenceIndex)),
                ("Actual", FormatValue(error.ActualValue, error.FirstDifferenceIndex)))
            .ToString();
    }

    public virtual string Format<T>(EqualWithToleranceAssertionError<T> error)
    {
        return CreateMessage("Assert.Equal() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .AppendGroup(
                ("Expected", FormatValue(error.ExpectedValue)),
                ("Actual", FormatValue(error.ActualValue)))
            .Append("Tolerance", FormatValue(error.Tolerance))
            .ToString();
    }

    public virtual string Format(EquivalentAssertionError error)
    {
        return CreateMessage("Assert.Equivalent() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .Append("Path", error.Path)
            .Append("Reason", error.Reason)
            .AppendGroup(
                ("Expected", FormatStructuralValue(error.ExpectedValue)),
                ("Actual", FormatStructuralValue(error.ActualValue)))
            .ToString();
    }

    public virtual string Format<TExpected, TActual>(ReadOnlySpanEqualAssertionError<TExpected, TActual> error)
    {
        return CreateMessage($"Assert.Equal() assertion failed: Item at index {error.FirstDifferenceIndex} differs.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .Append("Index of first difference", error.FirstDifferenceIndex.ToString(CultureInfo.InvariantCulture))
            .AppendGroup(
                ("Expected item", FormatReadOnlySpanValue(error.ExpectedValue, error.FirstDifferenceIndex)),
                ("Actual item", FormatReadOnlySpanValue(error.ActualValue, error.FirstDifferenceIndex)))
            .ToString();
    }

    public virtual string Format<TExpected, TActual>(ReadOnlySpanLengthAssertionError<TExpected, TActual> error)
    {
        return CreateMessage("Assert.Equal() assertion failed: Lengths differ.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .AppendGroup(
                ("Expected length", error.ExpectedValue.Length.ToString(CultureInfo.InvariantCulture)),
                ("Actual length", error.ActualValue.Length.ToString(CultureInfo.InvariantCulture)))
            .Append("Index of first difference", error.FirstDifferenceIndex.ToString(CultureInfo.InvariantCulture))
            .AppendGroup(
                ("Expected", FormatReadOnlySpanValue(error.ExpectedValue, error.FirstDifferenceIndex < error.ExpectedValue.Length ? error.FirstDifferenceIndex : null)),
                ("Actual", FormatReadOnlySpanValue(error.ActualValue, error.FirstDifferenceIndex < error.ActualValue.Length ? error.FirstDifferenceIndex : null)))
            .ToString();
    }

    public virtual string Format<T>(ValueStartsWithAssertionError<T> error)
    {
        return CreateMessage("Assert.StartsWith() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .AppendGroup(
                ("Expected prefix", FormatValue(error.ExpectedValue)),
                ("Actual", FormatReadOnlySpanValue(error.ActualValue, error.ActualValue.IsEmpty ? null : 0)))
            .ToString();
    }

    public virtual string Format<T>(ValueCollectionStartsWithAssertionError<T> error)
    {
        EnsureObservedItems(error.ActualValue, GetMaxFormattedIndex(error.ActualValue.Items.Count > 0 ? 0 : null));

        return CreateMessage("Assert.StartsWith() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .AppendGroup(
                ("Expected prefix", FormatValue(error.ExpectedValue)),
                ("Actual", FormatValue(error.ActualValue.Items, error.ActualValue.Items.Count > 0 ? 0 : null)))
            .ToString();
    }

    public virtual string Format<T>(ReadOnlySpanEmptyAssertionError<T> error)
    {
        return CreateMessage("Assert.Empty() assertion failed.", error.Message)
            .Append("Expression", error.ActualExpression)
            .Append("Actual", FormatReadOnlySpanValue(error.ActualValue, error.ActualValue.IsEmpty ? null : 0))
            .ToString();
    }

    public virtual string Format(StringEmptyAssertionError error)
    {
        return CreateMessage("Assert.Empty() assertion failed.", error.Message)
            .Append("Expression", error.ActualExpression)
            .Append("Actual", FormatStringValue(error.ActualValue, error.ActualValue.IsEmpty ? null : 0))
            .ToString();
    }

    public virtual string Format<T>(CollectionEmptyAssertionError<T> error)
    {
        EnsureObservedItems(error.ActualValue, GetMaxFormattedIndex(error.ActualValue.Items.Count > 0 ? 0 : null));

        return CreateMessage("Assert.Empty() assertion failed.", error.Message)
            .Append("Expression", error.ActualExpression)
            .Append("Actual", FormatValue(error.ActualValue.Items, error.ActualValue.Items.Count > 0 ? 0 : null))
            .ToString();
    }

    public virtual async Task<string> FormatAsync<T>(AsyncCollectionEmptyAssertionError<T> error)
    {
        await EnsureObservedItemsAsync(error.ActualValue, GetMaxFormattedIndex(error.ActualValue.Items.Count > 0 ? 0 : null)).ConfigureAwait(false);

        return CreateMessage("Assert.Empty() assertion failed.", error.Message)
            .Append("Expression", error.ActualExpression)
            .Append("Actual", FormatValue(error.ActualValue.Items, error.ActualValue.Items.Count > 0 ? 0 : null))
            .ToString();
    }

    public virtual string Format<T>(ReadOnlySpanSingleAssertionError<T> error)
    {
        return CreateMessage("Assert.Single() assertion failed.", error.Message)
            .Append("Expression", error.ActualExpression)
            .Append("Actual", FormatReadOnlySpanValue(error.ActualValue, GetSingleFailureHighlightedIndex(error.ActualValue.Length)))
            .ToString();
    }

    public virtual string Format(StringSingleAssertionError error)
    {
        return CreateMessage("Assert.Single() assertion failed.", error.Message)
            .Append("Expression", error.ActualExpression)
            .Append("Actual", FormatStringValue(error.ActualValue, GetSingleFailureHighlightedIndex(error.ActualValue.Length)))
            .ToString();
    }

    public virtual string Format<T>(CollectionSingleAssertionError<T> error)
    {
        EnsureObservedItems(error.ActualValue, GetMaxFormattedIndex(GetSingleFailureHighlightedIndex(error.ActualValue.Items.Count)));

        return CreateMessage("Assert.Single() assertion failed.", error.Message)
            .Append("Expression", error.ActualExpression)
            .Append("Actual", FormatValue(error.ActualValue.Items, GetSingleFailureHighlightedIndex(error.ActualValue.Items.Count)))
            .ToString();
    }

    public virtual string Format<T>(CollectionSinglePredicateAssertionError<T> error)
    {
        EnsureObservedItems(error.MatchingValues, GetMaxFormattedIndex(GetSingleFailureHighlightedIndex(error.MatchingValues.Items.Count)));

        return CreateMessage("Assert.Single() assertion failed.", error.Message)
            .AppendGroup(
                ("Expression", error.ActualExpression),
                ("Predicate expression", error.PredicateExpression))
            .Append("Matching items", FormatValue(error.MatchingValues.Items, GetSingleFailureHighlightedIndex(error.MatchingValues.Items.Count)))
            .ToString();
    }

    public virtual string Format<T>(CollectionContainsPredicateAssertionError<T> error)
    {
        EnsureObservedItems(error.MatchingValues, GetMaxFormattedIndex(highlightedIndex: null));

        return CreateMessage("Assert.Contains() assertion failed.", error.Message)
            .AppendGroup(
                ("Expression", error.ActualExpression),
                ("Predicate expression", error.PredicateExpression))
            .Append("Matching items", FormatValue(error.MatchingValues.Items))
            .ToString();
    }

    public virtual string Format(PredicateNullActualAssertionError error)
    {
        return CreateMessage($"Assert.{error.AssertionName}() assertion failed.", error.Message)
            .AppendGroup(
                ("Expression", error.ActualExpression),
                ("Predicate expression", error.PredicateExpression))
            .Append("Actual", "<null>")
            .ToString();
    }

    public virtual string Format<T>(CollectionDoesNotContainPredicateAssertionError<T> error)
    {
        EnsureObservedItems(error.MatchingValues, GetMaxFormattedIndex(highlightedIndex: null));

        return CreateMessage("Assert.DoesNotContain() assertion failed.", error.Message)
            .AppendGroup(
                ("Expression", error.ActualExpression),
                ("Predicate expression", error.PredicateExpression))
            .AppendGroup(
                ("Not expected", "any matching item"),
                ("Matching items", FormatValue(error.MatchingValues.Items)))
            .ToString();
    }

    public virtual async Task<string> FormatAsync<T>(AsyncCollectionSingleAssertionError<T> error)
    {
        await EnsureObservedItemsAsync(error.ActualValue, GetMaxFormattedIndex(GetSingleFailureHighlightedIndex(error.ActualValue.Items.Count))).ConfigureAwait(false);

        return CreateMessage("Assert.Single() assertion failed.", error.Message)
            .Append("Expression", error.ActualExpression)
            .Append("Actual", FormatValue(error.ActualValue.Items, GetSingleFailureHighlightedIndex(error.ActualValue.Items.Count)))
            .ToString();
    }

    public virtual string Format<T>(CollectionAssertionError<T> error)
    {
        EnsureObservedItems(error.ActualValue, GetMaxFormattedIndex(highlightedIndex: null));

        return CreateMessage("Assert.Collection() assertion failed: Collection count does not match inspector count.", error.Message)
            .Append("Expression", error.ActualExpression)
            .AppendGroup(
                ("Expected count", error.ExpectedCount.ToString(CultureInfo.InvariantCulture)),
                ("Actual count", error.ActualValue.Items.Count.ToString(CultureInfo.InvariantCulture)))
            .Append("Actual", FormatValue(error.ActualValue.Items))
            .ToString();
    }

    public virtual string Format<T>(CollectionInspectorAssertionError<T> error)
    {
        EnsureObservedItems(error.ActualValue, GetMaxFormattedIndex(error.Index));

        return CreateMessage($"Assert.Collection() assertion failed: Item at index {error.Index} failed.", error.Message)
            .Append("Expression", error.ActualExpression)
            .Append("Actual", FormatValue(error.ActualValue.Items, error.Index))
            .Append("Exception", FormatException(error.Exception))
            .ToString();
    }

    public virtual string Format<T>(ReadOnlySpanAllAssertionError<T> error)
    {
        return CreateMessage($"Assert.All() assertion failed: Item at index {error.Index} failed.", error.Message)
            .AppendGroup(
                ("Expression", error.ActualExpression),
                ("Assertion expression", error.AssertionExpression))
            .Append("Actual", FormatReadOnlySpanValue(error.ActualValue, error.Index))
            .Append("Exception", FormatException(error.Exception))
            .ToString();
    }

    public virtual string Format<T>(CollectionAllPredicateAssertionError<T> error)
    {
        EnsureObservedItems(error.ActualValue, GetMaxFormattedIndex(error.Index));

        return CreateMessage($"Assert.All() assertion failed: Item at index {error.Index} did not satisfy the predicate.", error.Message)
            .AppendGroup(
                ("Expression", error.ActualExpression),
                ("Predicate expression", error.PredicateExpression))
            .Append("Actual", FormatValue(error.ActualValue.Items, error.Index))
            .ToString();
    }

    public virtual string Format<T>(CollectionDoesNotAllPredicateAssertionError<T> error)
    {
        EnsureObservedItems(error.ActualValue, GetMaxFormattedIndex(highlightedIndex: null));

        return CreateMessage("Assert.DoesNotAll() assertion failed: All items satisfy the predicate, but expected at least one that does not.", error.Message)
            .AppendGroup(
                ("Expression", error.ActualExpression),
                ("Predicate expression", error.PredicateExpression))
            .Append("Actual", FormatValue(error.ActualValue.Items))
            .ToString();
    }

    public virtual string Format<T>(CollectionAllAssertionError<T> error)
    {
        EnsureObservedItems(error.ActualValue, GetMaxFormattedIndex(error.Index));

        return CreateMessage($"Assert.All() assertion failed: Item at index {error.Index} failed.", error.Message)
            .AppendGroup(
                ("Expression", error.ActualExpression),
                ("Assertion expression", error.AssertionExpression))
            .Append("Actual", FormatValue(error.ActualValue.Items, error.Index))
            .Append("Exception", FormatException(error.Exception))
            .ToString();
    }

    public virtual async Task<string> FormatAsync<T>(AsyncCollectionAllAssertionError<T> error)
    {
        await EnsureObservedItemsAsync(error.ActualValue, GetMaxFormattedIndex(error.Index)).ConfigureAwait(false);

        return CreateMessage($"Assert.All() assertion failed: Item at index {error.Index} failed.", error.Message)
            .AppendGroup(
                ("Expression", error.ActualExpression),
                ("Assertion expression", error.AssertionExpression))
            .Append("Actual", FormatValue(error.ActualValue.Items, error.Index))
            .Append("Exception", FormatException(error.Exception))
            .ToString();
    }

    public virtual string Format<T>(ReadOnlySpanDistinctAssertionError<T> error)
    {
        return CreateMessage($"Assert.Distinct() assertion failed: Duplicate item found at index {error.DuplicateIndex}.", error.Message)
            .Append("Expression", error.ActualExpression)
            .AppendGroup(
                ("First index", error.FirstIndex.ToString(CultureInfo.InvariantCulture)),
                ("Duplicate index", error.DuplicateIndex.ToString(CultureInfo.InvariantCulture)))
            .Append("Actual", FormatReadOnlySpanValue(error.ActualValue, error.DuplicateIndex))
            .ToString();
    }

    public virtual string Format<T>(CollectionDistinctAssertionError<T> error)
    {
        EnsureObservedItems(error.ActualValue, GetMaxFormattedIndex(error.DuplicateIndex));

        return CreateMessage($"Assert.Distinct() assertion failed: Duplicate item found at index {error.DuplicateIndex}.", error.Message)
            .Append("Expression", error.ActualExpression)
            .AppendGroup(
                ("First index", error.FirstIndex.ToString(CultureInfo.InvariantCulture)),
                ("Duplicate index", error.DuplicateIndex.ToString(CultureInfo.InvariantCulture)))
            .Append("Actual", FormatValue(error.ActualValue.Items, error.DuplicateIndex))
            .ToString();
    }

    public virtual async Task<string> FormatAsync<T>(AsyncCollectionDistinctAssertionError<T> error)
    {
        await EnsureObservedItemsAsync(error.ActualValue, GetMaxFormattedIndex(error.DuplicateIndex)).ConfigureAwait(false);

        return CreateMessage($"Assert.Distinct() assertion failed: Duplicate item found at index {error.DuplicateIndex}.", error.Message)
            .Append("Expression", error.ActualExpression)
            .AppendGroup(
                ("First index", error.FirstIndex.ToString(CultureInfo.InvariantCulture)),
                ("Duplicate index", error.DuplicateIndex.ToString(CultureInfo.InvariantCulture)))
            .Append("Actual", FormatValue(error.ActualValue.Items, error.DuplicateIndex))
            .ToString();
    }

    public virtual string Format<T>(ReadOnlySpanCountAssertionError<T> error)
    {
        return CreateMessage($"Assert.{error.AssertionName}() assertion failed.", error.Message)
            .Append("Expression", error.ActualExpression)
            .AppendGroup(
                ("Expected count", error.ExpectedCount.ToString(CultureInfo.InvariantCulture)),
                ("Actual count", error.ActualCount.ToString(CultureInfo.InvariantCulture)))
            .Append("Actual", FormatReadOnlySpanValue(error.ActualValue))
            .ToString();
    }

    public virtual string Format(StringCountAssertionError error)
    {
        return CreateMessage($"Assert.{error.AssertionName}() assertion failed.", error.Message)
            .Append("Expression", error.ActualExpression)
            .AppendGroup(
                ("Expected count", error.ExpectedCount.ToString(CultureInfo.InvariantCulture)),
                ("Actual count", error.ActualCount.ToString(CultureInfo.InvariantCulture)))
            .Append("Actual", FormatStringValue(error.ActualValue, highlightedIndex: null))
            .ToString();
    }

    public virtual string Format<T>(CollectionCountAssertionError<T> error)
    {
        EnsureObservedItems(error.ActualValue, GetMaxFormattedIndex(highlightedIndex: null));

        return CreateMessage($"Assert.{error.AssertionName}() assertion failed.", error.Message)
            .Append("Expression", error.ActualExpression)
            .AppendGroup(
                ("Expected count", error.ExpectedCount.ToString(CultureInfo.InvariantCulture)),
                ("Actual count", error.ActualCount.ToString(CultureInfo.InvariantCulture)))
            .Append("Actual", FormatValue(error.ActualValue.Items))
            .ToString();
    }

    public virtual async Task<string> FormatAsync<T>(AsyncCollectionCountAssertionError<T> error)
    {
        await EnsureObservedItemsAsync(error.ActualValue, GetMaxFormattedIndex(highlightedIndex: null)).ConfigureAwait(false);

        return CreateMessage($"Assert.{error.AssertionName}() assertion failed.", error.Message)
            .Append("Expression", error.ActualExpression)
            .AppendGroup(
                ("Expected count", error.ExpectedCount.ToString(CultureInfo.InvariantCulture)),
                ("Actual count", error.ActualCount.ToString(CultureInfo.InvariantCulture)))
            .Append("Actual", FormatValue(error.ActualValue.Items))
            .ToString();
    }

    public virtual string Format<T>(ValueContainsAssertionError<T> error)
    {
        return CreateMessage("Assert.Contains() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .AppendGroup(
                ("Expected item", FormatValue(error.ExpectedValue)),
                ("Actual", FormatReadOnlySpanValue(error.ActualValue)))
            .ToString();
    }

    public virtual string Format<T>(ValueCollectionContainsAssertionError<T> error)
    {
        EnsureObservedItems(error.ActualValue, GetMaxFormattedIndex(highlightedIndex: null));

        return CreateMessage("Assert.Contains() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .AppendGroup(
                ("Expected item", FormatValue(error.ExpectedValue)),
                ("Actual", FormatValue(error.ActualValue.Items)))
            .ToString();
    }

    public virtual string Format<TExpected>(NullActualAssertionError<TExpected> error)
    {
        return CreateMessage($"Assert.{error.AssertionName}() assertion failed.", error.Message)
            .AppendGroup(
                (error.ExpectedExpressionLabel, error.ExpectedExpression ?? string.Empty),
                ("Actual expression", error.ActualExpression ?? string.Empty))
            .AppendGroup(
                (error.ExpectedValueLabel, FormatValue(error.ExpectedValue)),
                ("Actual", "<null>"))
            .ToString();
    }

    public virtual string Format<TKey, TValue>(KeyValuePairCollectionContainsAssertionError<TKey, TValue> error)
    {
        EnsureObservedItems(error.ActualValue, MaxFormattedItems - 1);

        return CreateMessage("Assert.Contains() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected key expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .AppendGroup(
                ("Expected key", FormatValue(error.ExpectedKey)),
                ("Actual", FormatKeyValuePairs(error.ActualValue.Items)))
            .ToString();
    }

    public virtual string Format(DictionaryContainsAssertionError error)
    {
        return CreateMessage("Assert.Contains() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected key expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .AppendGroup(
                ("Expected key", FormatValue(error.ExpectedKey)),
                ("Actual", FormatDictionary(error.ActualValue)))
            .ToString();
    }

    public virtual string Format<T>(ReadOnlySpanContainsAssertionError<T> error)
    {
        return CreateMessage("Assert.Contains() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .AppendGroup(
                ("Expected", FormatReadOnlySpanValue(error.ExpectedValue)),
                ("Actual", FormatReadOnlySpanValue(error.ActualValue)))
            .ToString();
    }

    public virtual string Format(ReadOnlySpanCharContainsAssertionError error)
    {
        return CreateMessage("Assert.Contains() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .Append("Comparison", error.Comparison.ToString())
            .AppendGroup(
                ("Expected", FormatStringValue(error.ExpectedValue, highlightedIndex: null)),
                ("Actual", FormatStringValue(error.ActualValue, highlightedIndex: null)))
            .ToString();
    }

    public virtual string Format(StringNullActualAssertionError error)
    {
        return CreateMessage($"Assert.{error.AssertionName}() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .Append("Comparison", error.Comparison.ToString())
            .AppendGroup(
                (error.ExpectedValueLabel, FormatValue(error.ExpectedValue)),
                ("Actual", "<null>"))
            .ToString();
    }

    public virtual async Task<string> FormatAsync<TExpected, TActual>(CollectionAsyncCollectionContainsAssertionError<TExpected, TActual> error)
    {
        await EnsureObservedItemsAsync(error.ActualValue, GetMaxFormattedIndex(highlightedIndex: null)).ConfigureAwait(false);

        return CreateMessage("Assert.Contains() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .AppendGroup(
                ("Expected", FormatValue(error.ExpectedValue.Items)),
                ("Actual", FormatValue(error.ActualValue.Items)))
            .ToString();
    }

    public virtual string Format<TExpected, TActual>(CollectionContainsAssertionError<TExpected, TActual> error)
    {
        EnsureObservedItems(error.ActualValue, GetMaxFormattedIndex(highlightedIndex: null));

        return CreateMessage("Assert.Contains() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .AppendGroup(
                ("Expected", FormatValue(error.ExpectedValue.Items)),
                ("Actual", FormatValue(error.ActualValue.Items)))
            .ToString();
    }

    public virtual string Format<T>(ValueEndsWithAssertionError<T> error)
    {
        return CreateMessage("Assert.EndsWith() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .AppendGroup(
                ("Expected suffix", FormatValue(error.ExpectedValue)),
                ("Actual", FormatReadOnlySpanValue(error.ActualValue, error.ActualValue.IsEmpty ? null : error.ActualValue.Length - 1)))
            .ToString();
    }

    public virtual string Format<T>(ValueCollectionEndsWithAssertionError<T> error)
    {
        var highlightedIndex = error.ActualValue.Items.Count > 0 ? error.ActualValue.Items.Count - 1 : (int?)null;

        return CreateMessage("Assert.EndsWith() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .AppendGroup(
                ("Expected suffix", FormatValue(error.ExpectedValue)),
                ("Actual", FormatValue(error.ActualValue.Items, highlightedIndex)))
            .ToString();
    }

    public virtual string Format<T>(ReadOnlySpanEndsWithAssertionError<T> error)
    {
        var actualIndex = GetActualSuffixIndex(error.ExpectedValue.Length, error.ActualValue.Length, error.FirstDifferenceIndex);

        return CreateMessage("Assert.EndsWith() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .Append("Index of first difference", error.FirstDifferenceIndex.ToString(CultureInfo.InvariantCulture))
            .AppendGroup(
                ("Expected suffix", FormatReadOnlySpanValue(error.ExpectedValue, error.FirstDifferenceIndex < error.ExpectedValue.Length ? error.FirstDifferenceIndex : null)),
                ("Actual", FormatReadOnlySpanValue(error.ActualValue, actualIndex)))
            .ToString();
    }

    public virtual string Format(ReadOnlySpanCharEndsWithAssertionError error)
    {
        var actualIndex = GetActualSuffixIndex(error.ExpectedValue.Length, error.ActualValue.Length, error.FirstDifferenceIndex);

        return CreateMessage("Assert.EndsWith() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .Append("Comparison", error.Comparison.ToString())
            .Append("Index of first difference", error.FirstDifferenceIndex.ToString(CultureInfo.InvariantCulture))
            .AppendGroup(
                ("Expected suffix", FormatStringValue(error.ExpectedValue, error.FirstDifferenceIndex < error.ExpectedValue.Length ? error.FirstDifferenceIndex : null)),
                ("Actual", FormatStringValue(error.ActualValue, actualIndex)))
            .ToString();
    }

    public virtual async Task<string> FormatAsync<TExpected, TActual>(CollectionAsyncCollectionEndsWithAssertionError<TExpected, TActual> error)
    {
        await EnsureObservedItemsAsync(error.ActualValue, int.MaxValue - 1).ConfigureAwait(false);
        var actualIndex = GetActualSuffixIndex(error.ExpectedValue.Items.Count, error.ActualValue.Items.Count, error.FirstDifferenceIndex);

        return CreateMessage("Assert.EndsWith() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .Append("Index of first difference", error.FirstDifferenceIndex.ToString(CultureInfo.InvariantCulture))
            .AppendGroup(
                ("Expected suffix", FormatValue(error.ExpectedValue.Items, error.FirstDifferenceIndex < error.ExpectedValue.Items.Count ? error.FirstDifferenceIndex : null)),
                ("Actual", FormatValue(error.ActualValue.Items, actualIndex)))
            .ToString();
    }

    public virtual string Format<TExpected, TActual>(CollectionEndsWithAssertionError<TExpected, TActual> error)
    {
        var actualIndex = GetActualSuffixIndex(error.ExpectedValue.Items.Count, error.ActualValue.Items.Count, error.FirstDifferenceIndex);

        return CreateMessage("Assert.EndsWith() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .Append("Index of first difference", error.FirstDifferenceIndex.ToString(CultureInfo.InvariantCulture))
            .AppendGroup(
                ("Expected suffix", FormatValue(error.ExpectedValue.Items, error.FirstDifferenceIndex < error.ExpectedValue.Items.Count ? error.FirstDifferenceIndex : null)),
                ("Actual", FormatValue(error.ActualValue.Items, actualIndex)))
            .ToString();
    }

    public virtual string Format<T>(ReadOnlySpanStartsWithAssertionError<T> error)
    {
        return CreateMessage("Assert.StartsWith() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .Append("Index of first difference", error.FirstDifferenceIndex.ToString(CultureInfo.InvariantCulture))
            .AppendGroup(
                ("Expected prefix", FormatReadOnlySpanValue(error.ExpectedValue, error.FirstDifferenceIndex)),
                ("Actual", FormatReadOnlySpanValue(error.ActualValue, error.FirstDifferenceIndex < error.ActualValue.Length ? error.FirstDifferenceIndex : null)))
            .ToString();
    }

    public virtual string Format(ReadOnlySpanCharStartsWithAssertionError error)
    {
        return CreateMessage("Assert.StartsWith() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .Append("Comparison", error.Comparison.ToString())
            .Append("Index of first difference", error.FirstDifferenceIndex.ToString(CultureInfo.InvariantCulture))
            .AppendGroup(
                ("Expected prefix", FormatStringValue(error.ExpectedValue, error.FirstDifferenceIndex)),
                ("Actual", FormatStringValue(error.ActualValue, error.FirstDifferenceIndex < error.ActualValue.Length ? error.FirstDifferenceIndex : null)))
            .ToString();
    }

    public virtual async Task<string> FormatAsync<T>(AsyncCollectionStartsWithAssertionError<T> error)
    {
        var maxIndex = GetMaxFormattedIndex(error.FirstDifferenceIndex);
        await EnsureObservedItemsAsync(error.ExpectedValue, maxIndex).ConfigureAwait(false);
        await EnsureObservedItemsAsync(error.ActualValue, maxIndex).ConfigureAwait(false);

        return CreateMessage("Assert.StartsWith() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .Append("Index of first difference", error.FirstDifferenceIndex.ToString(CultureInfo.InvariantCulture))
            .AppendGroup(
                ("Expected prefix", FormatValue(error.ExpectedValue.Items, error.FirstDifferenceIndex)),
                ("Actual", FormatValue(error.ActualValue.Items, error.FirstDifferenceIndex < error.ActualValue.Items.Count ? error.FirstDifferenceIndex : null)))
            .ToString();
    }

    public virtual async Task<string> FormatAsync<TExpected, TActual>(CollectionAsyncCollectionStartsWithAssertionError<TExpected, TActual> error)
    {
        var maxIndex = GetMaxFormattedIndex(error.FirstDifferenceIndex);
        EnsureObservedItems(error.ExpectedValue, maxIndex);
        await EnsureObservedItemsAsync(error.ActualValue, maxIndex).ConfigureAwait(false);

        return CreateMessage("Assert.StartsWith() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .Append("Index of first difference", error.FirstDifferenceIndex.ToString(CultureInfo.InvariantCulture))
            .AppendGroup(
                ("Expected prefix", FormatValue(error.ExpectedValue.Items, error.FirstDifferenceIndex)),
                ("Actual", FormatValue(error.ActualValue.Items, error.FirstDifferenceIndex < error.ActualValue.Items.Count ? error.FirstDifferenceIndex : null)))
            .ToString();
    }

    public virtual string Format<TExpected, TActual>(CollectionStartsWithAssertionError<TExpected, TActual> error)
    {
        var maxIndex = GetMaxFormattedIndex(error.FirstDifferenceIndex);
        EnsureObservedItems(error.ExpectedValue, maxIndex);
        EnsureObservedItems(error.ActualValue, maxIndex);

        return CreateMessage("Assert.StartsWith() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .Append("Index of first difference", error.FirstDifferenceIndex.ToString(CultureInfo.InvariantCulture))
            .AppendGroup(
                ("Expected prefix", FormatValue(error.ExpectedValue.Items, error.FirstDifferenceIndex)),
                ("Actual", FormatValue(error.ActualValue.Items, error.FirstDifferenceIndex < error.ActualValue.Items.Count ? error.FirstDifferenceIndex : null)))
            .ToString();
    }

    public virtual string Format<TExpected, TActual>(CollectionEqualAssertionError<TExpected, TActual> error)
    {
        // Both snapshots were read up to the first difference by the assertion, so this does not enumerate further.
        var itemDiffers = error.ExpectedValue.TryGetItem(error.FirstDifferenceIndex, out _) && error.ActualValue.TryGetItem(error.FirstDifferenceIndex, out _);

        return CreateMessage(GetCollectionEqualHeader(itemDiffers, error.FirstDifferenceIndex), error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .Append("Index of first difference", error.FirstDifferenceIndex.ToString(CultureInfo.InvariantCulture))
            .AppendGroup(
                ("Expected", FormatValue(error.ExpectedValue, error.FirstDifferenceIndex)),
                ("Actual", FormatValue(error.ActualValue, error.FirstDifferenceIndex)))
            .ToString();
    }

    public virtual string Format<TExpected, TActual>(CollectionEqualUnorderedAssertionError<TExpected, TActual> error)
    {
        var builder = CreateMessage("Assert.EqualUnordered() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression));

        if (error.MissingExpectedIndex is not null)
        {
            builder.Append("Missing expected item index", error.MissingExpectedIndex.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (error.UnexpectedActualIndex is not null)
        {
            builder.Append("Unexpected actual item index", error.UnexpectedActualIndex.Value.ToString(CultureInfo.InvariantCulture));
        }

        return builder
            .AppendGroup(
                ("Expected", FormatValue(error.ExpectedValue.Items, error.MissingExpectedIndex)),
                ("Actual", FormatValue(error.ActualValue.Items, error.UnexpectedActualIndex)))
            .ToString();
    }

    public virtual async Task<string> FormatAsync<TExpected, TActual>(AsyncCollectionEqualAssertionError<TExpected, TActual> error)
    {
        var maxIndex = GetMaxFormattedIndex(error.FirstDifferenceIndex);
        await EnsureObservedItemsAsync(error.ExpectedValue, maxIndex).ConfigureAwait(false);
        await EnsureObservedItemsAsync(error.ActualValue, maxIndex).ConfigureAwait(false);
        var itemDiffers = error.ExpectedValue.Items.Count > error.FirstDifferenceIndex && error.ActualValue.Items.Count > error.FirstDifferenceIndex;

        return CreateMessage(GetCollectionEqualHeader(itemDiffers, error.FirstDifferenceIndex), error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .Append("Index of first difference", error.FirstDifferenceIndex.ToString(CultureInfo.InvariantCulture))
            .AppendGroup(
                ("Expected", FormatValue(error.ExpectedValue.Items, error.FirstDifferenceIndex)),
                ("Actual", FormatValue(error.ActualValue.Items, error.FirstDifferenceIndex)))
            .ToString();
    }

    public virtual Task<string> FormatAsync<TExpected, TActual>(AsyncCollectionEqualUnorderedAssertionError<TExpected, TActual> error)
    {
        var builder = CreateMessage("Assert.EqualUnordered() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression));

        if (error.MissingExpectedIndex is not null)
        {
            builder.Append("Missing expected item index", error.MissingExpectedIndex.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (error.UnexpectedActualIndex is not null)
        {
            builder.Append("Unexpected actual item index", error.UnexpectedActualIndex.Value.ToString(CultureInfo.InvariantCulture));
        }

        return Task.FromResult(builder
            .AppendGroup(
                ("Expected", FormatValue(error.ExpectedValue.Items, error.MissingExpectedIndex)),
                ("Actual", FormatValue(error.ActualValue.Items, error.UnexpectedActualIndex)))
            .ToString());
    }

    protected virtual string FormatValue(object? value, int? highlightedIndex = null)
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        return FormatValue(value, highlightedIndex, visited);
    }

    protected virtual string FormatValue(object? value, int? highlightedIndex, HashSet<object> visited)
    {
        if (value is null)
            return "<null>";

        if (value is string stringValue)
            return FormatStringValue(stringValue, highlightedIndex);

        if (value is char charValue)
            return FormatCharValue(charValue);

        if (TryFormatMemoryValue(value, highlightedIndex, visited, out var memoryValue))
            return memoryValue;

        if (value is System.Collections.IEnumerable enumerable)
        {
            return FormatEnumerableValue(enumerable, highlightedIndex, visited);
        }

        if (TryFormatKeyValuePairOrTupleValue(value, visited, out var structuredValue))
            return structuredValue;

        return FormatScalarValue(value);
    }

    private static string FormatScalarValue(object value)
    {
        // ToString runs user code, which can use the current culture (records, anonymous types) or throw. Neither may
        // change or replace the assertion failure, so the culture is pinned and exceptions become part of the message.
        var previousCulture = CultureInfo.CurrentCulture;
        var changeCulture = !ReferenceEquals(previousCulture, CultureInfo.InvariantCulture);
        try
        {
            if (changeCulture)
            {
                CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            }

            return value switch
            {
                // The default formats of these types drop fractional seconds (or seconds for TimeOnly), so two different
                // values could print identically. The round-trip format is lossless.
                DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
                DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
                DateOnly dateOnly => dateOnly.ToString("O", CultureInfo.InvariantCulture),
                TimeOnly timeOnly => timeOnly.ToString("O", CultureInfo.InvariantCulture),
                IFormattable formattable => formattable.ToString(format: null, CultureInfo.InvariantCulture) ?? string.Empty,
                _ => value.ToString() ?? string.Empty,
            };
        }
        catch (Exception exception)
        {
            return FormatUserCodeException("ToString()", exception);
        }
        finally
        {
            if (changeCulture)
            {
                CultureInfo.CurrentCulture = previousCulture;
            }
        }
    }

    private static string FormatUserCodeException(string operation, Exception exception)
    {
        string? message;
        try
        {
            message = exception.Message;
        }
        catch (Exception)
        {
            message = null;
        }

        var result = new StringBuilder();
        result.Append('<').Append(operation).Append(" threw ").Append(exception.GetType().FullName);
        if (!string.IsNullOrEmpty(message))
        {
            result.Append(": ");
            AppendEscapedText(result, message, highlightedIndex: null, quote: null);
        }

        result.Append('>');
        return result.ToString();
    }

    private bool TryFormatMemoryValue(object value, int? highlightedIndex, HashSet<object> visited, [NotNullWhen(true)] out string? result)
    {
        result = null;
        var type = value.GetType();
        if (!type.IsConstructedGenericType)
            return false;

        var definition = type.GetGenericTypeDefinition();
        if (definition != typeof(ReadOnlyMemory<>) && definition != typeof(Memory<>))
            return false;

        // A boxed memory can be reached again through its own content (object[] holding a boxed memory over itself).
        if (!visited.Add(value))
        {
            result = "<circular reference>";
            return true;
        }

        try
        {
            var method = FormatBoxedMemoryValueMethod.MakeGenericMethod(type.GetGenericArguments()[0]);
            result = (string)method.Invoke(this, BindingFlags.DoNotWrapExceptions, binder: null, [value, highlightedIndex, visited], culture: null)!;
            return true;
        }
        finally
        {
            visited.Remove(value);
        }
    }

    private string FormatBoxedMemoryValue<T>(object value, int? highlightedIndex, HashSet<object> visited)
    {
        ReadOnlyMemory<T> memory = value is Memory<T> writableMemory ? writableMemory : (ReadOnlyMemory<T>)value;
        return FormatReadOnlySpanValue(memory.Span, highlightedIndex, visited);
    }

    private bool TryFormatKeyValuePairOrTupleValue(object value, HashSet<object> visited, [NotNullWhen(true)] out string? result)
    {
        // KeyValuePair and tuples are formatted from their parts, so nested values get the same invariant formatting,
        // quoting and escaping as top-level values instead of whatever their own ToString produces.
        var type = value.GetType();
        if (type.IsConstructedGenericType && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
        {
            var key = type.GetProperty(nameof(KeyValuePair<,>.Key))!.GetValue(value);
            var pairValue = type.GetProperty(nameof(KeyValuePair<,>.Value))!.GetValue(value);
            result = "[" + FormatValue(key, highlightedIndex: null, visited) + ", " + FormatValue(pairValue, highlightedIndex: null, visited) + "]";
            return true;
        }

        if (value is ITuple tuple && IsSystemTupleType(type))
        {
            var builder = new StringBuilder();
            builder.Append('(');
            for (var i = 0; i < tuple.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(FormatValue(tuple[i], highlightedIndex: null, visited));
            }

            builder.Append(')');
            result = builder.ToString();
            return true;
        }

        result = null;
        return false;

        static bool IsSystemTupleType(Type type)
        {
            if (!type.IsConstructedGenericType || type.Assembly != typeof(object).Assembly)
                return false;

            var name = type.GetGenericTypeDefinition().FullName;
            return name is not null && (name.StartsWith("System.ValueTuple`", StringComparison.Ordinal) || name.StartsWith("System.Tuple`", StringComparison.Ordinal));
        }
    }

    private string FormatStructuralValue(object? value)
    {
        if (value is StructuralMissingValue)
            return "<missing>";

        return FormatValue(value);
    }

    private static string GetCollectionEqualHeader(bool itemDiffers, int firstDifferenceIndex)
    {
        return itemDiffers
            ? $"Assert.Equal() assertion failed: Item at index {firstDifferenceIndex.ToString(CultureInfo.InvariantCulture)} differs."
            : "Assert.Equal() assertion failed: Lengths differ.";
    }

    private static string FormatType(Type? type)
    {
        return type?.FullName ?? "<null>";
    }

    internal static string FormatExpression(string? expression)
    {
        return string.IsNullOrEmpty(expression) ? "<actual>" : expression;
    }

    protected virtual string FormatReadOnlySpanValue<T>(ReadOnlySpan<T> value, int? highlightedIndex = null)
    {
        return FormatReadOnlySpanValue(value, highlightedIndex, new HashSet<object>(ReferenceEqualityComparer.Instance));
    }

    private string FormatReadOnlySpanValue<T>(ReadOnlySpan<T> value, int? highlightedIndex, HashSet<object> visited)
    {
        if (typeof(T) == typeof(char))
        {
            var chars = unsafe(System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(ref System.Runtime.CompilerServices.Unsafe.As<T, char>(ref System.Runtime.InteropServices.MemoryMarshal.GetReference(value)), value.Length));
            return FormatStringValue(chars, highlightedIndex);
        }

        // Same window as enumerables: a span can be arbitrarily large, and only the items around the highlighted one are useful.
        var window = GetItemWindow(highlightedIndex);
        var items = new List<string>();
        var hasSkippedItems = false;
        for (var index = 0; index < value.Length; index++)
        {
            if (index > window.MaxIndex)
            {
                items.Add("...");
                hasSkippedItems = false;
                break;
            }

            if (!window.IsVisible(index))
            {
                hasSkippedItems = true;
                index = window.FocusStartIndex - 1;
                continue;
            }

            if (hasSkippedItems)
            {
                items.Add("...");
                hasSkippedItems = false;
            }

            items.Add(FormatHighlightedValue(FormatValue(value[index], highlightedIndex: null, visited), index, highlightedIndex));
        }

        if (hasSkippedItems)
        {
            items.Add("...");
        }

        return $"[{string.Join(", ", items)}]";
    }

    private string FormatKeyValuePairs<TKey, TValue>(IEnumerable<KeyValuePair<TKey, TValue>> value)
    {
        return FormatKeyValueEntries(value, useDictionaryEnumerator: false, static item => item is KeyValuePair<TKey, TValue> pair ? (pair.Key, pair.Value) : default);
    }

    private string FormatDictionary(System.Collections.IDictionary value)
    {
        // A dictionary enumerates DictionaryEntry values only through IDictionary.GetEnumerator.
        return FormatKeyValueEntries(value, useDictionaryEnumerator: true, static item => item is System.Collections.DictionaryEntry entry ? (entry.Key, entry.Value) : default);
    }

    private string FormatKeyValueEntries(System.Collections.IEnumerable value, bool useDictionaryEnumerator, Func<object?, (object? Key, object? Value)> getEntry)
    {
        var items = new List<string>();
        var index = 0;
        var isTruncated = false;
        var enumerator = GetEnumerator(value, out var enumerationError, useDictionaryEnumerator);
        try
        {
            while (enumerator is not null)
            {
                if (!TryMoveNext(enumerator, out var item, out enumerationError))
                    break;

                if (index >= MaxFormattedItems)
                {
                    items.Add("...");
                    isTruncated = true;
                    break;
                }

                var (key, entryValue) = getEntry(item);
                items.Add(FormatValue(key) + ": " + FormatValue(entryValue));
                index++;
            }
        }
        finally
        {
            DisposeEnumerator(enumerator);
        }

        if (!isTruncated)
        {
            AddEnumerationError(items, value, enumerationError);
        }

        return $"[{string.Join(", ", items)}]";
    }

    protected virtual string FormatEnumerableValue(System.Collections.IEnumerable value, int? highlightedIndex, HashSet<object> visited)
    {
        if (!visited.Add(value))
            return "<circular reference>";

        System.Collections.IEnumerator? enumerator = null;
        try
        {
            var items = new List<string>();
            var window = GetItemWindow(highlightedIndex);
            var hasSkippedItems = false;
            var isTruncated = false;
            var index = 0;

            enumerator = GetEnumerator(value, out var enumerationError);
            while (enumerator is not null)
            {
                if (!TryMoveNext(enumerator, out var item, out enumerationError))
                    break;

                if (index > window.MaxIndex)
                {
                    items.Add("...");
                    isTruncated = true;
                    break;
                }

                if (window.IsVisible(index))
                {
                    if (hasSkippedItems)
                    {
                        items.Add("...");
                        hasSkippedItems = false;
                    }

                    items.Add(FormatHighlightedValue(FormatValue(item, highlightedIndex: null, visited), index, highlightedIndex));
                }
                else
                {
                    hasSkippedItems = true;
                }

                index++;
            }

            if (!isTruncated)
            {
                // The sequence ended while items were being skipped: without a marker, the list would look complete.
                if (hasSkippedItems)
                {
                    items.Add("...");
                }

                AddEnumerationError(items, value, enumerationError);
            }

            return $"[{string.Join(", ", items)}]";
        }
        finally
        {
            DisposeEnumerator(enumerator);
            visited.Remove(value);
        }
    }

    private static System.Collections.IEnumerator? GetEnumerator(System.Collections.IEnumerable value, out Exception? exception, bool useDictionaryEnumerator = false)
    {
        try
        {
            exception = null;
            return useDictionaryEnumerator && value is System.Collections.IDictionary dictionary ? dictionary.GetEnumerator() : value.GetEnumerator();
        }
        catch (Exception ex)
        {
            exception = ex;
            return null;
        }
    }

    private static bool TryMoveNext(System.Collections.IEnumerator enumerator, out object? item, out Exception? exception)
    {
        try
        {
            exception = null;
            if (enumerator.MoveNext())
            {
                item = enumerator.Current;
                return true;
            }
        }
        catch (Exception ex)
        {
            exception = ex;
        }

        item = null;
        return false;
    }

    private static void DisposeEnumerator(System.Collections.IEnumerator? enumerator)
    {
        try
        {
            (enumerator as IDisposable)?.Dispose();
        }
        catch (Exception)
        {
            // The failure being reported matters more than an enumerator that cannot clean up.
        }
    }

    private static void AddEnumerationError(List<string> items, System.Collections.IEnumerable value, Exception? exception)
    {
        if (exception is not null || ObservationExceptions.TryGetValue(value, out exception))
        {
            items.Add(FormatUserCodeException("enumeration", exception));
        }
    }

    private bool IsFocusedHighlightedItem(int? highlightedIndex)
    {
        return highlightedIndex is not null && highlightedIndex.GetValueOrDefault() >= MaxFormattedItems;
    }

    private int GetMaxFormattedIndex(int? highlightedIndex)
    {
        if (IsFocusedHighlightedItem(highlightedIndex))
            return highlightedIndex.GetValueOrDefault() + HighlightedContextItemCount;

        return Math.Max(MaxFormattedItems - 1, highlightedIndex.GetValueOrDefault(-1) + SuffixItemCount);
    }

    private ItemWindow GetItemWindow(int? highlightedIndex)
    {
        if (IsFocusedHighlightedItem(highlightedIndex))
        {
            var focusStartIndex = Math.Max(PrefixItemCount, highlightedIndex.GetValueOrDefault() - HighlightedContextItemCount);
            return new ItemWindow(PrefixItemCount, focusStartIndex, GetMaxFormattedIndex(highlightedIndex));
        }

        return new ItemWindow(MaxFormattedItems, FocusStartIndex: -1, GetMaxFormattedIndex(highlightedIndex));
    }

    private static int? GetActualSuffixIndex(int expectedLength, int actualLength, int firstDifferenceIndex)
    {
        if (firstDifferenceIndex >= expectedLength)
            return null;

        var actualIndex = actualLength - expectedLength + firstDifferenceIndex;
        if (actualIndex < 0 || actualIndex >= actualLength)
            return null;

        return actualIndex;
    }

    private static int? GetSingleFailureHighlightedIndex(int count)
    {
        return count > 1 ? 1 : null;
    }

    private static string FormatException(Exception? exception)
    {
        if (exception is null)
            return "<none>";

        string message;
        try
        {
            message = exception.Message;
        }
        catch (Exception ex)
        {
            return FormatUserCodeException("Message", ex);
        }

        if (string.IsNullOrEmpty(message))
            return exception.GetType().FullName ?? exception.GetType().Name;

        return message.Replace("\n", "\n           ", StringComparison.Ordinal);
    }

    /// <summary>
    /// Observes only the items a failure message can show, so formatting a long or infinite sequence does not
    /// materialize it. The returned items format the same way as the complete sequence would.
    /// </summary>
    internal IReadOnlyList<T> GetFormattedItems<T>(CollectionSnapshot<T> snapshot)
    {
        EnsureObservedItems(snapshot, MaxFormattedItems - 1);

        return snapshot.Items;
    }

    /// <inheritdoc cref="GetFormattedItems{T}(CollectionSnapshot{T})"/>
    internal async Task<IReadOnlyList<T>> GetFormattedItemsAsync<T>(AsyncCollectionSnapshot<T> snapshot)
    {
        await EnsureObservedItemsAsync(snapshot, MaxFormattedItems - 1).ConfigureAwait(false);

        return snapshot.Items;
    }

    private static void EnsureObservedItems<T>(CollectionSnapshot<T> snapshot, int maxIndex)
    {
        if (snapshot.IsComplete || snapshot.ObservedCount > maxIndex + 1)
            return;

        try
        {
            for (var index = snapshot.ObservedCount; !snapshot.IsComplete && snapshot.ObservedCount <= maxIndex + 1 && snapshot.TryGetItem(index, out _); index++)
            {
            }
        }
        catch (Exception exception)
        {
            // Reading past what the assertion needed is only for the message, so a failing sequence must not replace
            // the assertion failure. The exception is written after the observed items instead.
            ObservationExceptions.AddOrUpdate(snapshot.Items, exception);
        }
    }

    private static async Task EnsureObservedItemsAsync<T>(AsyncCollectionSnapshot<T> snapshot, int maxIndex)
    {
        if (snapshot.IsComplete || snapshot.ObservedCount > maxIndex + 1)
            return;

        try
        {
            for (var index = snapshot.ObservedCount; !snapshot.IsComplete && snapshot.ObservedCount <= maxIndex + 1 && await snapshot.TryGetItem(index).ConfigureAwait(false) is (true, _); index++)
            {
            }
        }
        catch (Exception exception)
        {
            ObservationExceptions.AddOrUpdate(snapshot.Items, exception);
        }
    }

    private static string FormatHighlightedValue(string value, int index, int? highlightedIndex)
    {
        if (index == highlightedIndex)
            return Underline(value);

        return value;
    }

    private static string Underline(string value)
    {
        var result = new StringBuilder(value.Length * 2);
        for (var i = 0; i < value.Length; i++)
        {
            result.Append(value[i]);

            // The combining mark goes after the whole surrogate pair: inserting it between the two halves would leave
            // lone surrogates in the message.
            if (char.IsHighSurrogate(value[i]) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                i++;
                result.Append(value[i]);
            }

            result.Append(CombiningLowLine);
        }

        return result.ToString();
    }

    internal static string FormatStringValue(string value, int? highlightedIndex)
    {
        return FormatStringValue(value.AsSpan(), highlightedIndex);
    }

    private static string FormatStringValue(ReadOnlySpan<char> value, int? highlightedIndex)
    {
        var result = new StringBuilder(value.Length + 2);
        result.Append('"');
        AppendEscapedText(result, value, highlightedIndex, quote: '"');
        result.Append('"');
        return result.ToString();
    }

    private static string FormatCharValue(char value)
    {
        var result = new StringBuilder(8);
        result.Append('\'');
        AppendEscapedText(result, [value], highlightedIndex: null, quote: '\'');
        result.Append('\'');
        return result.ToString();
    }

    private static void AppendEscapedText(StringBuilder result, ReadOnlySpan<char> value, int? highlightedIndex, char? quote)
    {
        for (var i = 0; i < value.Length; i++)
        {
            var start = result.Length;
            var isHighlighted = i == highlightedIndex;
            if (char.IsHighSurrogate(value[i]) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                // A surrogate pair is one character: it is never split, and highlighting either half highlights both.
                result.Append(value[i]).Append(value[i + 1]);
                i++;
                isHighlighted |= i == highlightedIndex;
            }
            else
            {
                AppendEscapedChar(result, value[i], quote);
            }

            if (isHighlighted)
            {
                var escaped = result.ToString(start, result.Length - start);
                result.Length = start;
                result.Append(Underline(escaped));
            }
        }

        static void AppendEscapedChar(StringBuilder result, char value, char? quote)
        {
            switch (value)
            {
                case '\r':
                    result.Append("\\r");
                    break;
                case '\n':
                    result.Append("\\n");
                    break;
                case '\t':
                    result.Append("\\t");
                    break;
                case '\\':
                    result.Append("\\\\");
                    break;
                case var _ when value == quote:
                    result.Append('\\').Append(value);
                    break;
                case var _ when IsInvisibleOrAmbiguous(value):
                    result.Append("\\u").Append(((int)value).ToString("X4", CultureInfo.InvariantCulture));
                    break;
                default:
                    result.Append(value);
                    break;
            }
        }

        // Characters that render as nothing, as a line break, or as something indistinguishable from a regular space.
        // Printing them raw would make different values look identical or break the message layout.
        static bool IsInvisibleOrAmbiguous(char value)
        {
            return char.GetUnicodeCategory(value) switch
            {
                UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator or UnicodeCategory.Surrogate => true,
                UnicodeCategory.SpaceSeparator => value != ' ',
                _ => false,
            };
        }
    }

    /// <summary>The items written for a sequence: a prefix, then everything from <see cref="FocusStartIndex"/> up to <see cref="MaxIndex"/>.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    private readonly record struct ItemWindow(int PrefixItemCount, int FocusStartIndex, int MaxIndex)
    {
        public bool IsVisible(int index) => index < PrefixItemCount || index >= FocusStartIndex;
    }
}
#pragma warning restore CA1822, CA1852
