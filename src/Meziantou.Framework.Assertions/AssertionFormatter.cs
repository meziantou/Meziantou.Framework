namespace Meziantou.Framework.Assertions;

#pragma warning disable CA1822, CA1852 // Formatter methods intentionally share an instance-based overridable shape.
internal class AssertionFormatter
{
    private const char CombiningLowLine = '\u0332';

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
                ("Actual", FormatReadOnlySpanValue(error.ActualValue.Span)))
            .ToString();
    }

    public virtual string Format<TExpected, TActual>(NegativeReadOnlySpanValueAssertionError<TExpected, TActual> error)
    {
        return CreateMessage($"Assert.{error.AssertionName}() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .AppendGroup(
                (error.NotExpectedLabel, FormatReadOnlySpanValue(error.ExpectedValue.Span)),
                ("Actual", FormatReadOnlySpanValue(error.ActualValue.Span)))
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
                ("Actual", FormatReadOnlySpanValue(error.ActualValue.Span)))
            .ToString();
    }

    public virtual string Format<T>(NegativeReadOnlySpanCountAssertionError<T> error)
    {
        return CreateMessage($"Assert.{error.AssertionName}() assertion failed.", error.Message)
            .Append("Expression", error.ActualExpression)
            .AppendGroup(
                ("Not expected count", error.NotExpectedCount.ToString(CultureInfo.InvariantCulture)),
                ("Actual count", error.ActualCount.ToString(CultureInfo.InvariantCulture)))
            .Append("Actual", FormatReadOnlySpanValue(error.ActualValue.Span))
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
        return CreateMessage("Assert.Equal() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .AppendGroup(
                ("Expected", FormatValue(error.ExpectedValue)),
                ("Actual", FormatValue(error.ActualValue)))
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
            .AppendGroup(
                ("Expected", FormatReadOnlySpanValue(error.ExpectedValue)),
                ("Actual", FormatReadOnlySpanValue(error.ActualValue)))
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
        EnsureObservedItems(error.ActualValue, MaxFormattedItems - 1);

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
        EnsureObservedItems(error.ActualValue, MaxFormattedItems - 1);

        return CreateMessage("Assert.Empty() assertion failed.", error.Message)
            .Append("Expression", error.ActualExpression)
            .Append("Actual", FormatValue(error.ActualValue.Items, error.ActualValue.Items.Count > 0 ? 0 : null))
            .ToString();
    }

    public virtual async Task<string> FormatAsync<T>(AsyncCollectionEmptyAssertionError<T> error)
    {
        await EnsureObservedItemsAsync(error.ActualValue, MaxFormattedItems - 1).ConfigureAwait(false);

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
        EnsureObservedItems(error.ActualValue, MaxFormattedItems - 1);

        return CreateMessage("Assert.Single() assertion failed.", error.Message)
            .Append("Expression", error.ActualExpression)
            .Append("Actual", FormatValue(error.ActualValue.Items, GetSingleFailureHighlightedIndex(error.ActualValue.Items.Count)))
            .ToString();
    }

    public virtual string Format<T>(CollectionSinglePredicateAssertionError<T> error)
    {
        EnsureObservedItems(error.MatchingValues, MaxFormattedItems - 1);

        return CreateMessage("Assert.Single() assertion failed.", error.Message)
            .AppendGroup(
                ("Expression", error.ActualExpression),
                ("Predicate expression", error.PredicateExpression))
            .Append("Matching items", FormatValue(error.MatchingValues.Items, GetSingleFailureHighlightedIndex(error.MatchingValues.Items.Count)))
            .ToString();
    }

    public virtual string Format<T>(CollectionContainsPredicateAssertionError<T> error)
    {
        EnsureObservedItems(error.MatchingValues, MaxFormattedItems - 1);

        return CreateMessage("Assert.Contains() assertion failed.", error.Message)
            .AppendGroup(
                ("Expression", error.ActualExpression),
                ("Predicate expression", error.PredicateExpression))
            .Append("Matching items", FormatValue(error.MatchingValues.Items))
            .ToString();
    }

    public virtual string Format(ContainsPredicateNullActualAssertionError error)
    {
        return CreateMessage("Assert.Contains() assertion failed.", error.Message)
            .AppendGroup(
                ("Expression", error.ActualExpression),
                ("Predicate expression", error.PredicateExpression))
            .Append("Actual", "<null>")
            .ToString();
    }

    public virtual string Format<T>(CollectionDoesNotContainPredicateAssertionError<T> error)
    {
        EnsureObservedItems(error.MatchingValues, MaxFormattedItems - 1);

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
        await EnsureObservedItemsAsync(error.ActualValue, MaxFormattedItems - 1).ConfigureAwait(false);

        return CreateMessage("Assert.Single() assertion failed.", error.Message)
            .Append("Expression", error.ActualExpression)
            .Append("Actual", FormatValue(error.ActualValue.Items, GetSingleFailureHighlightedIndex(error.ActualValue.Items.Count)))
            .ToString();
    }

    public virtual string Format<T>(CollectionAssertionError<T> error)
    {
        EnsureObservedItems(error.ActualValue, MaxFormattedItems - 1);

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
        EnsureObservedItems(error.ActualValue, MaxFormattedItems - 1);

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
        EnsureObservedItems(error.ActualValue, MaxFormattedItems - 1);

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
        await EnsureObservedItemsAsync(error.ActualValue, MaxFormattedItems - 1).ConfigureAwait(false);

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
        EnsureObservedItems(error.ActualValue, MaxFormattedItems - 1);

        return CreateMessage("Assert.Contains() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .AppendGroup(
                ("Expected item", FormatValue(error.ExpectedValue)),
                ("Actual", FormatValue(error.ActualValue.Items)))
            .ToString();
    }

    public virtual string Format<TExpected>(ContainsNullActualAssertionError<TExpected> error)
    {
        return CreateMessage("Assert.Contains() assertion failed.", error.Message)
            .AppendGroup(
                (error.ExpectedExpressionLabel, error.ExpectedExpression ?? string.Empty),
                ("Actual expression", error.ActualExpression ?? string.Empty))
            .AppendGroup(
                (error.ExpectedValueLabel, FormatValue(error.ExpectedValue)),
                ("Actual", "<null>"))
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

    public virtual string Format(StringContainsNullActualAssertionError error)
    {
        return CreateMessage("Assert.Contains() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .Append("Comparison", error.Comparison.ToString())
            .AppendGroup(
                ("Expected", FormatValue(error.ExpectedValue)),
                ("Actual", "<null>"))
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
        await EnsureObservedItemsAsync(error.ActualValue, MaxFormattedItems - 1).ConfigureAwait(false);

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
        EnsureObservedItems(error.ActualValue, MaxFormattedItems - 1);

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
        return CreateMessage("Assert.Equal() assertion failed: Lengths differ.", error.Message)
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

        return CreateMessage("Assert.Equal() assertion failed: Lengths differ.", error.Message)
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

        if (value is System.Collections.IEnumerable enumerable)
        {
            return FormatEnumerableValue(enumerable, highlightedIndex, visited);
        }

        if (value is IFormattable formattable)
        {
            return formattable.ToString(format: null, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        return value.ToString() ?? string.Empty;
    }

    private string FormatStructuralValue(object? value)
    {
        if (value is StructuralMissingValue)
            return "<missing>";

        return FormatValue(value);
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
        if (typeof(T) == typeof(char))
        {
            ref var firstChar = ref System.Runtime.CompilerServices.Unsafe.As<T, char>(ref System.Runtime.InteropServices.MemoryMarshal.GetReference(value));
            var chars = System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(ref firstChar, value.Length);
            return FormatStringValue(chars, highlightedIndex);
        }

        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var items = new List<string>(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            items.Add(FormatHighlightedValue(FormatValue(value[i], highlightedIndex: null, visited), i, highlightedIndex));
        }

        return $"[{string.Join(", ", items)}]";
    }

    private string FormatKeyValuePairs<TKey, TValue>(IEnumerable<KeyValuePair<TKey, TValue>> value)
    {
        var items = new List<string>();
        var index = 0;
        foreach (var item in value)
        {
            if (index >= MaxFormattedItems)
            {
                items.Add("...");
                break;
            }

            items.Add(FormatValue(item.Key) + ": " + FormatValue(item.Value));
            index++;
        }

        return $"[{string.Join(", ", items)}]";
    }

    private string FormatDictionary(System.Collections.IDictionary value)
    {
        var items = new List<string>();
        var index = 0;
        foreach (System.Collections.DictionaryEntry item in value)
        {
            if (index >= MaxFormattedItems)
            {
                items.Add("...");
                break;
            }

            items.Add(FormatValue(item.Key) + ": " + FormatValue(item.Value));
            index++;
        }

        return $"[{string.Join(", ", items)}]";
    }

    protected virtual string FormatEnumerableValue(System.Collections.IEnumerable value, int? highlightedIndex, HashSet<object> visited)
    {
        if (!visited.Add(value))
            return "<circular reference>";

        try
        {
            var items = new List<string>();
            var shouldFocusHighlightedItem = IsFocusedHighlightedItem(highlightedIndex);
            var focusStartIndex = shouldFocusHighlightedItem
                ? Math.Max(PrefixItemCount, highlightedIndex.GetValueOrDefault() - HighlightedContextItemCount)
                : -1;
            var prefixItemCount = shouldFocusHighlightedItem ? PrefixItemCount : MaxFormattedItems;
            var maxIndex = GetMaxFormattedIndex(highlightedIndex);
            var hasSkippedItems = false;
            var index = 0;

            foreach (var item in value)
            {
                if (index > maxIndex)
                {
                    items.Add("...");
                    break;
                }

                if (index < prefixItemCount || index >= focusStartIndex)
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

            return $"[{string.Join(", ", items)}]";
        }
        finally
        {
            visited.Remove(value);
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

        var message = exception.Message;
        if (string.IsNullOrEmpty(message))
            return exception.GetType().FullName ?? exception.GetType().Name;

        return message.Replace(Environment.NewLine, Environment.NewLine + "           ", StringComparison.Ordinal);
    }

    private static void EnsureObservedItems<T>(CollectionSnapshot<T> snapshot, int maxIndex)
    {
        if (snapshot.IsComplete || snapshot.ObservedCount > maxIndex + 1)
            return;

        for (var index = snapshot.ObservedCount; !snapshot.IsComplete && snapshot.ObservedCount <= maxIndex + 1 && snapshot.TryGetItem(index, out _); index++)
        {
        }
    }

    private static async Task EnsureObservedItemsAsync<T>(AsyncCollectionSnapshot<T> snapshot, int maxIndex)
    {
        if (snapshot.IsComplete || snapshot.ObservedCount > maxIndex + 1)
            return;

        for (var index = snapshot.ObservedCount; !snapshot.IsComplete && snapshot.ObservedCount <= maxIndex + 1 && await snapshot.TryGetItem(index).ConfigureAwait(false) is (true, _); index++)
        {
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
        foreach (var c in value)
        {
            result.Append(c);
            result.Append(CombiningLowLine);
        }

        return result.ToString();
    }

    private static string FormatStringValue(string value, int? highlightedIndex)
    {
        var result = new StringBuilder(value.Length + 2);
        result.Append('"');

        for (var i = 0; i < value.Length; i++)
        {
            var escapedChar = EscapeChar(value[i]);
            if (i == highlightedIndex)
            {
                result.Append(Underline(escapedChar));
            }
            else
            {
                result.Append(escapedChar);
            }
        }

        result.Append('"');
        return result.ToString();

        static string EscapeChar(char value)
        {
            return value switch
            {
                '\r' => "\\r",
                '\n' => "\\n",
                '\t' => "\\t",
                '"' => "\\\"",
                '\\' => "\\\\",
                < ' ' => "\\u" + ((int)value).ToString("X4", CultureInfo.InvariantCulture),
                _ => value.ToString(),
            };
        }
    }

    private static string FormatStringValue(ReadOnlySpan<char> value, int? highlightedIndex)
    {
        return FormatStringValue(value.ToString(), highlightedIndex);
    }
}
#pragma warning restore CA1822, CA1852
