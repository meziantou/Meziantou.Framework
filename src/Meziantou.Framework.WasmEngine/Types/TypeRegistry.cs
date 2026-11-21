using System.Globalization;
using System.Text;

namespace Meziantou.Framework.WasmEngine;

/// <summary>A value type as decoded from a module: a concrete heap type is a type index of the module.</summary>
internal readonly record struct RawValType(ValueCode Code, bool Nullable, WasmHeapKind Heap, int TypeIndex)
{
    public static RawValType Of(ValueCode code) => new(code, Nullable: false, default, TypeIndex: -1);
}

/// <summary>A sub type as decoded from a module (https://webassembly.github.io/spec/core/syntax/types.html#recursive-types).</summary>
internal sealed class RawSubType
{
    public bool Final { get; init; } = true;

    /// <summary>The type index of the declared supertype, or -1.</summary>
    public int Super { get; init; } = -1;

    public CompositeKind Kind { get; init; }

    public RawValType[] Parameters { get; init; } = [];

    public RawValType[] Results { get; init; } = [];

    public (RawValType Type, bool Mutable)[] Fields { get; init; } = [];
}

/// <summary>
/// The canonical types of a runtime: the recursion groups are canonicalized (iso-recursive types,
/// https://webassembly.github.io/spec/core/valid/conventions.html#rolling-and-unrolling), so that two structurally
/// equal groups of different modules give the same <see cref="DefinedType"/> objects. It is thread-safe.
/// </summary>
/// <remarks>
/// The registry references the groups weakly: the groups of the modules that are no longer used (and of the modules that
/// were only validated) are collected. A group references the groups of its types, so the key of a group, which has the
/// ids of these groups, never refers to a collected group.
/// </remarks>
internal sealed class TypeRegistry
{
    private const int MinimumCleanupCount = 1024;

    private readonly Dictionary<string, WeakReference<DefinedType[]>> _groups = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();

    // The number of entries from which the entries of the collected groups are removed
    private int _cleanupCount = MinimumCleanupCount;
    private int _nextId;
    private DefinedType? _mutableI16Array;

    /// <summary>
    /// Canonicalizes a recursion group whose first type has the index <paramref name="groupStart"/> in its module.
    /// <paramref name="resolve"/> returns the canonical type of a type index of a previous group.
    /// </summary>
    public DefinedType[] Canonicalize(RawSubType[] group, int groupStart, Func<int, DefinedType> resolve)
    {
        var key = new StringBuilder();
        foreach (var type in group)
        {
            key.Append(type.Final ? 'F' : 'O');
            if (type.Super >= 0)
            {
                key.Append('<');
                AppendTypeIndex(key, type.Super, group.Length, groupStart, resolve);
            }

            key.Append(type.Kind switch { CompositeKind.Func => 'f', CompositeKind.Struct => 's', _ => 'a' });
            foreach (var parameter in type.Parameters)
            {
                AppendValType(key, parameter, group.Length, groupStart, resolve);
            }

            key.Append("->");
            foreach (var result in type.Results)
            {
                AppendValType(key, result, group.Length, groupStart, resolve);
            }

            foreach (var (fieldType, mutable) in type.Fields)
            {
                key.Append(mutable ? 'm' : 'c');
                AppendValType(key, fieldType, group.Length, groupStart, resolve);
            }

            key.Append(';');
        }

        var keyString = key.ToString();
        lock (_lock)
        {
            if (_groups.TryGetValue(keyString, out var entry) && entry.TryGetTarget(out var existing))
                return existing;

            var result = new DefinedType[group.Length];
            for (var i = 0; i < group.Length; i++)
            {
                result[i] = new DefinedType(_nextId++, group[i].Kind) { Group = result };
            }

            DefinedType Resolve(int index) => index >= groupStart && index < groupStart + group.Length ? result[index - groupStart] : resolve(index);

            ValType ToValType(RawValType raw) => raw.Code != ValueCode.Ref ? new ValType(raw.Code) : raw.TypeIndex >= 0 ? new ValType(raw.Nullable, Resolve(raw.TypeIndex)) : new ValType(raw.Nullable, raw.Heap);

            for (var i = 0; i < group.Length; i++)
            {
                var raw = group[i];
                var type = result[i];
                type.Final = raw.Final;
                type.Parameters = Array.ConvertAll(raw.Parameters, ToValType);
                type.Results = Array.ConvertAll(raw.Results, ToValType);
                type.Fields = Array.ConvertAll(raw.Fields, field => new FieldType(ToValType(field.Type), field.Mutable));
            }

            // The supertypes are previous types (validated by the decoder), so their depth is known
            for (var i = 0; i < group.Length; i++)
            {
                if (group[i].Super >= 0)
                {
                    var super = Resolve(group[i].Super);
                    result[i].Super = super;
                    result[i].Depth = super.Depth + 1;
                }
            }

            if (_groups.Count >= _cleanupCount)
            {
                RemoveCollectedGroups();
                _cleanupCount = Math.Max(MinimumCleanupCount, _groups.Count * 2);
            }

            _groups[keyString] = new WeakReference<DefinedType[]>(result);
            return result;
        }
    }

