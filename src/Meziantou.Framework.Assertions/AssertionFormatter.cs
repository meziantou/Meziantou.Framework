using System.Reflection;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

#pragma warning disable CA1822, CA1852 // Formatter methods intentionally share an instance-based overridable shape.
internal partial class AssertionFormatter
{
    private const char CombiningLowLine = '̲';
    private const string Ellipsis = "...";

    // Nested sequences and tuples deeper than this are not expanded. A sequence yielding fresh copies of itself is not
    // a circular reference, so without a limit formatting it would recurse until the process crashes.
    private const int MaxFormattingDepth = 16;
    private const string MaxDepthMarker = "<max depth reached>";

    private static readonly MethodInfo FormatBoxedMemoryValueMethod = typeof(AssertionFormatter).GetMethod(nameof(FormatBoxedMemoryValue), BindingFlags.NonPublic | BindingFlags.Instance)!;

    // Exceptions thrown by a lazy sequence while the formatter reads more of it, keyed by the snapshot's item list.
    private static readonly ConditionalWeakTable<object, Exception> ObservationExceptions = new();

    // Options set by Assert.UseFormatterOptions for the current asynchronous flow. They take precedence over Options.
    private static readonly AsyncLocal<FormatterOptions?> ScopedOptions = new();

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

    /// <summary>Gets the options used to format a message: the scoped options when a scope is active, otherwise <see cref="Options"/>.</summary>
    internal FormatterOptions CurrentOptions => ScopedOptions.Value ?? Options;

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

    /// <summary>Gets or sets the maximum number of characters of a string to format before truncating it.</summary>
    public int MaxFormattedStringLength
    {
        get => Options.MaxFormattedStringLength;
        set => Options.MaxFormattedStringLength = value;
    }

    /// <summary>Uses <paramref name="options"/> instead of the global options for the current asynchronous flow, until the returned scope is disposed.</summary>
    internal static IDisposable UseOptions(FormatterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var scope = new OptionsScope(ScopedOptions.Value);
        ScopedOptions.Value = options;
        return scope;
    }

