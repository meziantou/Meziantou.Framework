namespace Meziantou.Framework.JavaScriptEngine;

[Flags]
internal enum ObjectFlags
{
    None = 0,

    /// <summary>[[Extensible]]</summary>
    Extensible = 1 << 0,

    /// <summary>The [[SetPrototypeOf]] of an immutable prototype exotic object (https://tc39.es/ecma262/#sec-immutable-prototype-exotic-objects).</summary>
    ImmutablePrototype = 1 << 1,

    /// <summary>[[IsHTMLDDA]] (https://tc39.es/ecma262/#sec-IsHTMLDDA-internal-slot): typeof is "undefined", ToBoolean is false, == null.</summary>
    IsHtmlDda = 1 << 2,

    /// <summary>The object has a [[Call]] internal method.</summary>
    Callable = 1 << 3,

    /// <summary>The object has a [[Construct]] internal method.</summary>
    Constructor = 1 << 4,

    /// <summary>Some own properties whose key is an array index are in the property table (not in the dense elements).</summary>
    IndexedProperties = 1 << 5,

    /// <summary>The object overrides [[GetOwnProperty]], [[Get]], [[Set]], [[HasProperty]] or [[DefineOwnProperty]]: the fast paths do not apply.</summary>
    Exotic = 1 << 6,

    /// <summary>The object is a class constructor (calling it throws a TypeError).</summary>
    ClassConstructor = 1 << 7,
}
