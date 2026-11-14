namespace Meziantou.Framework.Fonts;

/// <summary>
/// Receives the hints of a Type 2 charstring (Adobe Technical Note #5177, section 4.3, "Hint Operators") while
/// <see cref="CffCharstringInterpreter"/> runs it: an <see cref="OutlineSink"/> that implements this interface gets the
/// stems and the masks between the commands of the outline, in the order of the charstring.
/// </summary>
internal interface ICffHintSink
{
    /// <summary>
    /// Receives a stem hint: <c>hstem</c> and <c>hstemhm</c> give horizontal stems (two vertical coordinates), <c>vstem</c>,
    /// <c>vstemhm</c> and the arguments before the first <c>hintmask</c> vertical stems (two horizontal coordinates).
    /// </summary>
    /// <param name="isHorizontal">Whether the stem is horizontal: its edges are vertical coordinates.</param>
    /// <param name="edge">The first edge, in font units.</param>
    /// <param name="width">
    /// The distance to the second edge. A horizontal stem of width -20 is a ghost hint for a top edge at
    /// <paramref name="edge"/>, and one of width -21 a ghost hint for a bottom edge at <paramref name="edge"/> + <paramref name="width"/>.
    /// </param>
    void AddStem(bool isHorizontal, double edge, double width);

    /// <summary>
    /// Receives a <c>hintmask</c> (the stems the next part of the outline uses) or a <c>cntrmask</c> (stems whose counters
    /// are controlled together): one bit per stem, the most significant bit of the first byte for the first stem, the
    /// horizontal stems then the vertical stems in the order they were given.
    /// </summary>
    void SetMask(ReadOnlySpan<byte> mask, bool isCounterMask);
}
