using System.Runtime.CompilerServices;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// The layout of the named properties of an object (a hidden class): the keys and the attributes, in insertion order. The
/// values are in the slots of the object, at the position of their key. The objects that get the same properties in the
/// same order share their shape, so the inline caches of the interpreter can remember where a property is by remembering
/// the shape (<see cref="InlineCache"/>).
/// </summary>
/// <remarks>
/// <para>
/// A shape is immutable: adding a property moves the object to a child shape (a transition, cached in the parent). The
/// objects whose properties are deleted or reconfigured, or that have many properties, use a <see cref="PropertyTable"/>
/// of their own instead (the dictionary mode).
/// </para>
/// <para>
/// The empty shapes (<see cref="EmptyOrdinary"/>...) are static: they have no transitions. The shapes with properties are
/// created per engine (<see cref="ShapeRoots"/>), so the transition trees are not shared between threads and are freed
/// with their engine.
/// </para>
/// </remarks>
internal sealed class Shape : PropertyMap
{
    /// <summary>The number of properties above which an object uses a property table (the shapes of objects used as maps would never be shared).</summary>
    public const int MaxPropertyCount = 64;

    // The keys are compared linearly up to this count, then with the index of the table
    private const int LinearSearchMaxCount = 8;

    /// <summary>The shape of the ordinary objects without named properties.</summary>
    public static readonly Shape EmptyOrdinary = new(ShapeKind.Ordinary);

    /// <summary>The shape of the arrays without named properties (other than length).</summary>
    public static readonly Shape EmptyArray = new(ShapeKind.Array);

    /// <summary>The shape of the objects whose array indices are exotic (String objects, arguments objects) without named properties.</summary>
    public static readonly Shape EmptyIndexedExotic = new(ShapeKind.IndexedExotic);

    // The keys and attributes of the properties in insertion order. A child shape shares the table of its parent when it is
    // the first to extend it (the table then has more entries than this shape: only the first Count entries belong to it).
    private readonly ShapeTable? _table;

    // The transitions: the first one inline (most shapes have one child), the others in a dictionary
    private Shape? _transition;
    private Dictionary<TransitionKey, Shape>? _transitions;

    private Shape(ShapeKind kind)
    {
        Kind = kind;
    }

    private Shape(Shape parent, ShapeTable table)
    {
        Kind = parent.Kind;
        Parent = parent;
        _table = table;
        Count = parent.Count + 1;
    }

    /// <summary>Gets the kind of the objects that use the shape (the root of the tree).</summary>
    public ShapeKind Kind { get; }

    /// <summary>Gets the shape without the last property, if any.</summary>
    public Shape? Parent { get; }

    /// <summary>Gets the number of properties (and the number of slots used by the objects).</summary>
    public int Count { get; }

    /// <summary>Gets whether the shape is one of the static empty shapes (it has no transitions).</summary>
    public bool IsStaticRoot => ReferenceEquals(this, GetStaticRoot(Kind));

    /// <summary>Gets the key of a property.</summary>
    public PropertyKey GetKey(int slot) => _table!.Keys[slot];

    /// <summary>Gets the attributes of a property.</summary>
    public PropertyFlags GetFlags(int slot) => _table!.Flags[slot];

    /// <summary>Gets the slot of a property, or -1.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Find(in PropertyKey key)
    {
        var table = _table;
        if (table is null)
            return -1;

        if (table.Index is { } index)
            return index.TryGetValue(key, out var slot) && slot < Count ? slot : -1;

        var keys = table.Keys;
        var count = Count;
        for (var i = 0; i < count; i++)
        {
            if (keys[i].Equals(key))
                return i;
        }

        return -1;
    }

    /// <summary>Gets the shape with one more property (the key must not be in the shape), or null when the object must use a property table.</summary>
    public Shape? AddProperty(in PropertyKey key, PropertyFlags flags)
    {
        if (Count >= MaxPropertyCount)
            return null;

        if (IsStaticRoot)
        {
            // The static empty shapes have no transitions: the shapes with properties belong to the engine
            var roots = JsEngine.Current?.ShapeRoots;
            if (roots is null)
                return null;

            return roots.GetRoot(Kind).AddProperty(key, flags);
        }

        if (_transition is { } transition && transition.Count == Count + 1 && transition.GetFlags(Count) == flags && transition.GetKey(Count).Equals(key))
            return transition;

        var transitionKey = new TransitionKey(key, flags);
        if (_transitions is not null && _transitions.TryGetValue(transitionKey, out var existing))
            return existing;

        var child = new Shape(this, ShapeTable.Append(_table, Count, key, flags));
        if (_transition is null)
        {
            _transition = child;
        }
        else
        {
            (_transitions ??= []).Add(transitionKey, child);
        }

        return child;
    }

