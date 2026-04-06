using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meziantou.Framework;

// https://github.com/dotnet/runtime/blob/main/src/libraries/Common/src/System/Text/ValueStringBuilder.cs
public ref partial struct ValueStringBuilder
{
    private char[]? _arrayToReturnToPool;
    private Span<char> _chars;
    private int _pos;

    public ValueStringBuilder(Span<char> initialBuffer)
    {
        _arrayToReturnToPool = null;
        _chars = initialBuffer;
        _pos = 0;
    }

    public ValueStringBuilder(int initialCapacity)
    {
        _arrayToReturnToPool = ArrayPool<char>.Shared.Rent(initialCapacity);
        _chars = _arrayToReturnToPool;
        _pos = 0;
    }

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

    public readonly int Capacity => _chars.Length;

    public void EnsureCapacity(int capacity)
    {
        Debug.Assert(capacity >= 0);

        if ((uint)capacity > (uint)_chars.Length)
        {
            Grow(capacity - _pos);
        }
    }

    public void Clear()
    {
        _pos = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void NullTerminate()
    {
        EnsureCapacity(_pos + 1);
        _chars[_pos] = '\0';
    }

    public ref char GetPinnableReference()
    {
        return ref MemoryMarshal.GetReference(_chars);
    }

    public ref char this[int index]
    {
        get
        {
            Debug.Assert(index < _pos);
            return ref _chars[index];
        }
    }

    public override string ToString()
    {
        var s = _chars.Slice(0, _pos).ToString();
        Dispose();
        return s;
    }

    public readonly Span<char> RawChars => _chars;

    public readonly ReadOnlySpan<char> AsSpan() => _chars.Slice(0, _pos);
    public readonly ReadOnlySpan<char> AsSpan(int start) => _chars.Slice(start, _pos - start);
    public readonly ReadOnlySpan<char> AsSpan(int start, int length) => _chars.Slice(start, length);

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

    public void Append([InterpolatedStringHandlerArgument("")] ref AppendInterpolatedStringHandler handler)
    {
        this = handler._valueStringBuilder;
    }

    public void Append(IFormatProvider? provider, [InterpolatedStringHandlerArgument("", nameof(provider))] ref AppendInterpolatedStringHandler handler)
    {
        _ = provider;

        this = handler._valueStringBuilder;
    }

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

        var newCapacity = (int)Math.Max(
            (uint)(_pos + additionalCapacityBeyondPos),
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
