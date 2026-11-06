namespace Meziantou.Framework.Charts;

/// <summary>An axis of a <see cref="ParallelCoordinatesMark{T}"/>: a channel of the rows and its own scale.</summary>
/// <typeparam name="T">The type of the rows.</typeparam>
public sealed class ParallelDimension<T>
{
    /// <summary>Initializes a new instance of the <see cref="ParallelDimension{T}"/> class.</summary>
    /// <param name="name">The name of the axis, drawn above it.</param>
    /// <param name="value">The channel of the value of a row on the axis: a number, a time or a category.</param>
    public ParallelDimension(string name, Func<T, object?> value)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);
        Name = name;
        Value = value;
    }

    /// <summary>Gets the name of the axis.</summary>
    public string Name { get; }

    /// <summary>Gets the channel of the value of a row on the axis.</summary>
    public Func<T, object?> Value { get; }

    /// <summary>Gets or sets the scale of the axis, or <see langword="null"/> to infer it from the values: a linear scale for numbers, a time scale for times, a point scale for categories.</summary>
    public Scale? Scale { get; set; }
}
