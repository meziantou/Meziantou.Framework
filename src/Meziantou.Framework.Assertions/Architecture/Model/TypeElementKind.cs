namespace Meziantou.Framework.Assertions.Architecture;

/// <summary>Specifies the kind of a <see cref="TypeElement"/>.</summary>
public enum TypeElementKind
{
    /// <summary>A class, including a record class.</summary>
    Class,

    /// <summary>A struct, including a record struct.</summary>
    Struct,

    /// <summary>An interface.</summary>
    Interface,

    /// <summary>An enum.</summary>
    Enum,

    /// <summary>A delegate.</summary>
    Delegate,
}
