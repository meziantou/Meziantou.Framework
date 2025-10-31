namespace Meziantou.Framework.CodeDom;

/// <summary>Specifies the modifiers that can be applied to type and member declarations.</summary>
[Flags]
public enum Modifiers
{
    /// <summary>No modifiers.</summary>
    None = 0x0,

    /// <summary>Private access modifier.</summary>
    Private = 0x1,

    /// <summary>Protected access modifier.</summary>
    Protected = 0x2,

    /// <summary>Internal access modifier.</summary>
    Internal = 0x4,

    /// <summary>Public access modifier.</summary>
    Public = 0x8,

    /// <summary>Abstract modifier.</summary>
    Abstract = 0x10,

    /// <summary>Async modifier.</summary>
    Async = 0x20,

    /// <summary>Const modifier.</summary>
    Const = 0x40,

    /// <summary>New modifier.</summary>
    New = 0x80,

    /// <summary>Override modifier.</summary>
    Override = 0x100,

    /// <summary>Partial modifier.</summary>
    Partial = 0x200,

    /// <summary>ReadOnly modifier.</summary>
    ReadOnly = 0x400,

    /// <summary>Sealed modifier.</summary>
    Sealed = 0x800,

    /// <summary>Static modifier.</summary>
    Static = 0x1000,

    /// <summary>Unsafe modifier.</summary>
    Unsafe = 0x2000,

    /// <summary>Virtual modifier.</summary>
    Virtual = 0x4000,

    /// <summary>Volatile modifier.</summary>
    Volatile = 0x8000,

    /// <summary>Implicit operator modifier.</summary>
    Implicit = 0x10000,

    /// <summary>Explicit operator modifier.</summary>
    Explicit = 0x20000,

    /// <summary>Ref modifier.</summary>
    Ref = 0x40000,
}
