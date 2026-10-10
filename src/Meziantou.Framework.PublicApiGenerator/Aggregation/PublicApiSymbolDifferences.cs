namespace Meziantou.Framework.PublicApiGenerator;

/// <summary>The parts of a declaration that differ between the variants of an aggregated symbol.</summary>
[Flags]
public enum PublicApiSymbolDifferences
{
    None = 0,

    /// <summary>The kind of symbol differs (e.g. a class and a struct, or a method and an operator).</summary>
    Kind = 1 << 0,
    Accessibility = 1 << 1,

    /// <summary>Modifiers such as <c>static</c>, <c>abstract</c>, <c>virtual</c>, <c>sealed</c>, <c>new</c>, <c>readonly</c>, <c>required</c> or <c>unsafe</c> differ.</summary>
    Modifiers = 1 << 2,

    /// <summary>Types, ref kinds, parameter modifiers, accessors or generic parameters differ, ignoring nullable annotations.</summary>
    Signature = 1 << 3,

    /// <summary>Only the nullable annotations of the types differ.</summary>
    Nullability = 1 << 4,

    /// <summary>The generic constraints differ.</summary>
    Constraints = 1 << 5,

    /// <summary>
    /// The attributes of the symbol, of its parameters, of its return value or of its generic parameters differ.
    /// Attributes that encode other parts of the declaration, such as nullable annotations, tuple element names, ref kinds or the <c>required</c> modifier, are not compared.
    /// </summary>
    Attributes = 1 << 6,

    /// <summary>The base type or the implemented interfaces differ.</summary>
    Inheritance = 1 << 7,

    /// <summary>The value of a constant or the default value of a parameter differs.</summary>
    Value = 1 << 8,

    /// <summary>The names of the parameters differ.</summary>
    ParameterNames = 1 << 9,
}
