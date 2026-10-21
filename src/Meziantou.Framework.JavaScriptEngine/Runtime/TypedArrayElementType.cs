namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The element types of the TypedArrays (https://tc39.es/ecma262/#table-the-typedarray-constructors), in the order of the specification.</summary>
internal enum TypedArrayElementType : byte
{
    Int8,
    Uint8,
    Uint8Clamped,
    Int16,
    Uint16,
    Int32,
    Uint32,
    BigInt64,
    BigUint64,
    Float16,
    Float32,
    Float64,
}
