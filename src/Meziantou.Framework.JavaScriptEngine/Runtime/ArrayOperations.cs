using System.Runtime.CompilerServices;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The abstract operations on arrays and array-like objects (https://tc39.es/ecma262/#sec-array-objects), with the fast paths of the dense arrays.</summary>
internal static class ArrayOperations
{
    /// <summary>2^53-1, the maximum length of an array-like object.</summary>
    public const double MaxSafeInteger = 9007199254740991;

    public static readonly PropertyKey LengthKey = new("length");
    public static readonly PropertyKey ConstructorKey = new("constructor");

    /// <summary>The property key of an index (ToString(index)).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PropertyKey Key(long index) => (ulong)index < uint.MaxValue ? new PropertyKey((uint)index) : PropertyKey.FromInteger(index);

    /// <summary>ArrayCreate(length [, proto]): a RangeError is thrown when the length is greater than 2^32-1.</summary>
    public static JsArray ArrayCreate(double length, JsObject? prototype = null)
    {
        if (length > uint.MaxValue)
        {
            ThrowHelper.ThrowRangeError("Invalid array length");
        }

        prototype ??= JsEngine.Current!.CurrentRealm!.Intrinsics.ArrayPrototype;
        return new JsArray(prototype, (uint)length);
    }

    /// <summary>ArraySpeciesCreate(originalArray, length)</summary>
    public static JsObject ArraySpeciesCreate(JsObject originalArray, double length)
    {
        if (!JsOperations.IsArray(originalArray))
            return ArrayCreate(length);

        var constructor = originalArray.Get(ConstructorKey);
        if (constructor.Cell is JsObject { IsConstructor: true } constructorObject)
        {
            var thisRealm = JsEngine.Current!.CurrentRealm!;
            var realmC = ObjectOperations.GetFunctionRealm(constructorObject);
            if (!ReferenceEquals(thisRealm, realmC) && ReferenceEquals(constructorObject, realmC.Intrinsics.ArrayConstructor))
            {
                constructor = JsValue.Undefined;
            }
        }

        if (constructor.Cell is JsObject obj)
        {
            constructor = obj.Get(new PropertyKey(WellKnownSymbols.Species));
            if (constructor.IsNull)
            {
                constructor = JsValue.Undefined;
            }
        }

        if (constructor.IsUndefined)
            return ArrayCreate(length);

        if (constructor.Cell is not JsObject { IsConstructor: true } species)
            return ThrowHelper.ThrowTypeError<JsObject>("object.constructor[Symbol.species] is not a constructor");

        return JsOperations.Construct(species, [length]).AsObject();
    }

    /// <summary>
    /// Reads an element of the dense storage: true when the object has an own data property at the index (its [[Get]] and
    /// [[HasProperty]] would not run any code). Only for the arrays and the ordinary objects.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryGetDenseElement(JsObject obj, long index, out JsValue value)
    {
        if ((ulong)index <= uint.MaxValue && (obj is JsArray || !obj.IsExotic))
            return obj.TryGetDenseElement((uint)index, out value);

        value = default;
        return false;
    }

    /// <summary>HasProperty(O, index), then Get(O, index) when the property exists.</summary>
    public static bool TryGetElement(JsObject obj, long index, out JsValue value)
    {
        JsEngine.CountCurrentWork(1);
        if (TryGetDenseElement(obj, index, out value))
            return true;

        var key = Key(index);
        if (!obj.HasProperty(key))
            return false;

        value = obj.Get(key);
        return true;
    }

    /// <summary>Get(O, index)</summary>
    public static JsValue GetElement(JsObject obj, long index)
    {
        JsEngine.CountCurrentWork(1);
        if (TryGetDenseElement(obj, index, out var value))
            return value;

        return obj.Get(Key(index));
    }

    /// <summary>HasProperty(O, index)</summary>
    public static bool HasElement(JsObject obj, long index)
    {
        JsEngine.CountCurrentWork(1);
        if (TryGetDenseElement(obj, index, out _))
            return true;

        return obj.HasProperty(Key(index));
    }

    /// <summary>Set(O, index, value, true)</summary>
    public static void SetElement(JsObject obj, long index, JsValue value)
    {
        JsEngine.CountCurrentWork(1);
        obj.Set(Key(index), value, throwOnFailure: true);
    }

    /// <summary>DeletePropertyOrThrow(O, index)</summary>
    public static void DeleteElement(JsObject obj, long index)
    {
        JsEngine.CountCurrentWork(1);
        obj.DeletePropertyOrThrow(Key(index));
    }

    /// <summary>CreateDataPropertyOrThrow(O, index, value)</summary>
    public static void CreateElement(JsObject obj, long index, JsValue value)
    {
        JsEngine.CountCurrentWork(1);
        // An extensible array whose indices are all in the dense elements: the element is an ordinary data property
        if (obj is JsArray array && array.TryCreateDenseElement(index, value))
            return;

        obj.CreateDataPropertyOrThrow(Key(index), value);
    }

    /// <summary>Set(O, "length", length, true)</summary>
    public static void SetLength(JsObject obj, double length) => obj.Set(LengthKey, length, throwOnFailure: true);

    /// <summary>
    /// Gets whether the prototypes of an object have no property whose key is an array index (so that an absent element is
    /// undefined, and an assignment of an absent element creates an own property): only arrays and ordinary objects.
    /// </summary>
    public static bool PrototypesHaveNoElements(JsObject obj)
    {
        for (var prototype = obj.PrototypeSlot; prototype is not null; prototype = prototype.PrototypeSlot)
        {
            if (prototype is not JsArray && prototype.IsExotic)
                return false;

            if (!prototype.HasOnlyDenseElements)
                return false;

            if (prototype.HasAnyDenseElement())
                return false;
        }

        return true;
    }

    /// <summary>
    /// The first index in [k, length) of an element that may exist, when reading the absent elements of the object runs no
    /// code (an array or an ordinary object whose prototypes have no elements), so that a loop can skip the holes; k
    /// otherwise. The result is computed when it is used: the code run by the loop may add elements.
    /// </summary>
    public static long SkipHoles(JsObject obj, long k, long length)
    {
        if (k >= length || TryGetDenseElement(obj, k, out _))
            return k;

        if ((obj is not JsArray && obj.IsExotic) || !PrototypesHaveNoElements(obj))
            return k;

        // The indices greater than 2^32-2 are not array indices: they are not tracked
        var next = Math.Min(length, uint.MaxValue);
        var nextDense = obj.NextDenseElement(k, next);
        if (nextDense >= 0)
        {
            next = nextDense;
        }

        if (!obj.HasOnlyDenseElements)
        {
            foreach (var key in obj.GetIndexedTableKeys())
            {
                if (key.ArrayIndex >= k && key.ArrayIndex < next)
                {
                    next = key.ArrayIndex;
                }
            }
        }

        return Math.Max(next, k);
    }

    /// <summary>The last index in [0, k] of an element that may exist (see <see cref="SkipHoles"/>), -1 when there is none; k when the holes are observable.</summary>
    public static long SkipHolesBackward(JsObject obj, long k)
    {
        if (k < 0 || TryGetDenseElement(obj, k, out _))
            return k;

        if (k >= uint.MaxValue || (obj is not JsArray && obj.IsExotic) || !PrototypesHaveNoElements(obj))
            return k;

        var previous = obj.PreviousDenseElement(k);

        if (!obj.HasOnlyDenseElements)
        {
            foreach (var key in obj.GetIndexedTableKeys())
            {
                if (key.ArrayIndex <= k && key.ArrayIndex > previous)
                {
                    previous = key.ArrayIndex;
                }
            }
        }

        return previous;
    }

    /// <summary>
    /// Gets whether the elements [0, length) of an array are all in its dense storage, so that reading, writing and moving
    /// them is not observable. The array must be extensible and its prototypes must not have elements.
    /// </summary>
    public static bool IsPackedArray(JsObject obj, long length, out JsArray array)
    {
        array = null!;
        if (obj is not JsArray candidate || !candidate.HasOnlyDenseElements || !candidate.IsLengthWritable || !candidate.IsExtensibleSlot || candidate.Length != length)
            return false;

        if (length > 0 && !candidate.IsDensePacked(length))
            return false;

        if (!PrototypesHaveNoElements(candidate))
            return false;

        array = candidate;
        return true;
    }

    /// <summary>IsConcatSpreadable(O)</summary>
    public static bool IsConcatSpreadable(JsValue value)
    {
        if (value.Cell is not JsObject obj)
            return false;

        var spreadable = obj.Get(new PropertyKey(WellKnownSymbols.IsConcatSpreadable));
        if (!spreadable.IsUndefined)
            return JsOperations.ToBoolean(spreadable);

        return JsOperations.IsArray(value);
    }

    /// <summary>
    /// SortIndexedProperties(obj, len, SortCompare, holes) with CompareArrayElements: reads the elements (skipping the
    /// holes or not), and sorts them with a stable sort; the undefined values are last.
    /// </summary>
    public static List<JsValue> SortIndexedProperties(JsObject obj, long length, JsObject? comparer, bool skipHoles)
    {
        var items = new List<JsValue>();
        for (long k = 0; k < length; k++)
        {
            if (skipHoles)
            {
                k = SkipHoles(obj, k, length);
                if (k >= length)
                    break;

                if (TryGetElement(obj, k, out var value))
                {
                    items.Add(value);
                }
            }
            else
            {
                items.Add(GetElement(obj, k));
            }
        }

        // The undefined values are equal and greater than the other values: CompareArrayElements does not call the comparer for them
        var values = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(items);
        var count = 0;
        foreach (var value in values)
        {
            if (!value.IsUndefined)
            {
                values[count++] = value;
            }
        }

        values[count..].Clear();
        SortValues(values[..count], comparer);
        return items;
    }

    /// <summary>Sorts values (none is undefined) with CompareArrayElements, with a stable sort.</summary>
    private static void SortValues(Span<JsValue> values, JsObject? comparer)
    {
        if (values.Length < 2)
            return;

        if (comparer is not null)
        {
            MergeSort(values, (x, y) =>
            {
                var v = JsOperations.ToNumber(comparer.Call(JsValue.Undefined, [x, y]));
                return v < 0 ? -1 : v > 0 ? 1 : 0;
            });

            return;
        }

        // ToString of a primitive value other than a symbol runs no code: the strings can be computed once
        var allPrimitives = true;
        foreach (var value in values)
        {
            if (value.IsObject || value.IsSymbol)
            {
                allPrimitives = false;
                break;
            }
        }

        if (allPrimitives)
        {
            var pairs = new (string Key, JsValue Value)[values.Length];
            for (var i = 0; i < values.Length; i++)
            {
                pairs[i] = (JsOperations.ToString(values[i]), values[i]);
            }

            MergeSort(pairs.AsSpan(), (x, y) => string.CompareOrdinal(x.Key, y.Key));
            for (var i = 0; i < values.Length; i++)
            {
                values[i] = pairs[i].Value;
            }

            return;
        }

        MergeSort(values, (x, y) => string.CompareOrdinal(JsOperations.ToString(x), JsOperations.ToString(y)));
    }

    /// <summary>A stable merge sort; the comparison may throw (the sort stops) or be inconsistent (the order is then unspecified).</summary>
    public static void MergeSort<T>(Span<T> items, Comparison<T> comparison)
    {
        if (items.Length < 2)
            return;

        var buffer = new T[(items.Length + 1) / 2];
        MergeSort(items, buffer, comparison);
    }

    private static void MergeSort<T>(Span<T> items, T[] buffer, Comparison<T> comparison)
    {
        if (items.Length <= 8)
        {
            // Insertion sort (stable)
            for (var i = 1; i < items.Length; i++)
            {
                var item = items[i];
                var j = i;
                while (j > 0 && comparison(items[j - 1], item) > 0)
                {
                    items[j] = items[j - 1];
                    j--;
                }

                items[j] = item;
            }

            return;
        }

        var middle = items.Length / 2;
        var left = items[..middle];
        var right = items[middle..];
        MergeSort(left, buffer, comparison);
        MergeSort(right, buffer, comparison);
        if (comparison(left[^1], right[0]) <= 0)
            return;

        left.CopyTo(buffer);
        var leftItems = buffer.AsSpan(0, middle);
        int l = 0, r = 0, k = 0;
        while (l < leftItems.Length && r < right.Length)
        {
            if (comparison(right[r], leftItems[l]) < 0)
            {
                items[k++] = right[r++];
            }
            else
            {
                items[k++] = leftItems[l++];
            }
        }

        while (l < leftItems.Length)
        {
            items[k++] = leftItems[l++];
        }
    }
}
