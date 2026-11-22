using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Meziantou.Framework.Collections;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Meziantou.Framework.WasmEngine.SourceGeneration;

internal static class WasmDiagnostics
{
    private const string Category = "Meziantou.Framework.WasmEngine.SourceGeneration";

    public static readonly DiagnosticDescriptor TypeMustBePartial = new(
        id: "MFWASM001",
        title: "The type of a WebAssembly import must be partial",
        messageFormat: "Type '{0}' has methods marked with [WasmImport] and must be declared partial, as must its containing types",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedType = new(
        id: "MFWASM002",
        title: "The type of a WebAssembly import is not supported",
        messageFormat: "Type '{0}' cannot have methods marked with [WasmImport] because it is {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedMethod = new(
        id: "MFWASM003",
        title: "The method cannot be a WebAssembly import",
        messageFormat: "Method '{0}' cannot be a WebAssembly import: {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DuplicateImport = new(
        id: "MFWASM004",
        title: "The WebAssembly import is defined twice",
        messageFormat: "The import '{0}' '{1}' of method '{2}' is already defined by '{3}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidModule = new(
        id: "MFWASM005",
        title: "Invalid WebAssembly module",
        messageFormat: "The file '{0}' is not a valid WebAssembly module: {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor CannotReadModule = new(
        id: "MFWASM006",
        title: "Cannot read the WebAssembly module",
        messageFormat: "The file '{0}' cannot be read: {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DuplicateClassName = new(
        id: "MFWASM007",
        title: "Duplicate WebAssembly module class",
        messageFormat: "The module '{0}' generates the class '{1}', as does the module '{2}'. Set the WasmClassName metadata of one of the items.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidName = new(
        id: "MFWASM008",
        title: "Invalid name of a WebAssembly module class",
        messageFormat: "The {0} '{1}' of the module '{2}' is not a valid C# {0}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}

/// <summary>How a .NET value is converted to and from a <c>WasmValue</c>.</summary>
internal enum ValueMarshalling : byte
{
    Int32,
    UInt32,
    Int64,
    UInt64,
    Single,
    Double,
    Value,
    HostObject,
    Function,
}

internal static class WasmCode
{
    public const string Namespace = "global::Meziantou.Framework.WasmEngine.";
    public const string WasmValue = Namespace + "WasmValue";
    public const string HostValueHelper = "__WasmToHostValue";

    /// <summary>The .NET type of the values of an export: the numbers, a .NET object for <c>externref</c>, a function for the function references, and a <c>WasmValue</c> for the others.</summary>
    public static ValueMarshalling GetMarshalling(WasmTypeInfo type) => type.Kind switch
    {
        WasmTypeKind.I32 => ValueMarshalling.Int32,
        WasmTypeKind.I64 => ValueMarshalling.Int64,
        WasmTypeKind.F32 => ValueMarshalling.Single,
        WasmTypeKind.F64 => ValueMarshalling.Double,
        WasmTypeKind.Ref when type.Heap is WasmHeapTypeKind.Extern => ValueMarshalling.HostObject,
        WasmTypeKind.Ref when type.Heap is WasmHeapTypeKind.Func or WasmHeapTypeKind.ConcreteFunc => ValueMarshalling.Function,
        _ => ValueMarshalling.Value,
    };

    public static string GetClrType(ValueMarshalling marshalling, bool nullable) => marshalling switch
    {
        ValueMarshalling.Int32 => "int",
        ValueMarshalling.UInt32 => "uint",
        ValueMarshalling.Int64 => "long",
        ValueMarshalling.UInt64 => "ulong",
        ValueMarshalling.Single => "float",
        ValueMarshalling.Double => "double",
        ValueMarshalling.HostObject => nullable ? "object?" : "object",
        ValueMarshalling.Function => Namespace + (nullable ? "WasmFunction?" : "WasmFunction"),
        _ => WasmValue,
    };

    public static string ToWasmValue(ValueMarshalling marshalling, string expression) => marshalling switch
    {
        ValueMarshalling.Int32 => WasmValue + ".FromI32(" + expression + ")",
        ValueMarshalling.UInt32 => WasmValue + ".FromI32(unchecked((int)" + expression + "))",
        ValueMarshalling.Int64 => WasmValue + ".FromI64(" + expression + ")",
        ValueMarshalling.UInt64 => WasmValue + ".FromI64(unchecked((long)" + expression + "))",
        ValueMarshalling.Single => WasmValue + ".FromF32(" + expression + ")",
        ValueMarshalling.Double => WasmValue + ".FromF64(" + expression + ")",
        ValueMarshalling.HostObject => WasmValue + ".FromHostValue(" + expression + ")",
        ValueMarshalling.Function => WasmValue + ".FromReference(" + expression + ")",
        _ => expression,
    };

    public static string FromWasmValue(ValueMarshalling marshalling, bool nullable, string expression) => marshalling switch
    {
        ValueMarshalling.Int32 => expression + ".I32",
        ValueMarshalling.UInt32 => "unchecked((uint)" + expression + ".I32)",
        ValueMarshalling.Int64 => expression + ".I64",
        ValueMarshalling.UInt64 => "unchecked((ulong)" + expression + ".I64)",
        ValueMarshalling.Single => expression + ".F32",
        ValueMarshalling.Double => expression + ".F64",
        ValueMarshalling.HostObject => HostValueHelper + "(" + expression + ")" + (nullable ? "" : "!"),
        ValueMarshalling.Function => "(" + Namespace + (nullable ? "WasmFunction?" : "WasmFunction") + ")" + expression + ".Reference" + (nullable ? "" : "!"),
        _ => expression,
    };

    public static void WriteHostValueHelper(CodeWriter writer)
    {
        writer.WriteLine("private static object? " + HostValueHelper + "(" + WasmValue + " value) => value.Reference is " + Namespace + "WasmHostReference reference ? reference.Value : value.Reference;");
    }

    public static string GetValueTypeExpression(WasmTypeInfo type) => type.Kind switch
    {
        WasmTypeKind.I32 => Namespace + "WasmValueType.I32",
        WasmTypeKind.I64 => Namespace + "WasmValueType.I64",
        WasmTypeKind.F32 => Namespace + "WasmValueType.F32",
        WasmTypeKind.F64 => Namespace + "WasmValueType.F64",
        WasmTypeKind.V128 => Namespace + "WasmValueType.V128",
        _ => "new " + Namespace + "WasmValueType(" + Namespace + "WasmValueKind.Ref, " + Namespace + "WasmHeapKind." + type.Heap.ToString() + ", " + (type.Nullable ? "true" : "false") + ")",
    };

    public static string GetValueTypesExpression(IEnumerable<WasmTypeInfo> types) => "[" + string.Join(", ", types.Select(GetValueTypeExpression)) + "]";

    /// <summary>The type of the results of a method: void, a value, or a tuple of values.</summary>
    public static string GetReturnType(IReadOnlyList<string> types) => types.Count switch
    {
        0 => "void",
        1 => types[0],
        _ => "(" + string.Join(", ", types) + ")",
    };

    public static string Literal(string value) => SymbolDisplay.FormatLiteral(value, quote: true);

    /// <summary>A text that can be written in an XML documentation comment, on one line.</summary>
    public static string XmlText(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in SymbolDisplay.FormatLiteral(value, quote: false))
        {
            builder.Append(c switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                _ => c.ToString(),
            });
        }

        return builder.ToString();
    }
}

internal sealed class CodeWriter
{
    private readonly StringBuilder _builder = new();
    private int _indentation;

