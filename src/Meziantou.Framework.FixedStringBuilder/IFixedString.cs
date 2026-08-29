namespace Meziantou.Framework.FixedStringBuilder;

/// <summary>
/// The base interface for a fixed string.
/// </summary>
public interface IFixedString : ISpanFormattable
{
    /// <summary>
    /// Gets the maximum number of characters in this fixed string.
    /// </summary>
    static abstract int MaxLength { get; }

    /// <summary>
    /// Gets the number of characters in the string. The length is always less than or equal to <see cref="MaxLength"/>.
    /// </summary>
    int Length { get; }

    /// <summary>
    /// Resets this string to an empty string.
    /// </summary>
    /// <remarks>
    /// Only the length is reset. The characters that were written are not overwritten: they stay in the underlying
    /// buffer and remain readable through <see cref="GetUnsafeFullSpan"/>. Do not rely on this method to erase
    /// sensitive data.
    /// </remarks>
    void Clear();

    /// <summary>
    /// Resets this string to an empty string, optionally overwriting the underlying buffer with zeros.
    /// </summary>
    /// <param name="zeroBuffer">
    /// <see langword="true"/> to overwrite the whole underlying buffer with <c>'\0'</c>, so the characters that
    /// were written can no longer be read through <see cref="GetUnsafeFullSpan"/>; <see langword="false"/> to only
    /// reset the length, as <see cref="Clear()"/> does.
    /// </param>
    /// <remarks>
    /// The whole buffer is overwritten, not only the characters up to <see cref="Length"/>: a previous longer value
    /// can still be present after <see cref="Length"/>.
    /// </remarks>
    void Clear(bool zeroBuffer);

    /// <summary>
    /// Returns a span of all the characters up to <see cref="MaxLength"/>.
    /// </summary>
    /// <returns>A span of characters that contains the characters of this string.</returns>
    /// <remarks>This method is unsafe as it doesn't protect from returning a ref to a struct that could go out of scope. Favor using AsSpan() on individual FixedStrings structs.
    /// This method can only be used through a generic constraint or by explicit casting/boxing to <see cref="IFixedString"/>.
    /// </remarks>
    Span<char> GetUnsafeFullSpan();
}

/// <summary>
/// The base interface for a fixed string with its implementation type.
/// </summary>
public interface IFixedString<T> : IFixedString, IEquatable<T> where T : IFixedString<T>
{
    /// <summary>
    /// Converts a string to a fixed string.
    /// </summary>
    /// <param name="s">The string to convert to a fixed string.</param>
    static abstract implicit operator T(string s);
}

