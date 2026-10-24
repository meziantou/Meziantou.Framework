using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// A JavaScript string: a sequence of UTF-16 code units (lone surrogates allowed). A concatenation creates a rope, which
/// is flattened the first time its characters are read.
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

    public char this[int index] => ToString()[index];

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

        // Iterative, so that a deep rope does not overflow the stack
        var result = string.Create(Length, this, static (span, root) =>
        {
            var stack = new Stack<JsString>();
            stack.Push(root);
            var position = 0;
            while (stack.Count > 0)
            {
                var node = stack.Pop();
                if (node._value is { } value)
                {
                    value.CopyTo(span[position..]);
                    position += value.Length;
                }
                else
                {
                    stack.Push(node._right!);
                    stack.Push(node._left!);
                }
            }
        });

        _value = result;
        _left = null;
        _right = null;
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