    public void WriteLine()
    {
        _builder.Append('\n');
    }

    public void WriteLine(string text)
    {
        _builder.Append(' ', _indentation * 4).Append(text).Append('\n');
    }

    public void OpenBlock()
    {
        WriteLine("{");
        _indentation++;
    }

    public void CloseBlock() => CloseBlockWith("}");

    public void CloseBlockWith(string text)
    {
        _indentation--;
        WriteLine(text);
    }

    public override string ToString() => _builder.ToString();
}

internal sealed record LocationInfo(string FilePath, TextSpan TextSpan, LinePositionSpan LineSpan)
{
    public static LocationInfo? Create(Location? location)
        => location is { SourceTree: { } tree } ? new LocationInfo(tree.FilePath, location.SourceSpan, location.GetLineSpan().Span) : null;

    public Location ToLocation(Dictionary<string, SyntaxTree?>? trees)
        => trees is not null && trees.TryGetValue(FilePath, out var tree) && tree is not null ? Location.Create(tree, TextSpan) : Location.Create(FilePath, TextSpan, LineSpan);
}

/// <summary>A diagnostic without references to the compilation.</summary>
internal sealed record DiagnosticInfo(DiagnosticDescriptor Descriptor, LocationInfo? Location, ImmutableEquatableArray<string> Arguments)
{
    public static DiagnosticInfo Create(DiagnosticDescriptor descriptor, Location? location, params object?[] arguments)
        => Create(descriptor, LocationInfo.Create(location), arguments);

    public static DiagnosticInfo Create(DiagnosticDescriptor descriptor, LocationInfo? location, params object?[] arguments)
        => new(descriptor, location, arguments.Select(argument => argument?.ToString() ?? string.Empty).ToImmutableEquatableArray());

    public Diagnostic ToDiagnostic(Dictionary<string, SyntaxTree?>? trees) => Diagnostic.Create(Descriptor, Location?.ToLocation(trees), [.. Arguments]);
}

internal static class StableHash
{
    // FNV-1a: string.GetHashCode is randomized per process, so the names of the files would change between builds
    public static string Compute(string value)
    {
        unchecked
        {
            var hash = 2166136261;
            foreach (var c in value)
            {
                hash ^= c;
                hash *= 16777619;
            }

            return hash.ToString("x8", CultureInfo.InvariantCulture);
        }
    }
}
