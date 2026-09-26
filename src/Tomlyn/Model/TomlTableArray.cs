// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;

namespace Tomlyn.Model
{
    /// <summary>
    /// Runtime representation of a TOML table array
    /// </summary>
    public sealed class TomlTableArray : TomlObject, IList<TomlTable>
    {
        private readonly List<TomlTable> _items;

        /// <summary>
        /// Initializes a new empty table array.
        /// </summary>
        public TomlTableArray() : base(ObjectKind.TableArray)
        {
            _items = new List<TomlTable>();
        }

        /// <summary>
        /// Initializes a new table array with the specified capacity.
        /// </summary>
        /// <param name="capacity">The initial capacity.</param>
        internal TomlTableArray(int capacity) : base(ObjectKind.TableArray)
        {
            _items = new List<TomlTable>(capacity);
        }


        /// <summary>
        /// Returns an enumerator that iterates through the table array.
        /// </summary>
        /// <returns>An enumerator for the table array.</returns>
        public List<TomlTable>.Enumerator GetEnumerator()
        {
            return _items.GetEnumerator();
        }

        IEnumerator<TomlTable> IEnumerable<TomlTable>.GetEnumerator()
        {
            return GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <inheritdoc />
        public void Add(TomlTable item)
        {
            ArgumentNullException.ThrowIfNull(item);
            _items.Add(item);
        }

        /// <inheritdoc />
        public void Clear()
        {
            _items.Clear();
        }

        /// <inheritdoc />
        public bool Contains(TomlTable item)
        {
            ArgumentNullException.ThrowIfNull(item);
            return _items.Contains(item);
        }

        /// <inheritdoc />
        public void CopyTo(TomlTable[] array, int arrayIndex)
        {
            _items.CopyTo(array, arrayIndex);
        }

        /// <inheritdoc />
        public bool Remove(TomlTable item)
        {
            ArgumentNullException.ThrowIfNull(item);
            return _items.Remove(item);
        }

        /// <inheritdoc />
        public int Count => _items.Count;
        /// <inheritdoc />
        public bool IsReadOnly => false;

        /// <inheritdoc />
        public int IndexOf(TomlTable item)
        {
            ArgumentNullException.ThrowIfNull(item);
            return _items.IndexOf(item);
        }

        /// <inheritdoc />
        public void Insert(int index, TomlTable item)
        {
            ArgumentNullException.ThrowIfNull(item);
            _items.Insert(index, item);
        }

        /// <inheritdoc />
        public void RemoveAt(int index)
        {
            _items.RemoveAt(index);
        }

        /// <inheritdoc />
        public TomlTable this[int index]
        {
            get => _items[index];
            set => _items[index] = value ?? throw new ArgumentNullException(nameof(value));
        }
    }
}