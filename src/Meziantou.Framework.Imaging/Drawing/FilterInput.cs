using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>An input of a primitive of a <see cref="FilterGraph"/>: the drawing, its alpha, or the result of an earlier primitive.</summary>
/// <remarks>The default value is <see cref="Previous"/>.</remarks>
[StructLayout(LayoutKind.Auto)]
public readonly struct FilterInput : IEquatable<FilterInput>
{
    private FilterInput(FilterInputKind kind, int resultIndex)
    {
        Kind = kind;
        ResultIndex = resultIndex;
    }

    /// <summary>Gets the result of the primitive before, or the drawing for the first primitive.</summary>
    public static FilterInput Previous => default;

    /// <summary>Gets the drawing that is filtered.</summary>
    public static FilterInput SourceGraphic => new(FilterInputKind.SourceGraphic, 0);

    /// <summary>Gets the alpha of the drawing that is filtered, with black colors.</summary>
    public static FilterInput SourceAlpha => new(FilterInputKind.SourceAlpha, 0);

    /// <summary>Gets what the input is.</summary>
    public FilterInputKind Kind { get; }

    /// <summary>Gets the index of the primitive whose result the input is, for <see cref="FilterInputKind.Result"/>; 0 otherwise.</summary>
    public int ResultIndex { get; }

    /// <summary>Gets the result of an earlier primitive of the graph.</summary>
    /// <param name="index">The index of the primitive in the graph. It must be before the primitive that reads it.</param>
    /// <returns>The input.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative.</exception>
    public static FilterInput FromResult(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        return new FilterInput(FilterInputKind.Result, index);
    }

    /// <inheritdoc />
    public bool Equals(FilterInput other) => Kind == other.Kind && ResultIndex == other.ResultIndex;

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is FilterInput other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Kind, ResultIndex);

    /// <summary>Compares two inputs for equality.</summary>
    /// <param name="left">The first input.</param>
    /// <param name="right">The second input.</param>
    /// <returns><see langword="true"/> if they are the same input; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(FilterInput left, FilterInput right) => left.Equals(right);

    /// <summary>Compares two inputs for inequality.</summary>
    /// <param name="left">The first input.</param>
    /// <param name="right">The second input.</param>
    /// <returns><see langword="true"/> if they are different inputs; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(FilterInput left, FilterInput right) => !left.Equals(right);
}
