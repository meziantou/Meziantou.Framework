using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meziantou.Framework;

// https://github.com/dotnet/runtime/blob/main/src/libraries/Common/src/System/Text/ValueStringBuilder.cs
#if PUBLIC_VALUESTRINGBUILDER
public
#else
internal
#endif
ref partial struct ValueStringBuilder
{
    private char[]? _arrayToReturnToPool;
    private Span<char> _chars;
    private int _pos;

    /// <summary>
    /// Initializes a builder that writes into <paramref name="initialBuffer" /> and only rents from
    /// <see cref="ArrayPool{T}" /> once the content outgrows it.
    /// </summary>
    /// <param name="initialBuffer">The buffer to write into, typically stack-allocated.</param>
    public ValueStringBuilder(Span<char> initialBuffer)
    {
        _arrayToReturnToPool = null;
        _chars = initialBuffer;
        _pos = 0;
    }

    /// <summary>
    /// Initializes a builder backed by a buffer of at least <paramref name="initialCapacity" /> characters
    /// rented from <see cref="ArrayPool{T}" />.
    /// </summary>
    /// <param name="initialCapacity">The minimum number of characters the buffer must hold.</param>
    public ValueStringBuilder(int initialCapacity)
    {
        _arrayToReturnToPool = ArrayPool<char>.Shared.Rent(initialCapacity);
        _chars = _arrayToReturnToPool;
        _pos = 0;
    }

    /// <summary>
    /// Gets or sets the number of characters written so far. Setting it truncates the content, or exposes
    /// characters already present in the buffer when it is increased.
    /// </summary>
    public int Length
    {
        readonly get => _pos;
        set
        {
            Debug.Assert(value >= 0);
            Debug.Assert(value <= _chars.Length);
            _pos = value;
        }
    }

    /// <summary>
    /// Gets the number of characters the current buffer can hold before it has to grow.
    /// </summary>
    public readonly int Capacity => _chars.Length;

    /// <summary>
    /// Grows the buffer so that it can hold at least <paramref name="capacity" /> characters.
    /// </summary>
    /// <param name="capacity">The minimum capacity required.</param>
    public void EnsureCapacity(int capacity)
    {
        Debug.Assert(capacity >= 0);

        if ((uint)capacity > (uint)_chars.Length)
        {
            Grow(capacity - _pos);
        }
    }

    /// <summary>
    /// Resets <see cref="Length" /> to 0, keeping the current buffer. The characters are not erased and stay
    /// readable through <see cref="RawChars" />.
    /// </summary>
    public void Clear()
    {
        _pos = 0;
    }

    /// <summary>
    /// Writes a null character after the content, growing the buffer if needed, without changing
    /// <see cref="Length" />.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void NullTerminate()
    {
        EnsureCapacity(_pos + 1);
        _chars[_pos] = '\0';
    }

    /// <summary>
    /// Returns a reference to the first character of the buffer, so the builder can be used with a
    /// <see langword="fixed" /> statement. The result is not null-terminated unless
    /// <see cref="NullTerminate" /> was called.
    /// </summary>
    public ref char GetPinnableReference()
    {
        return ref MemoryMarshal.GetReference(_chars);
    }

    /// <summary>
    /// Gets a reference to the character at <paramref name="index" />, which can be used to overwrite it.
    /// </summary>
    /// <param name="index">The position of the character, which must be less than <see cref="Length" />.</param>
    public ref char this[int index]
    {
        get
        {
            Debug.Assert(index < _pos);
            return ref _chars[index];
        }
    }

    /// <summary>
    /// Returns the characters written so far and disposes this instance, returning the rented buffer to the pool.
    /// </summary>
    /// <remarks>
    /// This method is destructive. After it returns, the builder is reset to its default state: <see cref="Length"/>
    /// and <see cref="Capacity"/> are 0, a second call returns an empty string, and appending starts a new buffer.
    /// Use <see cref="AsSpan()"/> to read the content without disposing the builder.
    /// </remarks>
    public override string ToString()
    {
        var s = _chars.Slice(0, _pos).ToString();
        Dispose();
        return s;
    }

    /// <summary>
    /// Gets the whole buffer, including the part past <see cref="Length" />.
    /// </summary>
    /// <remarks>
    /// Everything beyond <see cref="Length" /> is uninitialized pooled memory and may hold data left by a
    /// previous user of the array. Use <see cref="AsSpan()" /> to read only what was written.
    /// </remarks>
    public readonly Span<char> RawChars => _chars;

    /// <summary>
    /// Returns the characters written so far, without disposing the builder.
    /// </summary>
    public readonly ReadOnlySpan<char> AsSpan() => _chars.Slice(0, _pos);

    /// <summary>
    /// Returns the characters written so far from <paramref name="start" /> onwards.
    /// </summary>
    /// <param name="start">The position to start at.</param>
    public readonly ReadOnlySpan<char> AsSpan(int start) => _chars.Slice(start, _pos - start);

    /// <summary>
    /// Returns <paramref name="length" /> characters starting at <paramref name="start" />.
    /// </summary>
    /// <param name="start">The position to start at.</param>
    /// <param name="length">The number of characters to return.</param>
    public readonly ReadOnlySpan<char> AsSpan(int start, int length) => _chars.Slice(0, _pos).Slice(start, length);

    /// <summary>
    /// Inserts <paramref name="value" /> <paramref name="count" /> times at <paramref name="index" />.
    /// </summary>
    /// <param name="index">The position to insert at.</param>
    /// <param name="value">The character to insert.</param>
    /// <param name="count">The number of times to insert it.</param>
    public void Insert(int index, char value, int count)
    {
        if (_pos > _chars.Length - count)
        {
            Grow(count);
        }

        var remaining = _pos - index;
        _chars.Slice(index, remaining).CopyTo(_chars.Slice(index + count));
        _chars.Slice(index, count).Fill(value);
        _pos += count;
    }

    /// <summary>
    /// Inserts <paramref name="s" /> at <paramref name="index" />. A <see langword="null" /> string is ignored.
    /// </summary>
    /// <param name="index">The position to insert at.</param>
    /// <param name="s">The string to insert.</param>
    public void Insert(int index, string? s)
    {
        if (s is null)
        {
            return;
        }

        var count = s.Length;
        if (_pos > _chars.Length - count)
        {
            Grow(count);
        }

        var remaining = _pos - index;
        _chars.Slice(index, remaining).CopyTo(_chars.Slice(index + count));
        s.AsSpan().CopyTo(_chars.Slice(index));
        _pos += count;
    }

    /// <summary>
    /// Appends a single character.
    /// </summary>
    /// <param name="c">The character to append.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Append(char c)
    {
        var pos = _pos;
        var chars = _chars;
        if ((uint)pos < (uint)chars.Length)
        {
            chars[pos] = c;
            _pos = pos + 1;
        }
        else
        {
            GrowAndAppend(c);
        }
    }

    /// <summary>
    /// Appends a string. A <see langword="null" /> string is ignored.
    /// </summary>
    /// <param name="s">The string to append.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Append(string? s)
    {
        if (s is null)
        {
            return;
        }

        var pos = _pos;
        if (s.Length == 1 && (uint)pos < (uint)_chars.Length)
        {
            _chars[pos] = s[0];
            _pos = pos + 1;
        }
        else
        {
            AppendSlow(s);
        }
    }

    private void AppendSlow(string s)
    {
        var pos = _pos;
        if (pos > _chars.Length - s.Length)
        {
            Grow(s.Length);
        }

        s.AsSpan().CopyTo(_chars.Slice(pos));
        _pos += s.Length;
    }

    /// <summary>
    /// Appends <paramref name="c" /> <paramref name="count" /> times.
    /// </summary>
    /// <param name="c">The character to append.</param>
    /// <param name="count">The number of times to append it.</param>
    public void Append(char c, int count)
    {
        if (_pos > _chars.Length - count)
        {
            Grow(count);
        }

        var dst = _chars.Slice(_pos, count);
        for (var i = 0; i < dst.Length; i++)
        {
            dst[i] = c;
        }

        _pos += count;
    }

    /// <summary>
    /// Appends the characters of <paramref name="value" />.
    /// </summary>
    /// <param name="value">The characters to append.</param>
    public void Append(ReadOnlySpan<char> value)
    {
        var pos = _pos;
        if (pos > _chars.Length - value.Length)
        {
            Grow(value.Length);
        }

        value.CopyTo(_chars.Slice(_pos));
        _pos += value.Length;
    }

    /// <summary>
    /// Reserves <paramref name="length" /> characters at the end of the content and returns them so the caller
    /// can write into the buffer directly. <see cref="Length" /> is advanced immediately, so the reserved
    /// characters are part of the content whether or not they are written to.
    /// </summary>
    /// <param name="length">The number of characters to reserve.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<char> AppendSpan(int length)
    {
        var origPos = _pos;
        if (origPos > _chars.Length - length)
        {
            Grow(length);
        }

        _pos = origPos + length;
        return _chars.Slice(origPos, length);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void GrowAndAppend(char c)
    {
        Grow(1);
        Append(c);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Grow(int additionalCapacityBeyondPos)
    {
        Debug.Assert(additionalCapacityBeyondPos > 0);
        Debug.Assert(_pos > _chars.Length - additionalCapacityBeyondPos, "Grow called incorrectly, no resize is needed.");

        const uint ArrayMaxLength = 0x7FFFFFC7;

        // Add as uint and clamp: _pos + additionalCapacityBeyondPos overflows int for a builder close to the
        // maximum array length, and the negative result used to reach Rent as an out-of-range argument.
        var requiredCapacity = Math.Min((uint)_pos + (uint)additionalCapacityBeyondPos, ArrayMaxLength);

        var newCapacity = (int)Math.Max(
            requiredCapacity,
            Math.Min((uint)_chars.Length * 2, ArrayMaxLength));

        var poolArray = ArrayPool<char>.Shared.Rent(newCapacity);

        _chars.Slice(0, _pos).CopyTo(poolArray);

        var toReturn = _arrayToReturnToPool;
        _chars = _arrayToReturnToPool = poolArray;
        if (toReturn is not null)
        {
            ArrayPool<char>.Shared.Return(toReturn);
        }
    }

    /// <summary>
    /// Releases the buffer back to <see cref="ArrayPool{T}" /> and resets the builder. Calling it more than
    /// once is safe.
    /// </summary>
    /// <remarks>The content is not erased; it stays readable by the next consumer to rent the array.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
        var toReturn = _arrayToReturnToPool;
        this = default;
        if (toReturn is not null)
        {
            ArrayPool<char>.Shared.Return(toReturn);
        }
    }
}
