namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The fields present in a property descriptor (a descriptor of [[DefineOwnProperty]] may be partial).</summary>
[Flags]
internal enum DescriptorFields : byte
{
    None = 0,
    Value = 1,
    Writable = 2,
    Get = 4,
    Set = 8,
    Enumerable = 16,
    Configurable = 32,
    Data = Value | Writable,
    Accessor = Get | Set,
    All = Value | Writable | Enumerable | Configurable,
}
