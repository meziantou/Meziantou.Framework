namespace Meziantou.Framework;

/// <summary>Specifies the case transformation to apply when generating a slug.</summary>
public enum CasingTransformation
{
    /// <summary>Preserves the original case of the input text.</summary>
    PreserveCase,

    /// <summary>Converts all characters to lowercase.</summary>
    ToLowerCase,

    /// <summary>Converts all characters to uppercase.</summary>
    ToUpperCase,
}
