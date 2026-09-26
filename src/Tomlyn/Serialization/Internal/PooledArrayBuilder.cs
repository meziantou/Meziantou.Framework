// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace Tomlyn.Serialization.Internal;

internal struct PooledArrayBuilder<T> : IDisposable
{
    private T[] _buffer;
    private int _count;

    public PooledArrayBuilder(int initialCapacity)
    {
        _buffer = initialCapacity > 0 ? ArrayPool<T>.Shared.Rent(initialCapacity) : Array.Empty<T>();
        _count = 0;
    }

    public int Count => _count;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(T item)
    {
        var buffer = _buffer;
        var count = _count;
        if ((uint)count < (uint)buffer.Length)
        {
            buffer[count] = item;
            _count = count + 1;
            return;
        }

        AddWithGrow(item, count);
    }

    public T[] ToArrayAndReturn()
    {
        var count = _count;
        if (count == 0)
        {
            Return();
            return Array.Empty<T>();
        }

        var buffer = _buffer;
        var result = new T[count];
        Array.Copy(buffer, result, count);
        Return(buffer, count);
        _buffer = Array.Empty<T>();
        _count = 0;
        return result;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void AddWithGrow(T item, int count)
    {
        Grow(count + 1);
        _buffer[count] = item;
        _count = count + 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Grow(int requiredCapacity)
    {
        var buffer = _buffer;
        var count = _count;
        var nextSize = buffer.Length == 0 ? 16 : buffer.Length * 2;
        if (nextSize < requiredCapacity)
        {
            nextSize = requiredCapacity;
        }

        var newBuffer = ArrayPool<T>.Shared.Rent(nextSize);
        if (count > 0)
        {
            Array.Copy(buffer, newBuffer, count);
        }

        Return(buffer, count);
        _buffer = newBuffer;
    }

    private void Return()
    {
        Return(_buffer, _count);
        _buffer = Array.Empty<T>();
        _count = 0;
    }

    private static void Return(T[] buffer, int count)
    {
        if (buffer.Length == 0)
        {
            return;
        }

        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            Array.Clear(buffer, 0, Math.Min(count, buffer.Length));
        }
        ArrayPool<T>.Shared.Return(buffer);
    }

    public void Dispose() => Return();
}
