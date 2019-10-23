using System;

namespace Meziantou.Framework.CodeDom
{
    [Flags]
    public enum Modifiers
    {
        None = 0x0,
        Private = 0x1,
        Protected = 0x2,
        Internal = 0x4,
        Public = 0x8,
        Abstract = 0x10,
        Async = 0x20,
        Const = 0x40,
        New = 0x80,
        Override = 0x100,
        Partial = 0x200,
        ReadOnly = 0x400,
        Sealed = 0x800,
        Static = 0x1000,
        Unsafe = 0x2000,
        Virtual = 0x4000,
        Volatile = 0x8000,
        Implicit = 0x10000,
        Explicit = 0x20000,
        Ref = 0x40000,
    }
}
