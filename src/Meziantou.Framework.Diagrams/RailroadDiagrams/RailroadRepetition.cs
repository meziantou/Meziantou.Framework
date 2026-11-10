namespace Meziantou.Framework.Diagrams;

/// <summary>
/// A repetition of a railroad diagram: a loop back below the expression, and a bypass above it when the expression can
/// be skipped. Bounds other than zero or one to unbounded, as the <c>2*4</c> of ABNF, are written below the loop.
/// </summary>
public sealed class RailroadRepetition : RailroadExpression
{
    private RailroadExpression _expression;
    private int _minimum;
    private int? _maximum;

    /// <summary>Initializes a new instance of the <see cref="RailroadRepetition"/> class.</summary>
    /// <param name="expression">The repeated expression.</param>
    /// <param name="minimum">The minimum number of times.</param>
    /// <param name="maximum">The maximum number of times, or <see langword="null"/> for no limit.</param>
    public RailroadRepetition(RailroadExpression expression, int minimum = 0, int? maximum = null)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentOutOfRangeException.ThrowIfNegative(minimum);
        if (maximum is < 0)
            throw new ArgumentOutOfRangeException(nameof(maximum), maximum, "The maximum cannot be negative.");

        _expression = expression;
        _minimum = minimum;
        _maximum = maximum;
    }

    /// <summary>Gets or sets the repeated expression.</summary>
    public RailroadExpression Expression
    {
        get => _expression;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _expression = value;
        }
    }

    /// <summary>Gets or sets the minimum number of times. With 0, the expression can be skipped.</summary>
    public int Minimum
    {
        get => _minimum;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _minimum = value;
        }
    }

    /// <summary>Gets or sets the maximum number of times, or <see langword="null"/> for no limit.</summary>
    public int? Maximum
    {
        get => _maximum;
        set
        {
            if (value is < 0)
                throw new ArgumentOutOfRangeException(nameof(value), value, "The maximum cannot be negative.");

            _maximum = value;
        }
    }
}
