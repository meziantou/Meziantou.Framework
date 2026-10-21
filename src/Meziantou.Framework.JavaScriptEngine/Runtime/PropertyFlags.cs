namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The attributes of a property.</summary>
[Flags]
internal enum PropertyFlags : byte
{
    None = 0,
    Writable = 1,
    Enumerable = 2,
    Configurable = 4,

    /// <summary>The property is an accessor property: its value is an <see cref="AccessorPair"/>.</summary>
    Accessor = 8,

    /// <summary>The value of the data property is computed the first time it is read: its value is a <see cref="LazyValue"/>.</summary>
    Lazy = 16,

    /// <summary>The attributes of a property created by an assignment or by CreateDataProperty.</summary>
    Default = Writable | Enumerable | Configurable,

    /// <summary>The attributes of the methods of the built-in objects: writable and configurable, not enumerable.</summary>
    BuiltIn = Writable | Configurable,
}
