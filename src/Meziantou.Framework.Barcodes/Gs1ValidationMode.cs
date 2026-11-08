namespace Meziantou.Framework.Barcodes;

/// <summary>
/// Specifies how a <see cref="Gs1Element"/> checks its application identifier and its data.
/// </summary>
public enum Gs1ValidationMode
{
    /// <summary>
    /// The syntax rules every element string follows: an application identifier of 2 to 4 digits, data of the GS1 AI
    /// encodable character set 82, and the length of the data of the identifiers whose length is predefined.
    /// </summary>
    Syntax = 0,

    /// <summary>
    /// The syntax rules, and the GS1 dictionary of application identifiers: the identifier must be known, and its data must
    /// match the format of the identifier, including its check digits, dates, times, and country and currency codes.
    /// </summary>
    Dictionary = 1,
}
