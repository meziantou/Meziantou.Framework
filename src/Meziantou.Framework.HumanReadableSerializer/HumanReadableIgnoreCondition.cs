namespace Meziantou.Framework.HumanReadable;

public enum HumanReadableIgnoreCondition
{
    /// <summary>
    /// Property is never ignored during serialization.
    /// </summary>
    Never = 0,

    /// <summary>
    /// Property is always ignored during serialization.
    /// </summary>
    Always = 1,

    /// <summary>
    /// If the value is the default, the property is ignored during serialization.
    /// This is applied to both reference and value-type properties and fields.
    /// </summary>
    WhenWritingDefault = 2,

    /// <summary>
    /// If the value is <see langword="null"/>, the property is ignored during serialization.
    /// This is applied only to reference-type or <see cref="Nullable{T}"/> properties and fields.
    /// </summary>
    WhenWritingNull = 3,
}
