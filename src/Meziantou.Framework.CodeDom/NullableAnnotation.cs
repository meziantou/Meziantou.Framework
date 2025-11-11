namespace Meziantou.Framework.CodeDom;

/// <summary>Specifies the nullable annotation for a type reference.</summary>
public enum NullableAnnotation
{
    /// <summary>No nullable annotation is specified.</summary>
    NotSet,

    /// <summary>The type is explicitly non-nullable (!).</summary>
    NotNull,

    /// <summary>The type is nullable (?).</summary>
    Nullable,
}
