using System.Runtime.CompilerServices;

namespace Meziantou.Framework.JavaScriptEngine;

// The access to the storage of the named properties for the inline caches of the interpreter.
public partial class JsObject
{
    /// <summary>Gets the storage of the named properties: a shape, a property table, or null (no property yet).</summary>
    internal PropertyMap? Map
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _map;
    }

    /// <summary>Gets the values of the properties of the shape.</summary>
    internal JsValue[]? Slots
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _slots;
    }

    /// <summary>
    /// Gets the storage of the object for an inline cache whose key is <paramref name="key"/>: its shape (the static empty
    /// shape when it has no properties yet), or its property table; null when the property is exotic for the object.
    /// </summary>
    internal PropertyMap? GetMapForCache(in PropertyKey key)
    {
        if (key.IsArrayIndex)
            return null;

        if (IsExotic)
        {
            // Only the arrays (except length), the String objects and the arguments objects (except the indices) have
            // ordinary named properties
            var ordinary = this switch
            {
                JsArray => !key.Equals(LengthKeyForCache),
                JsStringObject or Interpreter.JsArgumentsObject => true,
                _ => false,
            };

            if (!ordinary)
                return null;
        }

        if (_map is null)
        {
            _map = GetEmptyShape();
        }

        return _map;
    }

    private static readonly PropertyKey LengthKeyForCache = new("length");

    /// <summary>Finds an own named property for an inline cache: its position in the slots or in the table, and its attributes (a lazy value is evaluated).</summary>
    internal bool TryFindOwnForCache(in PropertyKey key, out int position, out PropertyFlags flags)
    {
        if (_map is Shape shape)
        {
            position = shape.Find(key);
            flags = position >= 0 ? shape.GetFlags(position) : default;
            return position >= 0;
        }

        if (_map is PropertyTable table)
        {
            position = table.Find(key);
            if (position >= 0)
            {
                if ((table.GetFlags(position) & PropertyFlags.Lazy) != 0)
                {
                    GetDescriptor(table, position);
                }

                flags = table.GetFlags(position);
                return true;
            }
        }

        position = -1;
        flags = default;
        return false;
    }

    /// <summary>
    /// Gets the shape of an ordinary object whose own properties are all in its shape (no element), or null: the keys of
    /// the shape are then the own property keys of the object, in order.
    /// </summary>
    internal Shape? GetOrdinaryShapeForEnumeration()
    {
        if (IsExotic || _map is not Shape shape)
            return null;

        if (HasAnyDenseElement())
            return null;

        return shape;
    }

    /// <summary>Gets whether an ordinary object may have an enumerable own property whose key is a string (no list is built).</summary>
    internal bool MayHaveEnumerableStringKeys()
    {
        if (HasAnyDenseElement())
            return true;

        if (_map is Shape shape)
        {
            for (var i = 0; i < shape.Count; i++)
            {
                if ((shape.GetFlags(i) & PropertyFlags.Enumerable) != 0 && shape.GetKey(i).IsString)
                    return true;
            }
        }
        else if (_map is PropertyTable table)
        {
            for (var i = 0; i < table.Capacity; i++)
            {
                if (!table.IsDeleted(i) && (table.GetFlags(i) & PropertyFlags.Enumerable) != 0 && table.GetKey(i).IsString)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Moves an object that gets many built-in methods (a prototype, a constructor, a namespace) to a property table of its
    /// own: building a chain of shapes for each realm of each engine would cost more than it saves (the inline caches
    /// use the positions in the table).
    /// </summary>
    internal void UsePropertyTableForBuiltIns()
    {
        if (_map is Shape { Count: >= 4 })
        {
            GetPropertyTable();
        }
    }

    /// <summary>
    /// Moves the object to a property table that has room for <paramref name="capacity"/> properties (e.g. a global object
    /// or a prototype that gets many properties from the host): no shape is created for its properties.
    /// </summary>
    internal void UsePropertyTable(int capacity)
    {
        GetPropertyTable(capacity).EnsureCapacity(capacity);
    }

    /// <summary>Allocates the slots of an object that has no property yet and will get <paramref name="count"/> properties (e.g. the length and the name of a built-in function).</summary>
    internal void ReserveSlots(int count)
    {
        if (_slots is null && _map is Shape { Count: 0 })
        {
            _slots = new JsValue[count];
        }
    }

    /// <summary>Marks the object as the holder of a property whose position an inline cache remembers.</summary>
    internal void MarkInlineCacheHolder()
    {
        if ((Flags & ObjectFlags.InlineCacheHolder) == 0)
        {
            Flags |= ObjectFlags.InlineCacheHolder;
        }
    }

    /// <summary>Marks the object as a prototype that an inline cache depends on (the absence of a property, its prototype).</summary>
    internal void MarkInlineCachePrototype()
    {
        if ((Flags & ObjectFlags.InlineCachePrototype) == 0)
        {
            Flags |= ObjectFlags.InlineCachePrototype;
        }
    }

    /// <summary>Gets the value at a position found by <see cref="TryFindOwnForCache"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal JsValue GetValueForCache(int position, bool inTable) => inTable ? ((PropertyTable)_map!).GetValue(position) : _slots![position];

    /// <summary>Sets the value at a position found by <see cref="TryFindOwnForCache"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void SetValueForCache(int position, bool inTable, JsValue value)
    {
        if (inTable)
        {
            ((PropertyTable)_map!).GetValue(position) = value;
        }
        else
        {
            _slots![position] = value;
        }
    }

    /// <summary>Adds a property with the transition remembered by an inline cache: the current shape is the parent of <paramref name="next"/>.</summary>
    internal void AddPropertyForCache(Shape next, JsValue value)
    {
        var slot = next.Count - 1;
        var slots = _slots;
        if (slots is null)
        {
            _slots = slots = new JsValue[next.GetSlotsLength(0)];
        }
        else if (slot >= slots.Length)
        {
            Array.Resize(ref _slots, next.GetSlotsLength(slots.Length));
            slots = _slots;
        }

        slots[slot] = value;
        _map = next;
        OnPropertyAdded(positionsChanged: false);
    }
}
