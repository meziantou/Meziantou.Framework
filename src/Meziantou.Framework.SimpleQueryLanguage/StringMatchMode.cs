namespace Meziantou.Framework.SimpleQueryLanguage;

/// <summary>Specifies how a string handler of <see cref="ExpressionQueryBuilder{T}"/> matches the value of the query.</summary>
public enum StringMatchMode
{
    /// <summary>The property contains the value (<see cref="string.Contains(string)"/>).</summary>
    Contains,

    /// <summary>The property is equal to the value.</summary>
    Exact,

    /// <summary>The property starts with the value (<see cref="string.StartsWith(string)"/>, which uses the current culture in memory).</summary>
    StartsWith,

    /// <summary>The property ends with the value (<see cref="string.EndsWith(string)"/>, which uses the current culture in memory).</summary>
    EndsWith,
}
