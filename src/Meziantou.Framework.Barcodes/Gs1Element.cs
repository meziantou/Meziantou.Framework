using Meziantou.Framework.Barcodes.Internal.Gs1;
using Meziantou.Framework.Barcodes.Internal.Linear;

namespace Meziantou.Framework.Barcodes;

/// <summary>
/// Represents a GS1 element string: an application identifier (AI) and the data it qualifies, such as AI <c>01</c>
/// (GTIN) and <c>09521234543213</c>.
/// </summary>
/// <remarks>
/// The data does not include the parentheses of the human-readable interpretation: they are printed under the bars but
/// are not encoded. By default, the application identifier and the data are checked against the syntax rules every GS1
/// element follows. Use <see cref="Gs1ValidationMode.Dictionary"/> to also check them against the GS1 dictionary of
/// application identifiers (the format of the data of each identifier, its check digits, dates, ...), and
/// <see cref="ValidateAssociations"/> to check the identifiers that must or must not be used together.
/// </remarks>
public sealed class Gs1Element
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Gs1Element"/> class, checked against the syntax rules of element strings.
    /// </summary>
    /// <param name="applicationIdentifier">The application identifier: 2 to 4 digits, without parentheses.</param>
    /// <param name="value">
    /// The data: characters of the GS1 AI encodable character set 82 (letters, digits and <c>!"%&amp;'()*+,-./:;&lt;=&gt;?_</c>),
    /// and <c>#</c> for the application identifier <c>8010</c>. The application identifiers whose data has a predefined length (<c>00</c> to <c>04</c>, <c>11</c> to <c>20</c>,
    /// <c>31nn</c> to <c>36nn</c> and <c>41n</c>) require digits of that length.
    /// </param>
    /// <exception cref="ArgumentException">The application identifier or the data is not valid.</exception>
    public Gs1Element(string applicationIdentifier, string value)
        : this(applicationIdentifier, value, Gs1ValidationMode.Syntax)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Gs1Element"/> class.
    /// </summary>
    /// <param name="applicationIdentifier">The application identifier: 2 to 4 digits, without parentheses.</param>
    /// <param name="value">The data that the application identifier qualifies.</param>
    /// <param name="validationMode">The rules the application identifier and the data are checked against.</param>
    /// <exception cref="ArgumentException">
    /// The application identifier or the data is not valid, or, with <see cref="Gs1ValidationMode.Dictionary"/>, the
    /// application identifier is not in the GS1 dictionary or the data does not match its format.
    /// </exception>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Barcodes.Tests/Gs1DictionaryTests.cs" ref:region="Gs1Element_Dictionary" ref:dedent="true">
    /// // The syntax rules accept any data of 14 digits for a GTIN
    /// var element = new Gs1Element("01", "09521234543214");
    /// Assert.Equal("(01)09521234543214", element.ToString());
    ///
    /// // The GS1 dictionary also checks the check digit, the dates, the country codes, ...
    /// var exception = Assert.Throws&lt;ArgumentException&gt;("value", () =&gt; new Gs1Element("01", "09521234543214", Gs1ValidationMode.Dictionary));
    /// Assert.Contains("The numeric check digit is incorrect.", exception.Message);
    ///
    /// var gtin = new Gs1Element("01", "09521234543213", Gs1ValidationMode.Dictionary);
    /// var expiry = new Gs1Element("17", "250100", Gs1ValidationMode.Dictionary); // day 00: the end of the month
    /// var barcode = Barcode.CreateGs1128(gtin, expiry);
    /// Assert.Equal("(01)09521234543213(17)250100", barcode.Text);
    /// </code>
    /// </example>
    public Gs1Element(string applicationIdentifier, string value, Gs1ValidationMode validationMode)
    {
        ArgumentNullException.ThrowIfNull(applicationIdentifier);
        ArgumentNullException.ThrowIfNull(value);
        if (validationMode is not (Gs1ValidationMode.Syntax or Gs1ValidationMode.Dictionary))
            throw new ArgumentOutOfRangeException(nameof(validationMode), validationMode, "The validation mode is not valid.");

        Gs1128Encoder.Validate(applicationIdentifier, value);
        if (validationMode is Gs1ValidationMode.Dictionary)
        {
            Gs1DictionaryValidator.Validate(applicationIdentifier, value);
        }

        ApplicationIdentifier = applicationIdentifier;
        Value = value;
    }

    /// <summary>Gets the application identifier, such as <c>01</c>.</summary>
    public string ApplicationIdentifier { get; }

    /// <summary>Gets the data that the application identifier qualifies.</summary>
    public string Value { get; }

    /// <summary>
    /// Checks the associations of application identifiers that the GS1 dictionary defines: the identifiers that an
    /// identifier requires, such as a batch number (<c>10</c>) that requires a GTIN (<c>01</c>), and the identifiers that
    /// cannot be used together, such as a GTIN (<c>01</c>) and the GTIN of the contained items (<c>02</c>). An
    /// application identifier cannot appear twice with different data.
    /// </summary>
    /// <param name="elements">
    /// The elements marked on an item. Pass the elements of every barcode of the item: an identifier may require an
    /// identifier encoded in another barcode. The application identifiers that are not in the dictionary have no rules.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="elements"/> contains <see langword="null"/>, a required application identifier is missing, two
    /// application identifiers cannot be used together, or an application identifier appears twice with different data.
    /// </exception>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Barcodes.Tests/Gs1DictionaryTests.cs" ref:region="Gs1Element_ValidateAssociations" ref:dedent="true">
    /// var gtin = new Gs1Element("01", "09521234543213", Gs1ValidationMode.Dictionary);
    /// var batch = new Gs1Element("10", "ABC123", Gs1ValidationMode.Dictionary);
    /// Gs1Element.ValidateAssociations(gtin, batch);
    ///
    /// // A batch number qualifies a trade item: it requires a GTIN
    /// Assert.Throws&lt;ArgumentException&gt;("elements", () =&gt; Gs1Element.ValidateAssociations(batch));
    /// </code>
    /// </example>
    public static void ValidateAssociations(params ReadOnlySpan<Gs1Element> elements)
    {
        Gs1DictionaryValidator.ValidateAssociations(elements, nameof(elements));
    }

    /// <summary>Returns the human-readable interpretation of the element string, such as <c>(01)09521234543213</c>.</summary>
    /// <returns>The application identifier in parentheses, followed by the data.</returns>
    public override string ToString() => "(" + ApplicationIdentifier + ")" + Value;
}
