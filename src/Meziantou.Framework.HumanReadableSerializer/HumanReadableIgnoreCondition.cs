namespace Meziantou.Framework.HumanReadable;

public enum HumanReadableIgnoreCondition
{
    /// <summary>
    /// Property is never ignored during serialization.
    /// </summary>
    Never,

    /// <summary>
    /// Property is always ignored during serialization.
    /// </summary>
    Always,

    /// <summary>
    /// If the value is the default, the property is ignored during serialization.
    /// This is applied to both reference and value-type properties and fields.
    /// </summary>
    WhenWritingDefault,

    /// <summary>
    /// If the value is <see langword="null"/>, the property is ignored during serialization.
    /// This is applied only to reference-type or <see cref="Nullable{T}"/> properties and fields.
    /// </summary>
    WhenWritingNull,

    /// <summary>
    /// If the value implements <see cref="System.Collections.IEnumerable"/> and the collection is empty, the property is ignored during serialization.
    /// </summary>
    /// <remarks>If the value is not a collection, the property is not ignored during serialization.</remarks>
    WhenWritingEmptyCollection,

    /// <summary>
    /// If the value implements <see cref="System.Collections.IEnumerable"/> and the collection is empty, or if the value is the default, the property is ignored during serialization.
    /// </summary>
    WhenWritingDefaultOrEmptyCollection,
}
