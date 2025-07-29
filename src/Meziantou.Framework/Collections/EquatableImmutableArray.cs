/*
 * https://github.com/Sergio0694/ComputeSharp/blob/6f7290433cd30caf925397cda7ecc9ef66862e46/src/ComputeSharp.SourceGeneration/Helpers/EquatableArray%7BT%7D.cs
 * 
 * MIT License
 * 
 * Copyright (c) 2024 Sergio Pedri
 * 
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 * 
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 * 
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 */

using System.Collections;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meziantou.Framework;

/// <summary>
/// Extensions for <see cref="EquatableImmutableArray{T}"/>.
/// </summary>
public static class EquatableImmutableArray
{
    /// <summary>
    /// Creates an <see cref="EquatableImmutableArray{T}"/> instance from a given <see cref="ImmutableArray{T}"/>.
    /// </summary>
    /// <typeparam name="T">The type of items in the input array.</typeparam>
    /// <param name="array">The input <see cref="ImmutableArray{T}"/> instance.</param>
    /// <returns>An <see cref="EquatableImmutableArray{T}"/> instance from a given <see cref="ImmutableArray{T}"/>.</returns>
    public static EquatableImmutableArray<T> AsEquatableArray<T>(this ImmutableArray<T> array)
        where T : IEquatable<T>
    {
        return new(array);
    }
}

/// <summary>
/// An immutable, equatable array. This is equivalent to <see cref="ImmutableArray{T}"/> but with value equality support.
/// </summary>
/// <typeparam name="T">The type of values in the array.</typeparam>
/// <param name="array">The input <see cref="ImmutableArray{T}"/> to wrap.</param>
public readonly struct EquatableImmutableArray<T>(ImmutableArray<T> array) : IEquatable<EquatableImmutableArray<T>>, IEnumerable<T>
    where T : IEquatable<T>
{
    /// <summary>
    /// The underlying <typeparamref name="T"/> array.
    /// </summary>
    [SuppressMessage("Design", "MA0143:Primary constructor parameters should be readonly")]
    private readonly T[]? _array = Unsafe.As<ImmutableArray<T>, T[]?>(ref array);

    /// <summary>
    /// Gets a reference to an item at a specified position within the array.
    /// </summary>
    /// <param name="index">The index of the item to retrieve a reference to.</param>
    /// <returns>A reference to an item at a specified position within the array.</returns>
    public ref readonly T this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ref AsImmutableArray().ItemRef(index);
    }

    /// <summary>
    /// Gets a value indicating whether the current array is empty.
    /// </summary>
    public bool IsEmpty
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => AsImmutableArray().IsEmpty;
    }

    /// <summary>
    /// Gets a value indicating whether the current array is default or empty.
    /// </summary>
    public bool IsDefaultOrEmpty
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => AsImmutableArray().IsDefaultOrEmpty;
    }

    /// <summary>
    /// Gets the length of the current array.
    /// </summary>
    public int Length
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => AsImmutableArray().Length;
    }

    /// <inheritdoc/>
    public bool Equals(EquatableImmutableArray<T> array)
    {
        return AsSpan().SequenceEqual(array.AsSpan());
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        return obj is EquatableImmutableArray<T> array && Equals(array);
    }

    /// <inheritdoc/>
    public override unsafe int GetHashCode()
    {
        if (_array is not T[] array)
        {
            return 0;
        }

        HashCode hashCode = default;

        if (typeof(T) == typeof(byte))
        {
            ReadOnlySpan<T> span = array;
            ref var r0 = ref MemoryMarshal.GetReference(span);
            ref var r1 = ref Unsafe.As<T, byte>(ref r0);

            fixed (byte* p = &r1)
            {
                ReadOnlySpan<byte> bytes = new(p, span.Length);
                hashCode.AddBytes(bytes);
            }
        }
        else
        {
            foreach (var item in array)
            {
                hashCode.Add(item);
            }
        }

        return hashCode.ToHashCode();
    }

    /// <summary>
    /// Gets an <see cref="ImmutableArray{T}"/> instance from the current <see cref="EquatableImmutableArray{T}"/>.
    /// </summary>
    /// <returns>The <see cref="ImmutableArray{T}"/> from the current <see cref="EquatableImmutableArray{T}"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ImmutableArray<T> AsImmutableArray() => Unsafe.As<T[]?, ImmutableArray<T>>(ref Unsafe.AsRef(in _array));

    /// <summary>
    /// Returns a <see cref="ReadOnlySpan{T}"/> wrapping the current items.
    /// </summary>
    /// <returns>A <see cref="ReadOnlySpan{T}"/> wrapping the current items.</returns>
    public ReadOnlySpan<T> AsSpan() => AsImmutableArray().AsSpan();

    /// <summary>
    /// Copies the contents of this <see cref="EquatableImmutableArray{T}"/> instance. to a mutable array.
    /// </summary>
    /// <returns>The newly instantiated array.</returns>
    public T[] ToArray() => [.. AsImmutableArray()];

    /// <summary>
    /// Gets an <see cref="ImmutableArray{T}.Enumerator"/> value to traverse items in the current array.
    /// </summary>
    /// <returns>An <see cref="ImmutableArray{T}.Enumerator"/> value to traverse items in the current array.</returns>
    public ImmutableArray<T>.Enumerator GetEnumerator() => AsImmutableArray().GetEnumerator();

    /// <inheritdoc/>
    IEnumerator<T> IEnumerable<T>.GetEnumerator() => ((IEnumerable<T>)AsImmutableArray()).GetEnumerator();

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)AsImmutableArray()).GetEnumerator();

    /// <summary>
    /// Implicitly converts an <see cref="ImmutableArray{T}"/> to <see cref="EquatableImmutableArray{T}"/>.
    /// </summary>
    /// <returns>An <see cref="EquatableImmutableArray{T}"/> instance from a given <see cref="ImmutableArray{T}"/>.</returns>
    public static implicit operator EquatableImmutableArray<T>(ImmutableArray<T> array) => array.AsEquatableArray();

    /// <summary>
    /// Implicitly converts an <see cref="EquatableImmutableArray{T}"/> to <see cref="ImmutableArray{T}"/>.
    /// </summary>
    /// <returns>An <see cref="ImmutableArray{T}"/> instance from a given <see cref="EquatableImmutableArray{T}"/>.</returns>
    public static implicit operator ImmutableArray<T>(EquatableImmutableArray<T> array) => array.AsImmutableArray();

    /// <summary>
    /// Checks whether two <see cref="EquatableImmutableArray{T}"/> values are the same.
    /// </summary>
    /// <param name="left">The first <see cref="EquatableImmutableArray{T}"/> value.</param>
    /// <param name="right">The second <see cref="EquatableImmutableArray{T}"/> value.</param>
    /// <returns>Whether <paramref name="left"/> and <paramref name="right"/> are equal.</returns>
    public static bool operator ==(EquatableImmutableArray<T> left, EquatableImmutableArray<T> right) => left.Equals(right);

    /// <summary>
    /// Checks whether two <see cref="EquatableImmutableArray{T}"/> values are not the same.
    /// </summary>
    /// <param name="left">The first <see cref="EquatableImmutableArray{T}"/> value.</param>
    /// <param name="right">The second <see cref="EquatableImmutableArray{T}"/> value.</param>
    /// <returns>Whether <paramref name="left"/> and <paramref name="right"/> are not equal.</returns>
    public static bool operator !=(EquatableImmutableArray<T> left, EquatableImmutableArray<T> right) => !left.Equals(right);
}