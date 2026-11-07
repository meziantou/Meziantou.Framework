namespace Meziantou.Framework.Language.Toml;

/// <summary>The versions of the TOML specification the parser can read.</summary>
public enum TomlVersion
{
    /// <summary>TOML 1.0.0.</summary>
    V1_0 = 0,

    /// <summary>
    /// TOML 1.1.0, which adds newlines and trailing commas in inline tables, the <c>\e</c> and <c>\xHH</c> escape
    /// sequences, and times written without seconds.
    /// </summary>
    V1_1 = 1,

    /// <summary>The most recent version the parser knows.</summary>
    Latest = V1_1,
}