    /// <summary>
    /// Reads the options once for a whole message. Formatting runs user code (ToString, enumerators) and can take a
    /// while, so reading the mutable options at every step could mix values set concurrently in a single message.
    /// </summary>
    private FormattingContext CreateContext()
    {
        return new FormattingContext(CurrentOptions);
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

    public virtual string Format(ThrowsParameterNameAssertionError error)
    {
        return CreateMessage("Assert.Throws() assertion failed.", error.Message)
            .Append("Expression", error.ActionExpression)
            .Append("Exception type", FormatType(error.ActualException.GetType()))
            .AppendGroup(
                ("Expected parameter name", FormatValue(error.ExpectedParamName)),
                ("Actual parameter name", FormatValue(error.ActualException.ParamName)))
            .Append("Exception", FormatException(error.ActualException))
            .ToString();
    }

    public virtual string Format<T>(NegativeReadOnlySpanActualValueAssertionError<T> error)
    {
        var context = CreateContext();

        return CreateMessage($"Assert.{error.AssertionName}() assertion failed.", error.Message)
            .Append("Expression", error.ActualExpression)
            .AppendGroup(
                ("Not expected", error.NotExpectedText),
                ("Actual", FormatReadOnlySpanValue(context, error.ActualValue)))
            .ToString();
    }

    public virtual string Format<TExpected, TActual>(NegativeReadOnlySpanValueAssertionError<TExpected, TActual> error)
    {
        var context = CreateContext();

        return CreateMessage($"Assert.{error.AssertionName}() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .AppendGroup(
                (error.NotExpectedLabel, FormatReadOnlySpanValue(context, error.ExpectedValue)),
                ("Actual", FormatReadOnlySpanValue(context, error.ActualValue)))
            .ToString();
    }

    public virtual string Format<TExpected, TActual>(NegativeReadOnlySpanExpectedActualValueAssertionError<TExpected, TActual> error)
    {
        var context = CreateContext();

        return CreateMessage($"Assert.{error.AssertionName}() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .AppendGroup(
                (error.NotExpectedLabel, FormatValue(context, error.ExpectedValue)),
                ("Actual", FormatReadOnlySpanValue(context, error.ActualValue)))
            .ToString();
    }

    public virtual string Format<T>(NegativeReadOnlySpanCountAssertionError<T> error)
    {
        var context = CreateContext();

        return CreateMessage($"Assert.{error.AssertionName}() assertion failed.", error.Message)
            .Append("Expression", error.ActualExpression)
            .AppendGroup(
                ("Not expected count", error.NotExpectedCount.ToString(CultureInfo.InvariantCulture)),
                ("Actual count", error.ActualCount.ToString(CultureInfo.InvariantCulture)))
            .Append("Actual", FormatReadOnlySpanValue(context, error.ActualValue))
            .ToString();
    }

    private string FormatNegativeValue(string assertionName, string? expectedExpression, string? actualExpression, string notExpectedLabel, object? expectedValue, object? actualValue, string? message)
    {
        var context = CreateContext();

        return CreateMessage($"Assert.{assertionName}() assertion failed.", message)
            .AppendGroup(
                ("Expected expression", expectedExpression ?? string.Empty),
                ("Actual expression", actualExpression ?? string.Empty))
            .AppendGroup(
                (notExpectedLabel, FormatValue(context, expectedValue)),
                ("Actual", FormatValue(context, actualValue)))
            .ToString();
    }

    public virtual string Format<TExpected, TActual>(DoesNotContainAssertionError<TExpected, TActual> error)
    {
        var builder = CreateMessage("Assert.DoesNotContain() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression ?? string.Empty),
                ("Actual expression", error.ActualExpression ?? string.Empty));

        if (error.FoundIndex is { } foundIndex)
        {
            builder.Append(error.FoundIndexLabel, foundIndex.ToString(CultureInfo.InvariantCulture));
        }

        return builder
            .AppendGroup(
                (error.NotExpectedLabel, FormatValue(error.ExpectedValue)),
                ("Actual", FormatValue(error.ActualValue, error.FoundIndex)))
            .ToString();
    }

    public virtual string Format<T>(ReadOnlySpanDoesNotContainItemAssertionError<T> error)
    {
        return CreateMessage("Assert.DoesNotContain() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .Append("Index of found item", error.FoundIndex.ToString(CultureInfo.InvariantCulture))
            .AppendGroup(
                ("Not expected item", FormatValue(error.ExpectedValue)),
                ("Actual", FormatReadOnlySpanValue(error.ActualValue, error.FoundIndex)))
            .ToString();
    }

    public virtual string Format<T>(ReadOnlySpanDoesNotContainAssertionError<T> error)
    {
        return CreateMessage("Assert.DoesNotContain() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .Append(error.FoundIndexLabel, error.FoundIndex.ToString(CultureInfo.InvariantCulture))
            .AppendGroup(
                (error.NotExpectedLabel, FormatReadOnlySpanValue(error.ExpectedValue)),
                ("Actual", FormatReadOnlySpanValue(error.ActualValue, error.FoundIndex)))
            .ToString();
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
        var context = CreateContext();

        return CreateMessage($"Assert.{error.AssertionName}() assertion failed.", error.Message)
            .Append("Expression", error.ActualExpression)
            .AppendGroup(
                ("Not expected", error.NotExpectedText),
                ("Actual", FormatValue(context, error.ActualValue)))
            .ToString();
    }

    public virtual string Format(NegativeSameAssertionError error)
    {
        var context = CreateContext();

        return CreateMessage("Assert.NotSame() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .AppendGroup(
                ("Not expected", "same instance as " + FormatValue(context, error.ExpectedValue)),
                ("Actual", FormatValue(context, error.ActualValue)))
            .ToString();
    }

    public virtual string Format<T>(NegativeRangeAssertionError<T> error)
    {
        var context = CreateContext();

        return CreateMessage($"Assert.{error.AssertionName}() assertion failed.", error.Message)
            .Append("Expression", error.ActualExpression)
            .AppendGroup(
                ("Not expected", $"in range [{FormatValue(context, error.LowValue)}, {FormatValue(context, error.HighValue)}]"),
                ("Actual", FormatValue(context, error.ActualValue)))
            .ToString();
    }

    public virtual string Format(NegativeTypeAssertionError error)
    {
        var context = CreateContext();

        return CreateMessage($"Assert.{error.AssertionName}() assertion failed.", error.Message)
            .Append("Expression", error.ActualExpression)
            .AppendGroup(
                (error.NotExpectedTypeLabel, FormatType(error.ExpectedType)),
                ("Actual type", FormatType(error.ActualValue?.GetType())))
            .Append("Actual value", FormatValue(context, error.ActualValue))
            .ToString();
    }

    public virtual string Format(NegativeSetAssertionError error)
    {
        var context = CreateContext();

        return CreateMessage($"Assert.{error.AssertionName}() assertion failed.", error.Message)
            .AppendGroup(
                ($"Expected {error.ExpectedSetRole} expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .AppendGroup(
                ($"Not expected {error.ExpectedSetRole}", FormatValue(context, error.ExpectedValue)),
                ("Actual", FormatValue(context, error.ActualValue)))
            .ToString();
    }

    public virtual string Format<T>(NegativeCountAssertionError<T> error)
    {
        var context = CreateContext();

        return CreateMessage($"Assert.{error.AssertionName}() assertion failed.", error.Message)
            .Append("Expression", error.ActualExpression)
            .AppendGroup(
                ("Not expected count", error.NotExpectedCount.ToString(CultureInfo.InvariantCulture)),
                ("Actual count", error.ActualCount.ToString(CultureInfo.InvariantCulture)))
            .Append("Actual", FormatValue(context, error.ActualValue))
            .ToString();
    }

    public virtual string Format<T>(NegativeEqualWithToleranceAssertionError<T> error)
    {
        var context = CreateContext();

        return CreateMessage("Assert.NotEqual() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .AppendGroup(
                ("Not expected", FormatValue(context, error.ExpectedValue)),
                ("Actual", FormatValue(context, error.ActualValue)))
            .Append("Tolerance", FormatValue(context, error.Tolerance))
            .ToString();
    }

    public virtual string Format(NullAssertionError error)
    {
        var context = CreateContext();

        return CreateMessage("Assert.Null() assertion failed.", error.Message)
            .Append("Expression", error.ActualExpression)
            .AppendGroup(
                ("Expected", "<null>"),
                ("Actual", FormatValue(context, error.ActualValue)))
            .ToString();
    }

    public virtual string Format(IsTypeAssertionError error)
    {
        var context = CreateContext();

        return CreateMessage("Assert.IsType() assertion failed.", error.Message)
            .Append("Expression", error.ActualExpression)
            .AppendGroup(
                ("Expected type", FormatType(error.ExpectedType)),
                ("Actual type", FormatType(error.ActualValue?.GetType())))
            .Append("Actual value", FormatValue(context, error.ActualValue))
            .ToString();
    }

    public virtual string Format(IsAssignableToAssertionError error)
    {
        var context = CreateContext();

        return CreateMessage("Assert.IsAssignableTo() assertion failed.", error.Message)
            .Append("Expression", error.ActualExpression)
            .AppendGroup(
                ("Expected type", FormatType(error.ExpectedType)),
                ("Actual type", FormatType(error.ActualValue?.GetType())))
            .Append("Actual value", FormatValue(context, error.ActualValue))
            .ToString();
    }

    public virtual string Format(SameAssertionError error)
    {
        var context = CreateContext();

        return CreateMessage("Assert.Same() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .AppendGroup(
                ("Expected", "same instance as " + FormatValue(context, error.ExpectedValue)),
                ("Actual", FormatValue(context, error.ActualValue)))
            .ToString();
    }

    public virtual string Format<T>(InRangeAssertionError<T> error)
    {
        var context = CreateContext();

        return CreateMessage("Assert.InRange() assertion failed.", error.Message)
            .Append("Expression", error.ActualExpression)
            .AppendGroup(
                ("Expected", $"in range [{FormatValue(context, error.LowValue)}, {FormatValue(context, error.HighValue)}]"),
                ("Actual", FormatValue(context, error.ActualValue)))
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
        var context = CreateContext();

        return CreateMessage("Assert.Matches() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .AppendGroup(
                ("Expected pattern", FormatValue(context, error.ExpectedPattern)),
                ("Actual", FormatValue(context, error.ActualValue)))
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
        var context = CreateContext();

        return CreateMessage($"Assert.{error.AssertionName}() assertion failed.", error.Message)
            .AppendGroup(
                ($"Expected {error.ExpectedSetRole} expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .AppendGroup(
                ($"Expected {error.ExpectedSetRole}", FormatValue(context, error.ExpectedValue.Items)),
                ("Actual", FormatValue(context, error.ActualValue.Items)))
            .ToString();
    }

    public virtual string Format<TExpected, TActual>(EqualAssertionError<TExpected, TActual> error)
    {
        var context = CreateContext();

        var builder = CreateMessage("Assert.Equal() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression));

        if (error.FirstDifferenceIndex is not null)
        {
            builder.Append("Index of first difference", error.FirstDifferenceIndex.Value.ToString(CultureInfo.InvariantCulture));
        }

        var expected = FormatValue(context, error.ExpectedValue, error.FirstDifferenceIndex);
        var actual = FormatValue(context, error.ActualValue, error.FirstDifferenceIndex);
        builder.AppendGroup(
            ("Expected", expected),
            ("Actual", actual));
        AppendIdenticalFormattingDetails(builder, expected, actual, error.ExpectedValue?.GetType(), error.ActualValue?.GetType(), "type");
        return builder.ToString();
    }

    public virtual string Format<T>(EqualWithToleranceAssertionError<T> error)
    {
        var context = CreateContext();

        return CreateMessage("Assert.Equal() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .AppendGroup(
                ("Expected", FormatValue(context, error.ExpectedValue)),
                ("Actual", FormatValue(context, error.ActualValue)))
            .Append("Tolerance", FormatValue(context, error.Tolerance))
            .ToString();
    }

    public virtual string Format(EquivalentAssertionError error)
    {
        var context = CreateContext();

        var expected = FormatStructuralValue(context, error.ExpectedValue);
        var actual = FormatStructuralValue(context, error.ActualValue);
        var builder = CreateMessage("Assert.Equivalent() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .Append("Path", error.Path)
            .Append("Reason", error.Reason)
            .AppendGroup(
                ("Expected", expected),
                ("Actual", actual));
        AppendIdenticalFormattingDetails(builder, expected, actual, error.ExpectedValue?.GetType(), error.ActualValue?.GetType(), "type");
        return builder.ToString();
    }

    public virtual string Format<TExpected, TActual>(ReadOnlySpanEqualAssertionError<TExpected, TActual> error)
    {
        var context = CreateContext();

        var expected = FormatReadOnlySpanValue(context, error.ExpectedValue, error.FirstDifferenceIndex);
        var actual = FormatReadOnlySpanValue(context, error.ActualValue, error.FirstDifferenceIndex);
        var builder = CreateMessage($"Assert.Equal() assertion failed: Item at index {error.FirstDifferenceIndex} differs.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .Append("Index of first difference", error.FirstDifferenceIndex.ToString(CultureInfo.InvariantCulture))
            .AppendGroup(
                ("Expected item", expected),
                ("Actual item", actual));
        AppendIdenticalFormattingDetails(builder, expected, actual, GetItemType(error.ExpectedValue, error.FirstDifferenceIndex), GetItemType(error.ActualValue, error.FirstDifferenceIndex), "item type");
        return builder.ToString();
    }

    public virtual string Format<TExpected, TActual>(ReadOnlySpanLengthAssertionError<TExpected, TActual> error)
    {
        var context = CreateContext();

        return CreateMessage("Assert.Equal() assertion failed: Lengths differ.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .AppendGroup(
                ("Expected length", error.ExpectedValue.Length.ToString(CultureInfo.InvariantCulture)),
                ("Actual length", error.ActualValue.Length.ToString(CultureInfo.InvariantCulture)))
            .Append("Index of first difference", error.FirstDifferenceIndex.ToString(CultureInfo.InvariantCulture))
            .AppendGroup(
                ("Expected", FormatReadOnlySpanValue(context, error.ExpectedValue, error.FirstDifferenceIndex < error.ExpectedValue.Length ? error.FirstDifferenceIndex : null)),
                ("Actual", FormatReadOnlySpanValue(context, error.ActualValue, error.FirstDifferenceIndex < error.ActualValue.Length ? error.FirstDifferenceIndex : null)))
            .ToString();
    }

    public virtual string Format<T>(ValueStartsWithAssertionError<T> error)
    {
        var context = CreateContext();

        return CreateMessage("Assert.StartsWith() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .AppendGroup(
                ("Expected prefix", FormatValue(context, error.ExpectedValue)),
                ("Actual", FormatReadOnlySpanValue(context, error.ActualValue, error.ActualValue.IsEmpty ? null : 0)))
            .ToString();
    }

    public virtual string Format<T>(ValueCollectionStartsWithAssertionError<T> error)
    {
        var context = CreateContext();

        EnsureObservedItems(error.ActualValue, context.GetMaxFormattedIndex(error.ActualValue.Items.Count > 0 ? 0 : null));

        return CreateMessage("Assert.StartsWith() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .AppendGroup(
                ("Expected prefix", FormatValue(context, error.ExpectedValue)),
                ("Actual", FormatValue(context, error.ActualValue.Items, error.ActualValue.Items.Count > 0 ? 0 : null)))
            .ToString();
    }

    public virtual string Format<T>(ReadOnlySpanEmptyAssertionError<T> error)
    {
        var context = CreateContext();

        return CreateMessage("Assert.Empty() assertion failed.", error.Message)
            .Append("Expression", error.ActualExpression)
            .Append("Actual", FormatReadOnlySpanValue(context, error.ActualValue, error.ActualValue.IsEmpty ? null : 0))
            .ToString();
    }

    public virtual string Format(StringEmptyAssertionError error)
    {
        var context = CreateContext();

        return CreateMessage("Assert.Empty() assertion failed.", error.Message)
            .Append("Expression", error.ActualExpression)
            .Append("Actual", FormatStringValue(context, error.ActualValue, error.ActualValue.IsEmpty ? null : 0))
            .ToString();
    }

    public virtual string Format<T>(CollectionEmptyAssertionError<T> error)
    {
        var context = CreateContext();

        EnsureObservedItems(error.ActualValue, context.GetMaxFormattedIndex(error.ActualValue.Items.Count > 0 ? 0 : null));

        return CreateMessage("Assert.Empty() assertion failed.", error.Message)
            .Append("Expression", error.ActualExpression)
            .Append("Actual", FormatValue(context, error.ActualValue.Items, error.ActualValue.Items.Count > 0 ? 0 : null))
            .ToString();
    }

    public virtual async Task<string> FormatAsync<T>(AsyncCollectionEmptyAssertionError<T> error)
    {
        var context = CreateContext();

        await EnsureObservedItemsAsync(error.ActualValue, context.GetMaxFormattedIndex(error.ActualValue.Items.Count > 0 ? 0 : null)).ConfigureAwait(false);

        return CreateMessage("Assert.Empty() assertion failed.", error.Message)
            .Append("Expression", error.ActualExpression)
            .Append("Actual", FormatValue(context, error.ActualValue.Items, error.ActualValue.Items.Count > 0 ? 0 : null))
            .ToString();
    }

    public virtual string Format<T>(ReadOnlySpanSingleAssertionError<T> error)
    {
        var context = CreateContext();

        return CreateMessage("Assert.Single() assertion failed.", error.Message)
            .Append("Expression", error.ActualExpression)
            .Append("Actual", FormatReadOnlySpanValue(context, error.ActualValue, GetSingleFailureHighlightedIndex(error.ActualValue.Length)))
            .ToString();
    }

    public virtual string Format(StringSingleAssertionError error)
    {
        var context = CreateContext();

        return CreateMessage("Assert.Single() assertion failed.", error.Message)
            .Append("Expression", error.ActualExpression)
            .Append("Actual", FormatStringValue(context, error.ActualValue, GetSingleFailureHighlightedIndex(error.ActualValue.Length)))
            .ToString();
    }

    public virtual string Format<T>(CollectionSingleAssertionError<T> error)
    {
        var context = CreateContext();

        EnsureObservedItems(error.ActualValue, context.GetMaxFormattedIndex(GetSingleFailureHighlightedIndex(error.ActualValue.Items.Count)));

        return CreateMessage("Assert.Single() assertion failed.", error.Message)
            .Append("Expression", error.ActualExpression)
            .Append("Actual", FormatValue(context, error.ActualValue.Items, GetSingleFailureHighlightedIndex(error.ActualValue.Items.Count)))
            .ToString();
    }

    public virtual string Format<T>(CollectionSinglePredicateAssertionError<T> error)
    {
        var context = CreateContext();

        EnsureObservedItems(error.MatchingValues, context.GetMaxFormattedIndex(GetSingleFailureHighlightedIndex(error.MatchingValues.Items.Count)));

        return CreateMessage("Assert.Single() assertion failed.", error.Message)
            .AppendGroup(
                ("Expression", error.ActualExpression),
                ("Predicate expression", error.PredicateExpression))
            .Append("Matching items", FormatValue(context, error.MatchingValues.Items, GetSingleFailureHighlightedIndex(error.MatchingValues.Items.Count)))
            .ToString();
    }

    public virtual string Format<T>(CollectionContainsPredicateAssertionError<T> error)
    {
        var context = CreateContext();

        EnsureObservedItems(error.MatchingValues, context.GetMaxFormattedIndex(highlightedIndex: null));

        return CreateMessage("Assert.Contains() assertion failed.", error.Message)
            .AppendGroup(
                ("Expression", error.ActualExpression),
                ("Predicate expression", error.PredicateExpression))
            .Append("Matching items", FormatValue(context, error.MatchingValues.Items))
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

    public virtual string Format(CollectionNullActualAssertionError error)
    {
        var builder = CreateMessage($"Assert.{error.AssertionName}() assertion failed.", error.Message)
            .Append("Expression", error.ActualExpression);

        if (error.ExpectedLabel is null)
            return builder.Append("Actual", "<null>").ToString();

        return builder
            .AppendGroup(
                (error.ExpectedLabel, error.ExpectedText),
                ("Actual", "<null>"))
            .ToString();
    }

    public virtual string Format<T>(CollectionDoesNotContainPredicateAssertionError<T> error)
    {
        var context = CreateContext();

        EnsureObservedItems(error.MatchingValues, context.GetMaxFormattedIndex(highlightedIndex: null));

        return CreateMessage("Assert.DoesNotContain() assertion failed.", error.Message)
            .AppendGroup(
                ("Expression", error.ActualExpression),
                ("Predicate expression", error.PredicateExpression))
            .AppendGroup(
                ("Not expected", "any matching item"),
                ("Matching items", FormatValue(context, error.MatchingValues.Items)))
            .ToString();
    }

    public virtual async Task<string> FormatAsync<T>(AsyncCollectionSingleAssertionError<T> error)
    {
        var context = CreateContext();

        await EnsureObservedItemsAsync(error.ActualValue, context.GetMaxFormattedIndex(GetSingleFailureHighlightedIndex(error.ActualValue.Items.Count))).ConfigureAwait(false);

        return CreateMessage("Assert.Single() assertion failed.", error.Message)
            .Append("Expression", error.ActualExpression)
            .Append("Actual", FormatValue(context, error.ActualValue.Items, GetSingleFailureHighlightedIndex(error.ActualValue.Items.Count)))
            .ToString();
    }

    public virtual string Format<T>(CollectionAssertionError<T> error)
    {
        var context = CreateContext();

        EnsureObservedItems(error.ActualValue, context.GetMaxFormattedIndex(highlightedIndex: null));

        return CreateMessage("Assert.Collection() assertion failed: Collection count does not match inspector count.", error.Message)
            .Append("Expression", error.ActualExpression)
            .AppendGroup(
                ("Expected count", error.ExpectedCount.ToString(CultureInfo.InvariantCulture)),
                ("Actual count", error.ActualCount))
            .Append("Actual", FormatValue(context, error.ActualValue.Items))
            .ToString();
    }

    public virtual string Format<T>(CollectionInspectorAssertionError<T> error)
    {
        var context = CreateContext();

        EnsureObservedItems(error.ActualValue, context.GetMaxFormattedIndex(error.Index));

        return CreateMessage($"Assert.Collection() assertion failed: Item at index {error.Index} failed.", error.Message)
            .Append("Expression", error.ActualExpression)
            .Append("Actual", FormatValue(context, error.ActualValue.Items, error.Index))
            .Append("Exception", FormatException(error.Exception))
            .ToString();
    }

    public virtual string Format<T>(ReadOnlySpanAllAssertionError<T> error)
    {
        var context = CreateContext();

        return CreateMessage($"Assert.All() assertion failed: Item at index {error.Index} failed.", error.Message)
            .AppendGroup(
                ("Expression", error.ActualExpression),
                ("Assertion expression", error.AssertionExpression))
            .Append("Actual", FormatReadOnlySpanValue(context, error.ActualValue, error.Index))
            .Append("Exception", FormatException(error.Exception))
            .ToString();
    }

    public virtual string Format<T>(CollectionAllPredicateAssertionError<T> error)
    {
        var context = CreateContext();

        EnsureObservedItems(error.ActualValue, context.GetMaxFormattedIndex(error.Index));

        return CreateMessage($"Assert.All() assertion failed: Item at index {error.Index} did not satisfy the predicate.", error.Message)
            .AppendGroup(
                ("Expression", error.ActualExpression),
                ("Predicate expression", error.PredicateExpression))
            .Append("Actual", FormatValue(context, error.ActualValue.Items, error.Index))
            .ToString();
    }

    public virtual string Format<T>(CollectionDoesNotAllPredicateAssertionError<T> error)
    {
        var context = CreateContext();

        EnsureObservedItems(error.ActualValue, context.GetMaxFormattedIndex(highlightedIndex: null));

        return CreateMessage("Assert.DoesNotAll() assertion failed: All items satisfy the predicate, but expected at least one that does not.", error.Message)
            .AppendGroup(
                ("Expression", error.ActualExpression),
                ("Predicate expression", error.PredicateExpression))
            .Append("Actual", FormatValue(context, error.ActualValue.Items))
            .ToString();
    }

    public virtual string Format<T>(CollectionAllAssertionError<T> error)
    {
        var context = CreateContext();

        EnsureObservedItems(error.ActualValue, context.GetMaxFormattedIndex(error.Index));

        return CreateMessage($"Assert.All() assertion failed: Item at index {error.Index} failed.", error.Message)
            .AppendGroup(
                ("Expression", error.ActualExpression),
                ("Assertion expression", error.AssertionExpression))
            .Append("Actual", FormatValue(context, error.ActualValue.Items, error.Index))
            .Append("Exception", FormatException(error.Exception))
            .ToString();
    }

    public virtual async Task<string> FormatAsync<T>(AsyncCollectionAllAssertionError<T> error)
    {
        var context = CreateContext();

        await EnsureObservedItemsAsync(error.ActualValue, context.GetMaxFormattedIndex(error.Index)).ConfigureAwait(false);

        return CreateMessage($"Assert.All() assertion failed: Item at index {error.Index} failed.", error.Message)
            .AppendGroup(
                ("Expression", error.ActualExpression),
                ("Assertion expression", error.AssertionExpression))
            .Append("Actual", FormatValue(context, error.ActualValue.Items, error.Index))
            .Append("Exception", FormatException(error.Exception))
            .ToString();
    }

    public virtual string Format<T>(ReadOnlySpanDistinctAssertionError<T> error)
    {
        var context = CreateContext();

        return CreateMessage($"Assert.Distinct() assertion failed: Duplicate item found at index {error.DuplicateIndex}.", error.Message)
            .Append("Expression", error.ActualExpression)
            .AppendGroup(
                ("First index", error.FirstIndex.ToString(CultureInfo.InvariantCulture)),
                ("Duplicate index", error.DuplicateIndex.ToString(CultureInfo.InvariantCulture)))
            .Append("Actual", FormatReadOnlySpanValue(context, error.ActualValue, error.DuplicateIndex))
            .ToString();
    }

    public virtual string Format<T>(CollectionDistinctAssertionError<T> error)
    {
        var context = CreateContext();

        EnsureObservedItems(error.ActualValue, context.GetMaxFormattedIndex(error.DuplicateIndex));

        return CreateMessage($"Assert.Distinct() assertion failed: Duplicate item found at index {error.DuplicateIndex}.", error.Message)
            .Append("Expression", error.ActualExpression)
            .AppendGroup(
                ("First index", error.FirstIndex.ToString(CultureInfo.InvariantCulture)),
                ("Duplicate index", error.DuplicateIndex.ToString(CultureInfo.InvariantCulture)))
            .Append("Actual", FormatValue(context, error.ActualValue.Items, error.DuplicateIndex))
            .ToString();
    }

    public virtual async Task<string> FormatAsync<T>(AsyncCollectionDistinctAssertionError<T> error)
    {
        var context = CreateContext();

        await EnsureObservedItemsAsync(error.ActualValue, context.GetMaxFormattedIndex(error.DuplicateIndex)).ConfigureAwait(false);

        return CreateMessage($"Assert.Distinct() assertion failed: Duplicate item found at index {error.DuplicateIndex}.", error.Message)
            .Append("Expression", error.ActualExpression)
            .AppendGroup(
                ("First index", error.FirstIndex.ToString(CultureInfo.InvariantCulture)),
                ("Duplicate index", error.DuplicateIndex.ToString(CultureInfo.InvariantCulture)))
            .Append("Actual", FormatValue(context, error.ActualValue.Items, error.DuplicateIndex))
            .ToString();
    }

    public virtual string Format<T>(ReadOnlySpanCountAssertionError<T> error)
    {
        var context = CreateContext();

        return CreateMessage($"Assert.{error.AssertionName}() assertion failed.", error.Message)
            .Append("Expression", error.ActualExpression)
            .AppendGroup(
                ("Expected count", error.ExpectedCount.ToString(CultureInfo.InvariantCulture)),
                ("Actual count", error.ActualCount.ToString(CultureInfo.InvariantCulture)))
            .Append("Actual", FormatReadOnlySpanValue(context, error.ActualValue))
            .ToString();
    }

    public virtual string Format(StringCountAssertionError error)
    {
        var context = CreateContext();

        return CreateMessage($"Assert.{error.AssertionName}() assertion failed.", error.Message)
            .Append("Expression", error.ActualExpression)
            .AppendGroup(
                ("Expected count", error.ExpectedCount.ToString(CultureInfo.InvariantCulture)),
                ("Actual count", error.ActualCount.ToString(CultureInfo.InvariantCulture)))
            .Append("Actual", FormatStringValue(context, error.ActualValue, highlightedIndex: null))
            .ToString();
    }

    public virtual string Format<T>(CollectionCountAssertionError<T> error)
    {
        var context = CreateContext();

        EnsureObservedItems(error.ActualValue, context.GetMaxFormattedIndex(highlightedIndex: null));

        return CreateMessage($"Assert.{error.AssertionName}() assertion failed.", error.Message)
            .Append("Expression", error.ActualExpression)
            .AppendGroup(
                ("Expected count", error.ExpectedCount.ToString(CultureInfo.InvariantCulture)),
                ("Actual count", error.ActualCount))
            .Append("Actual", FormatValue(context, error.ActualValue.Items))
            .ToString();
    }

    public virtual async Task<string> FormatAsync<T>(AsyncCollectionCountAssertionError<T> error)
    {
        var context = CreateContext();

        await EnsureObservedItemsAsync(error.ActualValue, context.GetMaxFormattedIndex(highlightedIndex: null)).ConfigureAwait(false);

        return CreateMessage($"Assert.{error.AssertionName}() assertion failed.", error.Message)
            .Append("Expression", error.ActualExpression)
            .AppendGroup(
                ("Expected count", error.ExpectedCount.ToString(CultureInfo.InvariantCulture)),
                ("Actual count", error.ActualCount))
            .Append("Actual", FormatValue(context, error.ActualValue.Items))
            .ToString();
    }

    public virtual string Format<T>(ValueContainsAssertionError<T> error)
    {
        var context = CreateContext();

        return CreateMessage("Assert.Contains() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .AppendGroup(
                ("Expected item", FormatValue(context, error.ExpectedValue)),
                ("Actual", FormatReadOnlySpanValue(context, error.ActualValue)))
            .ToString();
    }

    public virtual string Format<T>(ValueCollectionContainsAssertionError<T> error)
    {
        var context = CreateContext();

        EnsureObservedItems(error.ActualValue, context.GetMaxFormattedIndex(highlightedIndex: null));

        return CreateMessage("Assert.Contains() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .AppendGroup(
                ("Expected item", FormatValue(context, error.ExpectedValue)),
                ("Actual", FormatValue(context, error.ActualValue.Items)))
            .ToString();
    }

    public virtual string Format<TExpected>(NullActualAssertionError<TExpected> error)
    {
        var context = CreateContext();

        return CreateMessage($"Assert.{error.AssertionName}() assertion failed.", error.Message)
            .AppendGroup(
                (error.ExpectedExpressionLabel, error.ExpectedExpression ?? string.Empty),
                ("Actual expression", error.ActualExpression ?? string.Empty))
            .AppendGroup(
                (error.ExpectedValueLabel, FormatValue(context, error.ExpectedValue)),
                ("Actual", "<null>"))
            .ToString();
    }

    public virtual string Format<TActual>(NullExpectedAssertionError<TActual> error)
    {
        return CreateMessage($"Assert.{error.AssertionName}() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression ?? string.Empty),
                ("Actual expression", error.ActualExpression ?? string.Empty))
            .AppendGroup(
                ("Expected", "<null>"),
                ("Actual", FormatValue(error.ActualValue)))
            .ToString();
    }

    public virtual string Format<TKey, TValue>(KeyValuePairCollectionContainsAssertionError<TKey, TValue> error)
    {
        var context = CreateContext();

        EnsureObservedItems(error.ActualValue, context.MaxFormattedItems - 1);

        return CreateMessage("Assert.Contains() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected key expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .AppendGroup(
                ("Expected key", FormatValue(context, error.ExpectedKey)),
                ("Actual", FormatKeyValuePairs(context, error.ActualValue.Items)))
            .ToString();
    }

    public virtual string Format(DictionaryContainsAssertionError error)
    {
        var context = CreateContext();

        return CreateMessage("Assert.Contains() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected key expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .AppendGroup(
                ("Expected key", FormatValue(context, error.ExpectedKey)),
                ("Actual", FormatDictionary(context, error.ActualValue)))
            .ToString();
    }

    public virtual string Format<T>(ReadOnlySpanContainsAssertionError<T> error)
    {
        var context = CreateContext();

        return CreateMessage("Assert.Contains() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .AppendGroup(
                ("Expected", FormatReadOnlySpanValue(context, error.ExpectedValue)),
                ("Actual", FormatReadOnlySpanValue(context, error.ActualValue)))
            .ToString();
    }

    public virtual string Format(ReadOnlySpanCharContainsAssertionError error)
    {
        var context = CreateContext();

        return CreateMessage("Assert.Contains() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .Append("Comparison", error.Comparison.ToString())
            .AppendGroup(
                ("Expected", FormatStringValue(context, error.ExpectedValue, highlightedIndex: null)),
                ("Actual", FormatStringValue(context, error.ActualValue, highlightedIndex: null)))
            .ToString();
    }

    public virtual string Format(StringNullActualAssertionError error)
    {
        var context = CreateContext();

        return CreateMessage($"Assert.{error.AssertionName}() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .Append("Comparison", error.Comparison.ToString())
            .AppendGroup(
                (error.ExpectedValueLabel, FormatValue(context, error.ExpectedValue)),
                ("Actual", "<null>"))
            .ToString();
    }

    public virtual async Task<string> FormatAsync<TExpected, TActual>(CollectionAsyncCollectionContainsAssertionError<TExpected, TActual> error)
    {
        var context = CreateContext();

        await EnsureObservedItemsAsync(error.ActualValue, context.GetMaxFormattedIndex(highlightedIndex: null)).ConfigureAwait(false);

        return CreateMessage("Assert.Contains() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .AppendGroup(
                ("Expected", FormatValue(context, error.ExpectedValue.Items)),
                ("Actual", FormatValue(context, error.ActualValue.Items)))
            .ToString();
    }

    public virtual string Format<TExpected, TActual>(CollectionContainsAssertionError<TExpected, TActual> error)
    {
        var context = CreateContext();

        EnsureObservedItems(error.ActualValue, context.GetMaxFormattedIndex(highlightedIndex: null));

        return CreateMessage("Assert.Contains() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .AppendGroup(
                ("Expected", FormatValue(context, error.ExpectedValue.Items)),
                ("Actual", FormatValue(context, error.ActualValue.Items)))
            .ToString();
    }

    public virtual string Format<T>(ValueEndsWithAssertionError<T> error)
    {
        var context = CreateContext();

        return CreateMessage("Assert.EndsWith() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .AppendGroup(
                ("Expected suffix", FormatValue(context, error.ExpectedValue)),
                ("Actual", FormatReadOnlySpanValue(context, error.ActualValue, error.ActualValue.IsEmpty ? null : error.ActualValue.Length - 1)))
            .ToString();
    }

    public virtual string Format<T>(ValueCollectionEndsWithAssertionError<T> error)
    {
        var context = CreateContext();

        var highlightedIndex = error.ActualValue.Items.Count > 0 ? error.ActualValue.Items.Count - 1 : (int?)null;

        return CreateMessage("Assert.EndsWith() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .AppendGroup(
                ("Expected suffix", FormatValue(context, error.ExpectedValue)),
                ("Actual", FormatValue(context, error.ActualValue.Items, highlightedIndex)))
            .ToString();
    }

    public virtual string Format<T>(ReadOnlySpanEndsWithAssertionError<T> error)
    {
        var context = CreateContext();

        var actualIndex = GetActualSuffixIndex(error.ExpectedValue.Length, error.ActualValue.Length, error.FirstDifferenceIndex);

        return CreateMessage("Assert.EndsWith() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .Append("Index of first difference", error.FirstDifferenceIndex.ToString(CultureInfo.InvariantCulture))
            .AppendGroup(
                ("Expected suffix", FormatReadOnlySpanValue(context, error.ExpectedValue, error.FirstDifferenceIndex < error.ExpectedValue.Length ? error.FirstDifferenceIndex : null)),
                ("Actual", FormatReadOnlySpanValue(context, error.ActualValue, actualIndex)))
            .ToString();
    }

    public virtual string Format(ReadOnlySpanCharEndsWithAssertionError error)
    {
        var context = CreateContext();

        var actualIndex = GetActualSuffixIndex(error.ExpectedValue.Length, error.ActualValue.Length, error.FirstDifferenceIndex);

        return CreateMessage("Assert.EndsWith() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .Append("Comparison", error.Comparison.ToString())
            .Append("Index of first difference", error.FirstDifferenceIndex.ToString(CultureInfo.InvariantCulture))
            .AppendGroup(
                ("Expected suffix", FormatStringValue(context, error.ExpectedValue, error.FirstDifferenceIndex < error.ExpectedValue.Length ? error.FirstDifferenceIndex : null)),
                ("Actual", FormatStringValue(context, error.ActualValue, actualIndex)))
            .ToString();
    }

    public virtual async Task<string> FormatAsync<TExpected, TActual>(CollectionAsyncCollectionEndsWithAssertionError<TExpected, TActual> error)
    {
        var context = CreateContext();

        await EnsureObservedItemsAsync(error.ActualValue, int.MaxValue - 1).ConfigureAwait(false);
        var actualIndex = GetActualSuffixIndex(error.ExpectedValue.Items.Count, error.ActualValue.Items.Count, error.FirstDifferenceIndex);

        return CreateMessage("Assert.EndsWith() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .Append("Index of first difference", error.FirstDifferenceIndex.ToString(CultureInfo.InvariantCulture))
            .AppendGroup(
                ("Expected suffix", FormatValue(context, error.ExpectedValue.Items, error.FirstDifferenceIndex < error.ExpectedValue.Items.Count ? error.FirstDifferenceIndex : null)),
                ("Actual", FormatValue(context, error.ActualValue.Items, actualIndex)))
            .ToString();
    }

    public virtual string Format<TExpected, TActual>(CollectionEndsWithAssertionError<TExpected, TActual> error)
    {
        var context = CreateContext();

        var actualIndex = GetActualSuffixIndex(error.ExpectedValue.Items.Count, error.ActualValue.Items.Count, error.FirstDifferenceIndex);

        return CreateMessage("Assert.EndsWith() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .Append("Index of first difference", error.FirstDifferenceIndex.ToString(CultureInfo.InvariantCulture))
            .AppendGroup(
                ("Expected suffix", FormatValue(context, error.ExpectedValue.Items, error.FirstDifferenceIndex < error.ExpectedValue.Items.Count ? error.FirstDifferenceIndex : null)),
                ("Actual", FormatValue(context, error.ActualValue.Items, actualIndex)))
            .ToString();
    }

    public virtual string Format<T>(ReadOnlySpanStartsWithAssertionError<T> error)
    {
        var context = CreateContext();

        return CreateMessage("Assert.StartsWith() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .Append("Index of first difference", error.FirstDifferenceIndex.ToString(CultureInfo.InvariantCulture))
            .AppendGroup(
                ("Expected prefix", FormatReadOnlySpanValue(context, error.ExpectedValue, error.FirstDifferenceIndex)),
                ("Actual", FormatReadOnlySpanValue(context, error.ActualValue, error.FirstDifferenceIndex < error.ActualValue.Length ? error.FirstDifferenceIndex : null)))
            .ToString();
    }

    public virtual string Format(ReadOnlySpanCharStartsWithAssertionError error)
    {
        var context = CreateContext();

        return CreateMessage("Assert.StartsWith() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .Append("Comparison", error.Comparison.ToString())
            .Append("Index of first difference", error.FirstDifferenceIndex.ToString(CultureInfo.InvariantCulture))
            .AppendGroup(
                ("Expected prefix", FormatStringValue(context, error.ExpectedValue, error.FirstDifferenceIndex)),
                ("Actual", FormatStringValue(context, error.ActualValue, error.FirstDifferenceIndex < error.ActualValue.Length ? error.FirstDifferenceIndex : null)))
            .ToString();
    }


    public virtual async Task<string> FormatAsync<TExpected, TActual>(CollectionAsyncCollectionStartsWithAssertionError<TExpected, TActual> error)
    {
        var context = CreateContext();

        var maxIndex = context.GetMaxFormattedIndex(error.FirstDifferenceIndex);
        EnsureObservedItems(error.ExpectedValue, maxIndex);
        await EnsureObservedItemsAsync(error.ActualValue, maxIndex).ConfigureAwait(false);

        return CreateMessage("Assert.StartsWith() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .Append("Index of first difference", error.FirstDifferenceIndex.ToString(CultureInfo.InvariantCulture))
            .AppendGroup(
                ("Expected prefix", FormatValue(context, error.ExpectedValue.Items, error.FirstDifferenceIndex)),
                ("Actual", FormatValue(context, error.ActualValue.Items, error.FirstDifferenceIndex < error.ActualValue.Items.Count ? error.FirstDifferenceIndex : null)))
            .ToString();
    }

    public virtual string Format<TExpected, TActual>(CollectionStartsWithAssertionError<TExpected, TActual> error)
    {
        var context = CreateContext();

        var maxIndex = context.GetMaxFormattedIndex(error.FirstDifferenceIndex);
        EnsureObservedItems(error.ExpectedValue, maxIndex);
        EnsureObservedItems(error.ActualValue, maxIndex);

        return CreateMessage("Assert.StartsWith() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .Append("Index of first difference", error.FirstDifferenceIndex.ToString(CultureInfo.InvariantCulture))
            .AppendGroup(
                ("Expected prefix", FormatValue(context, error.ExpectedValue.Items, error.FirstDifferenceIndex)),
                ("Actual", FormatValue(context, error.ActualValue.Items, error.FirstDifferenceIndex < error.ActualValue.Items.Count ? error.FirstDifferenceIndex : null)))
            .ToString();
    }

    public virtual string Format<TExpected, TActual>(CollectionEqualAssertionError<TExpected, TActual> error)
    {
        var context = CreateContext();

        // Both snapshots were read up to the first difference by the assertion, so this does not enumerate further.
        var hasExpectedItem = error.ExpectedValue.TryGetItem(error.FirstDifferenceIndex, out var expectedItem);
        var hasActualItem = error.ActualValue.TryGetItem(error.FirstDifferenceIndex, out var actualItem);
        var itemDiffers = hasExpectedItem && hasActualItem;

        var expected = FormatValue(context, error.ExpectedValue, error.FirstDifferenceIndex);
        var actual = FormatValue(context, error.ActualValue, error.FirstDifferenceIndex);
        var builder = CreateMessage(GetCollectionEqualHeader(itemDiffers, error.FirstDifferenceIndex), error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .Append("Index of first difference", error.FirstDifferenceIndex.ToString(CultureInfo.InvariantCulture))
            .AppendGroup(
                ("Expected", expected),
                ("Actual", actual));
        AppendIdenticalFormattingDetails(builder, expected, actual, hasExpectedItem ? expectedItem?.GetType() : null, hasActualItem ? actualItem?.GetType() : null, "item type");
        return builder.ToString();
    }

    public virtual string Format<TExpected, TActual>(CollectionEqualUnorderedAssertionError<TExpected, TActual> error)
    {
        var context = CreateContext();

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

        var expected = FormatValue(context, error.ExpectedValue.Items, error.MissingExpectedIndex);
        var actual = FormatValue(context, error.ActualValue.Items, error.UnexpectedActualIndex);
        builder.AppendGroup(
            ("Expected", expected),
            ("Actual", actual));
        AppendIdenticalFormattingDetails(builder, expected, actual, GetItemType(error.ExpectedValue.Items, error.MissingExpectedIndex), GetItemType(error.ActualValue.Items, error.UnexpectedActualIndex), "item type");
        return builder.ToString();
    }

    public virtual async Task<string> FormatAsync<TExpected, TActual>(AsyncCollectionEqualAssertionError<TExpected, TActual> error)
    {
        var context = CreateContext();

        var maxIndex = context.GetMaxFormattedIndex(error.FirstDifferenceIndex);
        await EnsureObservedItemsAsync(error.ExpectedValue, maxIndex).ConfigureAwait(false);
        await EnsureObservedItemsAsync(error.ActualValue, maxIndex).ConfigureAwait(false);
        var itemDiffers = error.ExpectedValue.Items.Count > error.FirstDifferenceIndex && error.ActualValue.Items.Count > error.FirstDifferenceIndex;

        var expected = FormatValue(context, error.ExpectedValue.Items, error.FirstDifferenceIndex);
        var actual = FormatValue(context, error.ActualValue.Items, error.FirstDifferenceIndex);
        var builder = CreateMessage(GetCollectionEqualHeader(itemDiffers, error.FirstDifferenceIndex), error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .Append("Index of first difference", error.FirstDifferenceIndex.ToString(CultureInfo.InvariantCulture))
            .AppendGroup(
                ("Expected", expected),
                ("Actual", actual));
        AppendIdenticalFormattingDetails(builder, expected, actual, GetItemType(error.ExpectedValue.Items, error.FirstDifferenceIndex), GetItemType(error.ActualValue.Items, error.FirstDifferenceIndex), "item type");
        return builder.ToString();
    }

    public virtual Task<string> FormatAsync<TExpected, TActual>(AsyncCollectionEqualUnorderedAssertionError<TExpected, TActual> error)
    {
        var context = CreateContext();

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

        var expected = FormatValue(context, error.ExpectedValue.Items, error.MissingExpectedIndex);
        var actual = FormatValue(context, error.ActualValue.Items, error.UnexpectedActualIndex);
        builder.AppendGroup(
            ("Expected", expected),
            ("Actual", actual));
        AppendIdenticalFormattingDetails(builder, expected, actual, GetItemType(error.ExpectedValue.Items, error.MissingExpectedIndex), GetItemType(error.ActualValue.Items, error.UnexpectedActualIndex), "item type");
        return Task.FromResult(builder.ToString());
    }



    protected virtual string FormatValue(object? value, int? highlightedIndex = null)
    {
        return FormatValue(CreateContext(), value, highlightedIndex);
    }

    private string FormatValue(FormattingContext context, object? value, int? highlightedIndex = null)
    {
        if (value is null)
            return "<null>";

        if (value is string stringValue)
            return FormatStringValue(context, stringValue, highlightedIndex);

        if (value is char charValue)
            return FormatCharValue(charValue);

        if (TryFormatMemoryValue(context, value, highlightedIndex, out var memoryValue))
            return memoryValue;

        if (value is System.Collections.IEnumerable enumerable)
        {
            return FormatEnumerableValue(context, enumerable, highlightedIndex);
        }

        if (TryFormatKeyValuePairOrTupleValue(context, value, out var structuredValue))
            return structuredValue;

        return FormatScalarValue(context, value);
    }

    private static string FormatScalarValue(FormattingContext context, object value)
    {
        // ToString runs user code, which can use the current culture (records, anonymous types) or throw. Neither may
        // change or replace the assertion failure, so the culture is pinned and exceptions become part of the message.
        string text;
        var previousCulture = CultureInfo.CurrentCulture;
        var changeCulture = !ReferenceEquals(previousCulture, CultureInfo.InvariantCulture);
        try
        {
            if (changeCulture)
            {
                CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            }

            text = value switch
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

        return FormatUnquotedText(context, text);
    }

    /// <summary>
    /// Formats text produced by user code, such as a record's ToString. Line breaks and invisible characters are escaped
    /// so they cannot break the message layout or hide a difference. The text is not quoted and backslashes are kept as
    /// they are, so ordinary values render exactly as their ToString.
    /// </summary>
    private static string FormatUnquotedText(FormattingContext context, string text)
    {
        var isTruncated = text.Length > context.MaxFormattedStringLength;
        var (_, end) = isTruncated ? GetTextWindow(text, context.MaxFormattedStringLength, highlightedIndex: null) : (0, text.Length);

        var result = new StringBuilder(end + Ellipsis.Length);
        AppendEscapedText(result, text.AsSpan(0, end), highlightedIndex: null, quote: null, escapeBackslash: false);
        if (isTruncated)
        {
            result.Append(Ellipsis);
        }

        return result.ToString();
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

    private bool TryFormatMemoryValue(FormattingContext context, object value, int? highlightedIndex, [NotNullWhen(true)] out string? result)
    {
        result = null;
        var type = value.GetType();
        if (!type.IsConstructedGenericType)
            return false;

        var definition = type.GetGenericTypeDefinition();
        if (definition != typeof(ReadOnlyMemory<>) && definition != typeof(Memory<>))
            return false;

        // A boxed memory can be reached again through its own content (object[] holding a boxed memory over itself).
        if (!context.Visited.Add(value))
        {
            result = "<circular reference>";
            return true;
        }

        try
        {
            var method = FormatBoxedMemoryValueMethod.MakeGenericMethod(type.GetGenericArguments()[0]);
            result = (string)method.Invoke(this, BindingFlags.DoNotWrapExceptions, binder: null, [context, value, highlightedIndex], culture: null)!;
            return true;
        }
        finally
        {
            context.Visited.Remove(value);
        }
    }

    private string FormatBoxedMemoryValue<T>(FormattingContext context, object value, int? highlightedIndex)
    {
        ReadOnlyMemory<T> memory = value is Memory<T> writableMemory ? writableMemory : (ReadOnlyMemory<T>)value;
        return FormatReadOnlySpanValue(context, memory.Span, highlightedIndex);
    }

    private bool TryFormatKeyValuePairOrTupleValue(FormattingContext context, object value, [NotNullWhen(true)] out string? result)
    {
        // KeyValuePair and tuples are formatted from their parts, so nested values get the same invariant formatting,
        // quoting and escaping as top-level values instead of whatever their own ToString produces.
        var type = value.GetType();
        var isKeyValuePair = type.IsConstructedGenericType && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>);
        var tuple = !isKeyValuePair && value is ITuple valueTuple && IsSystemTupleType(type) ? valueTuple : null;
        if (!isKeyValuePair && tuple is null)
        {
            result = null;
            return false;
        }

        if (!context.TryEnterNestedValue())
        {
            result = MaxDepthMarker;
            return true;
        }

        try
        {
            if (isKeyValuePair)
            {
                var key = type.GetProperty(nameof(KeyValuePair<,>.Key))!.GetValue(value);
                var pairValue = type.GetProperty(nameof(KeyValuePair<,>.Value))!.GetValue(value);
                result = "[" + FormatValue(context, key) + ", " + FormatValue(context, pairValue) + "]";
                return true;
            }

            var builder = new StringBuilder();
            builder.Append('(');
            for (var i = 0; i < tuple!.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(FormatValue(context, tuple[i]));
            }

            builder.Append(')');
            result = builder.ToString();
            return true;
        }
        finally
        {
            context.ExitNestedValue();
        }

        static bool IsSystemTupleType(Type type)
        {
            if (!type.IsConstructedGenericType || type.Assembly != typeof(object).Assembly)
                return false;

            var name = type.GetGenericTypeDefinition().FullName;
            return name is not null && (name.StartsWith("System.ValueTuple`", StringComparison.Ordinal) || name.StartsWith("System.Tuple`", StringComparison.Ordinal));
        }
    }

    private string FormatStructuralValue(FormattingContext context, object? value)
    {
        if (value is StructuralMissingValue)
            return "<missing>";

        return FormatValue(context, value);
    }

    /// <summary>
    /// Unequal values can still be formatted identically: <c>0.1f</c> and <c>0.1</c>, or a ToString that ignores part of
    /// the state. Two identical lines would show no difference at all, so the message then explains why they differ.
    /// </summary>
    private static void AppendIdenticalFormattingDetails(AssertionMessageBuilder builder, string expectedText, string actualText, Type? expectedType, Type? actualType, string typeLabel)
    {
        if (!string.Equals(expectedText, actualText, StringComparison.Ordinal))
            return;

        if (expectedType != actualType)
        {
            builder.AppendGroup(
                ($"Expected {typeLabel}", FormatType(expectedType)),
                ($"Actual {typeLabel}", FormatType(actualType)));
        }
        else
        {
            builder.Append("Note", "The values differ but are formatted identically.");
        }
    }

    private static Type? GetItemType<T>(IReadOnlyList<T> items, int? index)
    {
        return index is int i && i >= 0 && i < items.Count ? items[i]?.GetType() : null;
    }

    private static Type? GetItemType<T>(ReadOnlySpan<T> items, int index)
    {
        return index >= 0 && index < items.Length ? items[index]?.GetType() : null;
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
        return FormatReadOnlySpanValue(CreateContext(), value, highlightedIndex);
    }

    private string FormatReadOnlySpanValue<T>(FormattingContext context, ReadOnlySpan<T> value, int? highlightedIndex = null)
    {
        if (typeof(T) == typeof(char))
        {
            var chars = unsafe(System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(ref System.Runtime.CompilerServices.Unsafe.As<T, char>(ref System.Runtime.InteropServices.MemoryMarshal.GetReference(value)), value.Length));
            return FormatStringValue(context, chars, highlightedIndex);
        }

        if (!context.TryEnterNestedValue())
            return MaxDepthMarker;

        try
        {
            // Same window as enumerables: a span can be arbitrarily large, and only the items around the highlighted one are useful.
            var window = context.GetItemWindow(highlightedIndex);
            var items = new List<string>();
            var hasSkippedItems = false;
            for (var index = 0; index < value.Length; index++)
            {
                if (index > window.MaxIndex)
                {
                    items.Add(Ellipsis);
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
                    items.Add(Ellipsis);
                    hasSkippedItems = false;
                }

                items.Add(FormatHighlightedValue(FormatValue(context, value[index]), index, highlightedIndex));
            }

            if (hasSkippedItems)
            {
                items.Add(Ellipsis);
            }

            return $"[{string.Join(", ", items)}]";
        }
        finally
        {
            context.ExitNestedValue();
        }
    }

    private string FormatKeyValuePairs<TKey, TValue>(FormattingContext context, IEnumerable<KeyValuePair<TKey, TValue>> value)
    {
        return FormatKeyValueEntries(context, value, useDictionaryEnumerator: false, static item => item is KeyValuePair<TKey, TValue> pair ? (pair.Key, pair.Value) : default);
    }

    private string FormatDictionary(FormattingContext context, System.Collections.IDictionary value)
    {
        // A dictionary enumerates DictionaryEntry values only through IDictionary.GetEnumerator.
        return FormatKeyValueEntries(context, value, useDictionaryEnumerator: true, static item => item is System.Collections.DictionaryEntry entry ? (entry.Key, entry.Value) : default);
    }

    private string FormatKeyValueEntries(FormattingContext context, System.Collections.IEnumerable value, bool useDictionaryEnumerator, Func<object?, (object? Key, object? Value)> getEntry)
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

                if (index >= context.MaxFormattedItems)
                {
                    items.Add(Ellipsis);
                    isTruncated = true;
                    break;
                }

                var (key, entryValue) = getEntry(item);
                items.Add(FormatValue(context, key) + ": " + FormatValue(context, entryValue));
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

    private string FormatEnumerableValue(FormattingContext context, System.Collections.IEnumerable value, int? highlightedIndex)
    {
        if (!context.Visited.Add(value))
            return "<circular reference>";

        System.Collections.IEnumerator? enumerator = null;
        var isNested = false;
        try
        {
            if (!context.TryEnterNestedValue())
                return MaxDepthMarker;

            isNested = true;
            var items = new List<string>();
            var window = context.GetItemWindow(highlightedIndex);
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
                    items.Add(Ellipsis);
                    isTruncated = true;
                    break;
                }

                if (window.IsVisible(index))
                {
                    if (hasSkippedItems)
                    {
                        items.Add(Ellipsis);
                        hasSkippedItems = false;
                    }

                    items.Add(FormatHighlightedValue(FormatValue(context, item), index, highlightedIndex));
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
                    items.Add(Ellipsis);
                }

                AddEnumerationError(items, value, enumerationError);
            }

            return $"[{string.Join(", ", items)}]";
        }
        finally
        {
            DisposeEnumerator(enumerator);
            if (isNested)
            {
                context.ExitNestedValue();
            }

            context.Visited.Remove(value);
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
        EnsureObservedItems(snapshot, CreateContext().MaxFormattedItems - 1);

        return snapshot.Items;
    }

    /// <inheritdoc cref="GetFormattedItems{T}(CollectionSnapshot{T})"/>
    internal async Task<IReadOnlyList<T>> GetFormattedItemsAsync<T>(AsyncCollectionSnapshot<T> snapshot)
    {
        await EnsureObservedItemsAsync(snapshot, CreateContext().MaxFormattedItems - 1).ConfigureAwait(false);

        return snapshot.Items;
    }

    private static void EnsureObservedItems<T>(CollectionSnapshot<T> snapshot, int maxIndex)
    {
        // The item after maxIndex is read too, to know whether the formatted items are followed by an ellipsis. The
        // count is computed as a long so that maxIndex = int.MaxValue does not overflow.
        var itemCount = (long)maxIndex + 1;
        if (snapshot.IsComplete || snapshot.ObservedCount > itemCount)
            return;

        try
        {
            for (var index = snapshot.ObservedCount; !snapshot.IsComplete && snapshot.ObservedCount <= itemCount && snapshot.TryGetItem(index, out _); index++)
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
        var itemCount = (long)maxIndex + 1;
        if (snapshot.IsComplete || snapshot.ObservedCount > itemCount)
            return;

        try
        {
            for (var index = snapshot.ObservedCount; !snapshot.IsComplete && snapshot.ObservedCount <= itemCount && await snapshot.TryGetItem(index).ConfigureAwait(false) is (true, _); index++)
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
        return FormatStringValue(Assert.ErrorFormatter.CreateContext(), value, highlightedIndex);
    }

    private static string FormatStringValue(FormattingContext context, ReadOnlySpan<char> value, int? highlightedIndex)
    {
        var maxLength = context.MaxFormattedStringLength;
        if (value.Length <= maxLength)
        {
            var result = new StringBuilder(value.Length + 2);
            result.Append('"');
            AppendEscapedText(result, value, highlightedIndex, quote: '"');
            result.Append('"');
            return result.ToString();
        }

        // Only the characters around the highlighted one are useful, and escaping a huge string in full could build a
        // message too large to allocate, hiding the assertion failure behind an OutOfMemoryException.
        var (start, end) = GetTextWindow(value, maxLength, highlightedIndex);
        var truncated = new StringBuilder(end - start + 32);
        if (start > 0)
        {
            truncated.Append(Ellipsis);
        }

        truncated.Append('"');
        AppendEscapedText(truncated, value[start..end], highlightedIndex - start, quote: '"');
        truncated.Append('"');
        if (end < value.Length)
        {
            truncated.Append(Ellipsis);
        }

        truncated.Append(" (length: ").Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(')');
        return truncated.ToString();
    }

    /// <summary>Gets the range of at most <paramref name="maxLength"/> characters to format, centered on the highlighted character.</summary>
    private static (int Start, int End) GetTextWindow(ReadOnlySpan<char> value, int maxLength, int? highlightedIndex)
    {
        var start = 0;
        if (highlightedIndex is int index)
        {
            // A highlighted index past the end (the shorter of two strings) centers the window on the end of the string.
            start = Math.Max(0, Math.Min(index, value.Length) - (maxLength / 2));
        }

        var end = maxLength >= value.Length - start ? value.Length : start + maxLength;
        start = Math.Max(0, end - maxLength);

        // Never split a surrogate pair: a lone half would be escaped and look like a different character.
        if (start > 0 && char.IsLowSurrogate(value[start]) && char.IsHighSurrogate(value[start - 1]))
        {
            start--;
        }

        if (end < value.Length && char.IsLowSurrogate(value[end]) && char.IsHighSurrogate(value[end - 1]))
        {
            end++;
        }

        return (start, end);
    }

    private static string FormatCharValue(char value)
    {
        var result = new StringBuilder(8);
        result.Append('\'');
        AppendEscapedText(result, [value], highlightedIndex: null, quote: '\'');
        result.Append('\'');
        return result.ToString();
    }

    private static void AppendEscapedText(StringBuilder result, ReadOnlySpan<char> value, int? highlightedIndex, char? quote, bool escapeBackslash = true)
    {
        for (var i = 0; i < value.Length; i++)
        {
            var start = result.Length;
            var isHighlighted = i == highlightedIndex;
            if (char.IsHighSurrogate(value[i]) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                // A surrogate pair is one character: it is never split, and highlighting either half highlights both.
                var rune = new Rune(value[i], value[i + 1]);
                if (IsInvisibleOrAmbiguous(Rune.GetUnicodeCategory(rune), rune.Value))
                {
                    result.Append("\\U").Append(rune.Value.ToString("X8", CultureInfo.InvariantCulture));
                }
                else
                {
                    result.Append(value[i]).Append(value[i + 1]);
                }

                i++;
                isHighlighted |= i == highlightedIndex;
            }
            else
            {
                AppendEscapedChar(result, value[i], quote, escapeBackslash);
            }

            if (isHighlighted)
            {
                var escaped = result.ToString(start, result.Length - start);
                result.Length = start;
                result.Append(Underline(escaped));
            }
        }

        static void AppendEscapedChar(StringBuilder result, char value, char? quote, bool escapeBackslash)
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
                case '\\' when escapeBackslash:
                    result.Append("\\\\");
                    break;
                case var _ when value == quote:
                    result.Append('\\').Append(value);
                    break;
                case var _ when IsInvisibleOrAmbiguous(char.GetUnicodeCategory(value), value):
                    result.Append("\\u").Append(((int)value).ToString("X4", CultureInfo.InvariantCulture));
                    break;
                default:
                    result.Append(value);
                    break;
            }
        }

        // Characters that render as nothing, as a line break, or as something indistinguishable from a regular space.
        // Printing them raw would make different values look identical or break the message layout.
        static bool IsInvisibleOrAmbiguous(UnicodeCategory category, int value)
        {
            return category switch
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

    /// <summary>The state of formatting one message: the options read once for the whole message, and the values being formatted.</summary>
    private sealed class FormattingContext(FormatterOptions options)
    {
        public int MaxFormattedItems { get; } = options.MaxFormattedItems;

        public int PrefixItemCount { get; } = options.PrefixItemCount;

        public int SuffixItemCount { get; } = options.SuffixItemCount;

        public int HighlightedContextItemCount { get; } = options.HighlightedContextItemCount;

        public int MaxFormattedStringLength { get; } = options.MaxFormattedStringLength;

        /// <summary>Gets the sequences being formatted, to detect circular references.</summary>
        public HashSet<object> Visited { get; } = new(ReferenceEqualityComparer.Instance);

        private int Depth { get; set; }

        public bool TryEnterNestedValue()
        {
            if (Depth >= MaxFormattingDepth || !RuntimeHelpers.TryEnsureSufficientExecutionStack())
                return false;

            Depth++;
            return true;
        }

        public void ExitNestedValue()
        {
            Depth--;
        }

        public int GetMaxFormattedIndex(int? highlightedIndex)
        {
            // The options can be as large as int.MaxValue, so the sums saturate instead of overflowing to a negative index.
            if (IsFocusedHighlightedItem(highlightedIndex))
                return SaturatingAdd(highlightedIndex.GetValueOrDefault(), HighlightedContextItemCount);

            return Math.Max(MaxFormattedItems - 1, SaturatingAdd(highlightedIndex.GetValueOrDefault(-1), SuffixItemCount));
        }

        public ItemWindow GetItemWindow(int? highlightedIndex)
        {
            if (IsFocusedHighlightedItem(highlightedIndex))
            {
                var focusStartIndex = Math.Max(PrefixItemCount, highlightedIndex.GetValueOrDefault() - HighlightedContextItemCount);
                return new ItemWindow(PrefixItemCount, focusStartIndex, GetMaxFormattedIndex(highlightedIndex));
            }

            return new ItemWindow(MaxFormattedItems, FocusStartIndex: -1, GetMaxFormattedIndex(highlightedIndex));
        }

        private bool IsFocusedHighlightedItem(int? highlightedIndex)
        {
            return highlightedIndex is not null && highlightedIndex.GetValueOrDefault() >= MaxFormattedItems;
        }

        private static int SaturatingAdd(int left, int right)
        {
            return (int)Math.Clamp((long)left + right, int.MinValue, int.MaxValue);
        }
    }

    private sealed class OptionsScope(FormatterOptions? previousOptions) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            ScopedOptions.Value = previousOptions;
        }
    }
}
#pragma warning restore CA1822, CA1852
