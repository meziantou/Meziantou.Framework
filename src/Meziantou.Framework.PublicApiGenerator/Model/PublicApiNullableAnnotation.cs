namespace Meziantou.Framework.PublicApiGenerator;

/// <summary>The nullable annotation of a type reference, as encoded by the C# compiler.</summary>
public enum PublicApiNullableAnnotation
{
    /// <summary>The type was compiled in a context where nullable annotations are disabled.</summary>
    Oblivious,

    /// <summary>The type is not annotated (e.g. <c>string</c>). Value types, other than <see cref="Nullable{T}"/>, are always not annotated.</summary>
    NotAnnotated,

    /// <summary>The type is annotated (e.g. <c>string?</c>).</summary>
    Annotated,
}