    /// <summary>Creates the empty shape of an engine for a kind of objects (the parent of its shapes with properties).</summary>
    internal static Shape CreateRoot(ShapeKind kind) => new(kind);

    private static Shape GetStaticRoot(ShapeKind kind)
    {
        return kind switch
        {
            ShapeKind.Array => EmptyArray,
            ShapeKind.IndexedExotic => EmptyIndexedExotic,
            _ => EmptyOrdinary,
        };
    }

    private readonly record struct TransitionKey(PropertyKey Key, PropertyFlags Flags);

    /// <summary>The keys and attributes of a branch of shapes (shared by a shape and its first descendants).</summary>
    private sealed class ShapeTable
    {
        public PropertyKey[] Keys = [];
        public PropertyFlags[] Flags = [];
        public int Count;
        public Dictionary<PropertyKey, int>? Index;

        /// <summary>Adds a property to the table of a shape whose count is <paramref name="count"/>: in place when the shape is the last owner of the table, in a copy otherwise.</summary>
        public static ShapeTable Append(ShapeTable? table, int count, in PropertyKey key, PropertyFlags flags)
        {
            if (table is null || table.Count != count)
            {
                // A new branch: copy the entries of the shape
                var copy = new ShapeTable
                {
                    Keys = new PropertyKey[Math.Max(4, count * 2)],
                    Flags = new PropertyFlags[Math.Max(4, count * 2)],
                };

                if (table is not null)
                {
                    Array.Copy(table.Keys, copy.Keys, count);
                    Array.Copy(table.Flags, copy.Flags, count);
                }

                copy.Count = count;
                if (count > LinearSearchMaxCount)
                {
                    copy.Index = new Dictionary<PropertyKey, int>(count * 2);
                    for (var i = 0; i < count; i++)
                    {
                        copy.Index[copy.Keys[i]] = i;
                    }
                }

                table = copy;
            }

            if (table.Count == table.Keys.Length)
            {
                Array.Resize(ref table.Keys, table.Count * 2);
                Array.Resize(ref table.Flags, table.Count * 2);
            }

            table.Keys[table.Count] = key;
            table.Flags[table.Count] = flags;
            table.Count++;
            if (table.Index is not null)
            {
                table.Index[key] = table.Count - 1;
            }
            else if (table.Count > LinearSearchMaxCount)
            {
                table.Index = new Dictionary<PropertyKey, int>(table.Count * 2);
                for (var i = 0; i < table.Count; i++)
                {
                    table.Index[table.Keys[i]] = i;
                }
            }

            return table;
        }
    }
}

/// <summary>The kinds of objects that can share shapes: the objects whose named properties are all ordinary.</summary>
internal enum ShapeKind : byte
{
    /// <summary>The ordinary objects (and the exotic objects whose named properties are ordinary, e.g. functions).</summary>
    Ordinary,

    /// <summary>The arrays: length is exotic.</summary>
    Array,

    /// <summary>The String objects and the arguments objects: the array indices are exotic.</summary>
    IndexedExotic,
}

/// <summary>The empty shapes of an engine: the roots of its transition trees.</summary>
internal sealed class ShapeRoots
{
    private readonly Shape _ordinary = Shape.CreateRoot(ShapeKind.Ordinary);
    private readonly Shape _array = Shape.CreateRoot(ShapeKind.Array);
    private readonly Shape _indexedExotic = Shape.CreateRoot(ShapeKind.IndexedExotic);

    public Shape GetRoot(ShapeKind kind)
    {
        return kind switch
        {
            ShapeKind.Array => _array,
            ShapeKind.IndexedExotic => _indexedExotic,
            _ => _ordinary,
        };
    }
}

/// <summary>The storage of the named properties of an object: a shared <see cref="Shape"/>, or a <see cref="PropertyTable"/> of its own (dictionary mode).</summary>
internal abstract class PropertyMap
{
}
