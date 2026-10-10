using System.Diagnostics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The outcome of one <see cref="ImageParser{TResult}.Parse"/> call: the parser completed, or it needs more contiguous input.</summary>
internal readonly struct ParseStatus : IEquatable<ParseStatus>
{
    private ParseStatus(int requiredBytes) => RequiredBytes = requiredBytes;

    /// <summary>Gets a status meaning that the parser has produced its result; the remaining input is not examined.</summary>
    public static ParseStatus Complete => default;

    /// <summary>
    /// Gets the minimum number of contiguous unconsumed bytes the parser needs at the start of its next buffer, or 0 when
    /// the parser is complete.
    /// </summary>
    public int RequiredBytes { get; }

    /// <summary>Gets a value indicating whether the parser is complete.</summary>
    public bool IsComplete => RequiredBytes == 0;

    /// <summary>Creates a status requesting at least <paramref name="requiredBytes"/> contiguous unconsumed bytes.</summary>
    /// <param name="requiredBytes">
    /// The minimum length of the next buffer (positive). It is normally larger than the unconsumed remainder of the current
    /// buffer; a smaller value lets a parser yield after a bounded amount of work (the driver calls it again without reading).
    /// </param>
    /// <returns>The status.</returns>
    public static ParseStatus NeedMoreData(int requiredBytes)
    {
        Debug.Assert(requiredBytes > 0);
        return new ParseStatus(requiredBytes);
    }

    public bool Equals(ParseStatus other) => RequiredBytes == other.RequiredBytes;

    public override bool Equals([NotNullWhen(true)] object? obj) => obj is ParseStatus other && Equals(other);

    public override int GetHashCode() => RequiredBytes;

    public override string ToString() => IsComplete ? "Complete" : string.Create(CultureInfo.InvariantCulture, $"NeedMoreData({RequiredBytes})");
}
