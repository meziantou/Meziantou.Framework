using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// A JavaScript string: a sequence of UTF-16 code units (lone surrogates allowed). A concatenation creates a rope, which
/// is flattened the first time its characters are read. A rope read by index is flattened in a buffer that has room
/// after its characters: when the leftmost part of a rope read by index is such a string, its other parts are appended
/// to the same buffer, so appending a character and reading one in a loop does not copy the string again and again.
/// </summary>
[DebuggerDisplay("{ToString()}")]
internal sealed class JsString : JsCell, IEquatable<JsString>
{
    /// <summary>The maximum length of a string (the maximum length of a .NET string).</summary>
    public const int MaxLength = 0x3FFFFFDF;

    // Concatenations shorter than this are flattened immediately
    private const int MinRopeLength = 64;

    private static readonly JsString[] SingleCharacters = CreateSingleCharacters();

    public static readonly JsString Empty = new(string.Empty);

    private string? _value;
    private JsString? _left;
    private JsString? _right;

    // The characters of the string are the first Length characters of the buffer
    private StringBuffer? _buffer;

    private JsString(string value)
        : base(JsValueKind.String)
    {
        _value = value;
        Length = value.Length;
    }

    private JsString(JsString left, JsString right)
        : base(JsValueKind.String)
    {
        _left = left;
        _right = right;
        Length = left.Length + right.Length;
    }

    public int Length { get; }

    public bool IsEmpty => Length == 0;

    public char this[int index]
    {
        get
        {
            if (_value is { } value)
                return value[index];

            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)index, (uint)Length, nameof(index));
            var buffer = _buffer ?? FlattenToBuffer();
            return buffer.Chars[index];
        }
    }

    public static JsString Create(string value)
    {
        if (value.Length == 0)
            return Empty;

        if (value.Length == 1 && value[0] < SingleCharacters.Length)
            return SingleCharacters[value[0]];

        return new JsString(value);
    }

    public static JsString Create(char value) => value < SingleCharacters.Length ? SingleCharacters[value] : new JsString(value.ToString());

    /// <summary>Concatenates two strings (a RangeError is thrown by the caller when the result is too long: see <see cref="CanConcat"/>).</summary>
    public static JsString Concat(JsString left, JsString right)
    {
        if (left.Length == 0)
            return right;

        if (right.Length == 0)
            return left;

        var length = (long)left.Length + right.Length;
        if (length > MaxLength)
            throw new InvalidOperationException("The string is too long");

        if (length < MinRopeLength)
            return new JsString(string.Concat(left.ToString(), right.ToString()));

        return new JsString(left, right);
    }

    public static bool CanConcat(JsString left, JsString right) => (long)left.Length + right.Length <= MaxLength;

    public override string ToString()
    {
        if (_value is not null)
            return _value;

        Flatten();
        return _value!;
    }

    private void Flatten()
    {
        // A concatenation allocates nothing: the memory limit of the script is checked when the string is built
        JsEngine.EnsureCurrentCanAllocate((long)Length * sizeof(char));
        if (_buffer is { } buffer)
        {
            _value = new string(buffer.Chars, 0, Length);

            // The buffer is kept while a concatenation can extend it
            if (buffer.Length != Length)
            {
                _buffer = null;
            }

            return;
        }

        _value = string.Create(Length, this, static (span, root) => root.CopyTo(span, start: 0));
        _left = null;
        _right = null;
    }

    /// <summary>Flattens a rope in a buffer: the buffer of its leftmost part when it can be extended, a new one otherwise.</summary>
    private StringBuffer FlattenToBuffer()
    {
        var leftmost = this;
        while (leftmost._left is { } left)
        {
            leftmost = left;
        }

        StringBuffer buffer;
        int start;
        if (leftmost._buffer is { } leftmostBuffer && leftmostBuffer.Chars.Length >= Length && Interlocked.CompareExchange(ref leftmostBuffer.Length, Length, leftmost.Length) == leftmost.Length)
        {
            // The characters of the leftmost part are already in the buffer, and no other string uses the characters after them
            buffer = leftmostBuffer;
            start = leftmost.Length;
        }
        else
        {
            // Room for the next concatenations
            var capacity = (int)Math.Min((long)Length * 2, MaxLength);
            JsEngine.EnsureCurrentCanAllocate((long)capacity * sizeof(char));
            buffer = new StringBuffer(new char[capacity], Length);
            start = 0;
        }

        CopyTo(buffer.Chars.AsSpan(0, Length), start);
        _buffer = buffer;
        _left = null;
        _right = null;
        return buffer;
    }

    /// <summary>Copies the characters [start, Length) of the string to the same positions of the destination.</summary>
    private void CopyTo(Span<char> destination, int start)
    {
        // Iterative, so that a deep rope does not overflow the stack
        var stack = new Stack<JsString>();
        stack.Push(this);
        var position = 0;
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            var end = position + node.Length;
            if (end <= start)
            {
                position = end;
                continue;
            }

            ReadOnlySpan<char> chars;
            if (node._value is { } value)
            {
                chars = value;
            }
            else if (node._buffer is { } buffer)
            {
                chars = buffer.Chars.AsSpan(0, node.Length);
            }
            else
            {
                stack.Push(node._right!);
                stack.Push(node._left!);
                continue;
            }

            var skip = Math.Max(start - position, 0);
            chars[skip..].CopyTo(destination[(position + skip)..]);
            position = end;
        }
    }

    public bool Equals([NotNullWhen(true)] JsString? other)
    {
        if (other is null)
            return false;

        if (ReferenceEquals(this, other))
            return true;

        return Length == other.Length && string.Equals(ToString(), other.ToString(), StringComparison.Ordinal);
    }

    public override bool Equals([NotNullWhen(true)] object? obj) => obj is JsString other && Equals(other);

    public override int GetHashCode() => string.GetHashCode(ToString(), StringComparison.Ordinal);

    /// <summary>The characters of strings: each string uses the first characters (its length); a concatenation can append characters after the last ones in use.</summary>
    private sealed class StringBuffer(char[] chars, int length)
    {
        public readonly char[] Chars = chars;

        // The number of characters in use
        public int Length = length;
    }

    private static JsString[] CreateSingleCharacters()
    {
        var result = new JsString[128];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = new JsString(((char)i).ToString());
        }

        return result;
    }
}
