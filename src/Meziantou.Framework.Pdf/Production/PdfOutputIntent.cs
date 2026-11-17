namespace Meziantou.Framework.Pdf;

/// <summary>An output intent of a document (ISO 32000-2, 14.11.5): the printing condition the document is prepared for.</summary>
public sealed class PdfOutputIntent
{
    internal PdfOutputIntent(string? subtype, string? outputConditionIdentifier, string? outputCondition, string? registryName, string? info, int? profileComponentCount)
    {
        Subtype = subtype;
        OutputConditionIdentifier = outputConditionIdentifier;
        OutputCondition = outputCondition;
        RegistryName = registryName;
        Info = info;
        ProfileComponentCount = profileComponentCount;
    }

    /// <summary>Gets the subtype (<c>/S</c>): <c>GTS_PDFX</c>, <c>GTS_PDFA1</c>, <c>ISO_PDFE1</c>...</summary>
    public string? Subtype { get; }

    /// <summary>Gets the identifier of the printing condition (<c>/OutputConditionIdentifier</c>), such as <c>FOGRA39</c>.</summary>
    public string? OutputConditionIdentifier { get; }

    /// <summary>Gets the description of the printing condition (<c>/OutputCondition</c>).</summary>
    public string? OutputCondition { get; }

    /// <summary>Gets the registry of the condition identifier (<c>/RegistryName</c>), such as <c>http://www.color.org</c>.</summary>
    public string? RegistryName { get; }

    /// <summary>Gets the additional information on the condition (<c>/Info</c>).</summary>
    public string? Info { get; }

    /// <summary>Gets the number of components of the ICC profile of the condition (<c>/DestOutputProfile</c>): 4 for CMYK; <see langword="null"/> when the intent has no profile.</summary>
    public int? ProfileComponentCount { get; }
}
