namespace Meziantou.Framework;

/// <summary>
/// Specifies the casing transformation to apply when creating a slug.
/// </summary>
public enum CasingTransformation
{
    /// <summary>
    /// Preserves the original case of characters.
    /// </summary>
    PreserveCase,

    /// <summary>
    /// Converts characters to lowercase.
    /// </summary>
    ToLowerCase,

    /// <summary>
    /// Converts characters to uppercase.
    /// </summary>
    ToUpperCase,
}
