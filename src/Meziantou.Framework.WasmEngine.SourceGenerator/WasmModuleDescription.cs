using System;
using System.Collections.Generic;
using System.Globalization;

namespace Meziantou.Framework.WasmEngine.SourceGeneration;

/// <summary>The kind of a value type: the values of <c>Meziantou.Framework.WasmEngine.WasmValueKind</c>.</summary>
internal enum WasmTypeKind : byte
{
    I32,
    I64,
    F32,
    F64,
    V128,
    Ref,
}

/// <summary>The heap type of a reference type: the values of <c>Meziantou.Framework.WasmEngine.WasmHeapKind</c>.</summary>
internal enum WasmHeapTypeKind : byte
{
    Extern,
    NoExtern,
    Func,
    ConcreteFunc,
    NoFunc,
    Any,
    None,
    Eq,
    I31,
    Array,
    ConcreteArray,
    Struct,
    ConcreteStruct,
    Exn,
    ConcreteExn,
    NoExn,
}

/// <summary>The kind of an export: the values of <c>Meziantou.Framework.WasmEngine.WasmExternKind</c>.</summary>
internal enum WasmExportKind : byte
{
    Function,
    Table,
    Memory,
    Global,
    Tag,
}

/// <summary>A value type, as the engine describes it (a concrete heap type is only described by its kind).</summary>
internal readonly record struct WasmTypeInfo(WasmTypeKind Kind, WasmHeapTypeKind Heap, bool Nullable)
{
    public static WasmTypeInfo I32 => new(WasmTypeKind.I32, default, Nullable: true);

    public static WasmTypeInfo I64 => new(WasmTypeKind.I64, default, Nullable: true);

    public static WasmTypeInfo F32 => new(WasmTypeKind.F32, default, Nullable: true);

    public static WasmTypeInfo F64 => new(WasmTypeKind.F64, default, Nullable: true);

    public static WasmTypeInfo V128 => new(WasmTypeKind.V128, default, Nullable: true);

    public static WasmTypeInfo Reference(WasmHeapTypeKind heap, bool nullable) => new(WasmTypeKind.Ref, heap, nullable);

    public override string ToString() => Kind switch
    {
        WasmTypeKind.I32 => "i32",
        WasmTypeKind.I64 => "i64",
        WasmTypeKind.F32 => "f32",
        WasmTypeKind.F64 => "f64",
        WasmTypeKind.V128 => "v128",
        _ => "(ref " + (Nullable ? "null " : "") + Heap.ToString().ToLowerInvariant() + ")",
    };
}

/// <summary>The type of a function. A parameter name is null when the module does not name it.</summary>
internal sealed class WasmFunctionSignature(IReadOnlyList<WasmTypeInfo> parameters, IReadOnlyList<string?> parameterNames, IReadOnlyList<WasmTypeInfo> results)
{
    public static WasmFunctionSignature Empty { get; } = new([], [], []);

    public IReadOnlyList<WasmTypeInfo> Parameters { get; } = parameters;

    public IReadOnlyList<string?> ParameterNames { get; } = parameterNames;

    public IReadOnlyList<WasmTypeInfo> Results { get; } = results;

    public override string ToString() => "(func (param" + Format(Parameters) + ") (result" + Format(Results) + "))";

    private static string Format(IReadOnlyList<WasmTypeInfo> types)
    {
        var result = "";
        foreach (var type in types)
        {
            result += " " + type.ToString();
        }

        return result;
    }
}

/// <summary>An export of a module. <see cref="Function"/> is the type of an exported function.</summary>
internal sealed class WasmExportDescription(string name, WasmExportKind kind, WasmFunctionSignature? function)
{
    public string Name { get; } = name;

    public WasmExportKind Kind { get; } = kind;

    public WasmFunctionSignature? Function { get; } = function;
}

/// <summary>The exports of a module, in the order of the module.</summary>
internal sealed class WasmModuleDescription(IReadOnlyList<WasmExportDescription> exports)
{
    public IReadOnlyList<WasmExportDescription> Exports { get; } = exports;
}

/// <summary>A module that cannot be read: <see cref="Position"/> is the offset of the error in the bytes or in the text.</summary>
#pragma warning disable CA1032 // Internal: the message has the position of the error
internal sealed class WasmDescriptionException(string message, int position) : FormatException(message + " (at offset " + position.ToString(CultureInfo.InvariantCulture) + ")")
#pragma warning restore CA1032
{
    public int Position { get; } = position;

    public string Reason { get; } = message;
}
