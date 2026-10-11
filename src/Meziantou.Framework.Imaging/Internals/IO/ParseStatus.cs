using System.Diagnostics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The outcome of one <see cref="ImageParser{TResult}.Parse"/> call: the parser completed, or it needs more contiguous input
/// (which the end of the input may or may not satisfy).
/// </summary>
internal readonly struct ParseStatus : IEquatable<ParseStatus>
{
    private ParseStatus(int requiredBytes, bool acceptsEndOfInput)
    {
        RequiredBytes = requiredBytes;
        AcceptsEndOfInput = acceptsEndOfInput;
    }

    /// <summary>Gets a status meaning that the parser has produced its result; the remaining input is not examined.</summary>
    public static ParseStatus Complete => default;

    /// <summary>
    /// Gets the minimum number of contiguous unconsumed bytes the parser needs at the start of its next buffer, or 0 when
    /// the parser is complete.
    /// </summary>
    public int RequiredBytes { get; }

    /// <summary>
    /// Gets a value indicating whether the end of the input satisfies the request as well as <see cref="RequiredBytes"/>
    /// bytes do (<see cref="NeedMoreDataOrEnd"/>).
    /// </summary>
    public bool AcceptsEndOfInput { get; }

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
        return new ParseStatus(requiredBytes, acceptsEndOfInput: false);
    }

    /// <summary>
    /// Creates a status requesting at least <paramref name="requiredBytes"/> contiguous unconsumed bytes, or the end of the
    /// input: the request of a parser whose structure is delimited by the end of the input (an optional terminator, a
    /// trailer located from the last bytes, a container buffered whole).
    /// </summary>
    /// <param name="requiredBytes">The minimum length of the next buffer (positive), unless the input ends first.</param>
    /// <returns>The status.</returns>
    /// <remarks>
    /// The encoded-byte limit applies as it does to <see cref="NeedMoreData"/>, except that an input that ends exactly at
    /// the limit is the end of the input, not a limit failure: the driver then calls the parser again with
    /// <c>isEndOfInput</c> set. A parser must not return this status once it was told the input ended.
    /// </remarks>
    public static ParseStatus NeedMoreDataOrEnd(int requiredBytes)
    {
        Debug.Assert(requiredBytes > 0);
        return new ParseStatus(requiredBytes, acceptsEndOfInput: true);
    }

    public bool Equals(ParseStatus other) => RequiredBytes == other.RequiredBytes && AcceptsEndOfInput == other.AcceptsEndOfInput;

    public override bool Equals([NotNullWhen(true)] object? obj) => obj is ParseStatus other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(RequiredBytes, AcceptsEndOfInput);

    public override string ToString()
    {
        if (IsComplete)
            return "Complete";

        return AcceptsEndOfInput
            ? string.Create(CultureInfo.InvariantCulture, $"NeedMoreDataOrEnd({RequiredBytes})")
            : string.Create(CultureInfo.InvariantCulture, $"NeedMoreData({RequiredBytes})");
    }
}
