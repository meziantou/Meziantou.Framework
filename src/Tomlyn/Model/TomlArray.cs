// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;

namespace Tomlyn.Model
{
    /// <summary>
    /// Runtime representation of a TOML array
    /// </summary>
    public sealed class TomlArray : TomlObject, IList<object?>
    {
        private readonly List<object?> _list;

        /// <summary>
        /// Initializes a new empty TOML array.
        /// </summary>
        public TomlArray() : base(ObjectKind.Array)
        {
            _list = new List<object?>();
        }

        /// <summary>
        /// Initializes a new TOML array with the specified capacity.
        /// </summary>
        /// <param name="capacity">The initial capacity.</param>
        public TomlArray(int capacity) : base(ObjectKind.Array)
        {
            _list = new List<object?>(capacity);
        }

        /// <summary>
        /// Returns an enumerator that iterates through the array.
        /// </summary>
        /// <returns>An enumerator for the array.</returns>
        public List<object?>.Enumerator GetEnumerator()
        {
            return _list.GetEnumerator();
        }

        IEnumerator<object?>  IEnumerable<object?>.GetEnumerator()
        {
            return _list.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <inheritdoc />
        public void Add(object? item)
        {
            _list.Add(item);
        }

        /// <inheritdoc />
        public void Clear()
        {
            _list.Clear();
        }

        /// <inheritdoc />
        public bool Contains(object? item)
        {
            return item != null && _list.Contains(item);
        }

        /// <inheritdoc />
        public void CopyTo(object?[] array, int arrayIndex)
        {
            _list.CopyTo(array, arrayIndex);
        }

        /// <inheritdoc />
        public bool Remove(object? item)
        {
            return item != null && _list.Remove(item);
        }

        /// <inheritdoc />
        public int Count => _list.Count;

        /// <inheritdoc />
        public bool IsReadOnly => false;

        /// <inheritdoc />
        public int IndexOf(object? item)
        {
            return _list.IndexOf(item);
        }

        /// <inheritdoc />
        public void Insert(int index, object? item)
        {
            _list.Insert(index, item);
        }

        /// <inheritdoc />
        public void RemoveAt(int index)
        {
            _list.RemoveAt(index);
        }

        /// <inheritdoc />
        public object? this[int index]
        {
            get => _list[index];
            set => _list[index] = value;
        }
    }
}