    /// <summary>The number of entries, including the ones of the groups collected since the last cleanup.</summary>
    internal int Count
    {
        get
        {
            lock (_lock)
            {
                return _groups.Count;
            }
        }
    }

    /// <summary>Removes the entries of the collected groups.</summary>
    internal void RemoveCollectedGroups()
    {
        lock (_lock)
        {
            foreach (var (key, entry) in _groups)
            {
                if (!entry.TryGetTarget(out _))
                {
                    _groups.Remove(key);
                }
            }
        }
    }

    /// <summary>Returns the canonical function type of parameter and result types (final, without supertype, in a group of its own).</summary>
    public DefinedType GetFunctionType(ValType[] parameters, ValType[] results)
    {
        // The concrete heap types are referenced by their index in this list, after the index of the group (0)
        var referenced = new List<DefinedType>();
        RawValType ToRaw(ValType type)
        {
            if (type.Defined is null)
                return new RawValType(type.Code, type.Nullable, type.Heap, TypeIndex: -1);

            referenced.Add(type.Defined);
            return new RawValType(type.Code, type.Nullable, type.Heap, TypeIndex: referenced.Count);
        }

        var group = new RawSubType[]
        {
            new() { Kind = CompositeKind.Func, Parameters = Array.ConvertAll(parameters, ToRaw), Results = Array.ConvertAll(results, ToRaw) },
        };

        return Canonicalize(group, groupStart: 0, index => referenced[index - 1])[0];
    }

    /// <summary>The canonical type <c>(array (mut i16))</c> in a group of its own (the arrays of char codes of the JS String Builtins).</summary>
    public DefinedType MutableI16Array => LazyInitializer.EnsureInitialized(ref _mutableI16Array, () =>
        Canonicalize([new RawSubType { Kind = CompositeKind.Array, Fields = [(RawValType.Of(ValueCode.I16), true)] }], groupStart: 0, index => throw new InvalidOperationException("The type has no type index"))[0]);


