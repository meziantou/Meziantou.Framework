using Meziantou.Framework.Barcodes.Internal.Linear;

namespace Meziantou.Framework.Barcodes;

/// <summary>
/// Represents a GS1 element string: an application identifier (AI) and the data it qualifies, such as AI <c>01</c>
/// (GTIN) and <c>09521234543213</c>.
/// </summary>
/// <remarks>
/// The data does not include the parentheses of the human-readable interpretation: they are printed under the bars but
/// are not encoded. The application identifier and the data are checked against the syntax rules every GS1 element follows,
/// not against the GS1 dictionary of application identifiers (the format of the data of each identifier).
/// </remarks>
public sealed class Gs1Element
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Gs1Element"/> class.
    /// </summary>
    /// <param name="applicationIdentifier">The application identifier: 2 to 4 digits, without parentheses.</param>
    /// <param name="value">
    /// The data: characters of the GS1 AI encodable character set 82 (letters, digits and <c>!"%&amp;'()*+,-./:;&lt;=&gt;?_</c>).
    /// The application identifiers whose data has a predefined length (<c>00</c> to <c>04</c>, <c>11</c> to <c>20</c>,
    /// <c>31nn</c> to <c>36nn</c> and <c>41n</c>) require digits of that length.
    /// </param>
    /// <exception cref="ArgumentException">The application identifier or the data is not valid.</exception>
    public Gs1Element(string applicationIdentifier, string value)
    {
        ArgumentNullException.ThrowIfNull(applicationIdentifier);
        ArgumentNullException.ThrowIfNull(value);
        Gs1128Encoder.Validate(applicationIdentifier, value);

        ApplicationIdentifier = applicationIdentifier;
        Value = value;
    }

    /// <summary>Gets the application identifier, such as <c>01</c>.</summary>
    public string ApplicationIdentifier { get; }

    /// <summary>Gets the data that the application identifier qualifies.</summary>
    public string Value { get; }

    /// <summary>Returns the human-readable interpretation of the element string, such as <c>(01)09521234543213</c>.</summary>
    /// <returns>The application identifier in parentheses, followed by the data.</returns>
    public override string ToString() => "(" + ApplicationIdentifier + ")" + Value;
}