    private static void AppendTypeIndex(StringBuilder key, int index, int count, int groupStart, Func<int, DefinedType> resolve)
    {
        if (index >= groupStart && index < groupStart + count)
        {
            key.Append('r').Append((index - groupStart).ToString(CultureInfo.InvariantCulture));
        }
        else
        {
            key.Append('c').Append(resolve(index).Id.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static void AppendValType(StringBuilder key, RawValType type, int count, int groupStart, Func<int, DefinedType> resolve)
    {
        key.Append(' ');
        key.Append(((int)type.Code).ToString(CultureInfo.InvariantCulture));
        if (type.Code != ValueCode.Ref)
            return;

        key.Append(type.Nullable ? '?' : '!');
        if (type.TypeIndex >= 0)
        {
            AppendTypeIndex(key, type.TypeIndex, count, groupStart, resolve);
        }
        else
        {
            key.Append('h').Append(((int)type.Heap).ToString(CultureInfo.InvariantCulture));
        }
    }
}

/// <summary>The subtyping relations (https://webassembly.github.io/spec/core/valid/matching.html).</summary>
internal static class TypeRelations
{
    public static bool IsSubtype(ValType sub, ValType super)
    {
        if (sub.Code == ValueCode.Bottom)
            return true;

        if (sub.Code != super.Code)
            return false;

        if (sub.Code != ValueCode.Ref)
            return true;

        if (sub.Nullable && !super.Nullable)
            return false;

        return IsHeapSubtype(sub.Heap, sub.Defined, super.Heap, super.Defined);
    }

    public static bool IsEquivalent(ValType a, ValType b) => IsSubtype(a, b) && IsSubtype(b, a);

    public static bool IsHeapSubtype(WasmHeapKind sub, DefinedType? subDefined, WasmHeapKind super, DefinedType? superDefined)
    {
        if (subDefined is not null)
        {
            if (superDefined is not null)
                return subDefined.IsSubtypeOf(superDefined);

            return subDefined.Kind switch
            {
                CompositeKind.Func => super == WasmHeapKind.Func,
                CompositeKind.Struct => super is WasmHeapKind.Struct or WasmHeapKind.Eq or WasmHeapKind.Any,
                _ => super is WasmHeapKind.Array or WasmHeapKind.Eq or WasmHeapKind.Any,
            };
        }

        if (superDefined is not null)
        {
            return sub switch
            {
                WasmHeapKind.None => superDefined.Kind is CompositeKind.Struct or CompositeKind.Array,
                WasmHeapKind.NoFunc => superDefined.Kind == CompositeKind.Func,
                _ => false,
            };
        }

        if (sub == super)
            return true;

        return sub switch
        {
            WasmHeapKind.None => super is WasmHeapKind.Any or WasmHeapKind.Eq or WasmHeapKind.I31 or WasmHeapKind.Struct or WasmHeapKind.Array,
            WasmHeapKind.I31 or WasmHeapKind.Struct or WasmHeapKind.Array => super is WasmHeapKind.Eq or WasmHeapKind.Any,
            WasmHeapKind.Eq => super == WasmHeapKind.Any,
            WasmHeapKind.NoFunc => super == WasmHeapKind.Func,
            WasmHeapKind.NoExtern => super == WasmHeapKind.Extern,
            WasmHeapKind.NoExn => super == WasmHeapKind.Exn,
            _ => false,
        };
    }

    /// <summary>The top heap type of the hierarchy of a heap type (any, func, extern or exn).</summary>
    public static WasmHeapKind GetTop(WasmHeapKind heap) => heap switch
    {
        WasmHeapKind.Func or WasmHeapKind.NoFunc or WasmHeapKind.ConcreteFunc => WasmHeapKind.Func,
        WasmHeapKind.Extern or WasmHeapKind.NoExtern => WasmHeapKind.Extern,
        WasmHeapKind.Exn or WasmHeapKind.NoExn or WasmHeapKind.ConcreteExn => WasmHeapKind.Exn,
        _ => WasmHeapKind.Any,
    };

    /// <summary>The bottom heap type of the hierarchy of a heap type (none, nofunc, noextern or noexn).</summary>
    public static WasmHeapKind GetBottom(WasmHeapKind heap) => GetTop(heap) switch
    {
        WasmHeapKind.Func => WasmHeapKind.NoFunc,
        WasmHeapKind.Extern => WasmHeapKind.NoExtern,
        WasmHeapKind.Exn => WasmHeapKind.NoExn,
        _ => WasmHeapKind.None,
    };

    /// <summary>Whether a declared sub type matches its supertype (https://webassembly.github.io/spec/core/valid/types.html#subtypes).</summary>
    public static bool IsValidSubtype(DefinedType sub, DefinedType super)
    {
        if (sub.Kind != super.Kind || super.Final)
            return false;

        switch (sub.Kind)
        {
            case CompositeKind.Func:
                if (sub.Parameters.Length != super.Parameters.Length || sub.Results.Length != super.Results.Length)
                    return false;

                for (var i = 0; i < sub.Parameters.Length; i++)
                {
                    if (!IsSubtype(super.Parameters[i], sub.Parameters[i]))
                        return false;
                }

                for (var i = 0; i < sub.Results.Length; i++)
                {
                    if (!IsSubtype(sub.Results[i], super.Results[i]))
                        return false;
                }

                return true;

            default:
                if (sub.Fields.Length < super.Fields.Length)
                    return false;

                for (var i = 0; i < super.Fields.Length; i++)
                {
                    if (!IsFieldSubtype(sub.Fields[i], super.Fields[i]))
                        return false;
                }

                return true;
        }
    }

    private static bool IsFieldSubtype(FieldType sub, FieldType super)
    {
        if (sub.Mutable != super.Mutable)
            return false;

        if (sub.Type.IsPacked || super.Type.IsPacked)
            return sub.Type.Code == super.Type.Code;

        return sub.Mutable ? IsEquivalent(sub.Type, super.Type) : IsSubtype(sub.Type, super.Type);
    }
}
