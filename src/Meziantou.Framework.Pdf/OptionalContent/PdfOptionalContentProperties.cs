using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>
/// The optional content of a document (<c>/OCProperties</c>, ISO 32000-2, 8.11.4): its groups (the layers) and its
/// configurations. The dictionary is created when a group is added.
/// </summary>
public sealed class PdfOptionalContentProperties
{
    // The groups and configurations read: a bounded number
    internal const int MaxGroups = 4096;

    private static readonly PdfName OCPropertiesName = PdfName.Create("OCProperties");
    private static readonly PdfName OCGsName = PdfName.Create("OCGs");
    private static readonly PdfName OCGName = PdfName.Create("OCG");
    private static readonly PdfName ConfigsName = PdfName.Create("Configs");
    private static readonly PdfName OrderName = PdfName.Create("Order");

    private readonly PdfDocument _document;

    internal PdfOptionalContentProperties(PdfDocument document)
    {
        _document = document;
    }

    /// <summary>Gets the optional content properties dictionary of the catalog, or <see langword="null"/> when the document has none.</summary>
    public PdfDictionary? Dictionary => _document.Catalog.Get<PdfDictionary>(OCPropertiesName);

    /// <summary>Gets the groups of the document (<c>/OCGs</c>).</summary>
    public IReadOnlyList<PdfOptionalContentGroup> Groups
    {
        get
        {
            var result = new List<PdfOptionalContentGroup>();
            if (Dictionary?.Get<PdfArray>(OCGsName) is not { } groups)
                return result;

            for (var i = 0; i < groups.Count && result.Count < MaxGroups; i++)
            {
                if (groups[i] is PdfReference reference && reference.Resolve() is PdfDictionary group)
                {
                    result.Add(new PdfOptionalContentGroup(_document, group, reference));
                }
            }

            return result;
        }
    }

    /// <summary>Gets the default configuration (<c>/D</c>): the state of the groups when the document is opened; <see langword="null"/> when the document has no optional content.</summary>
    public PdfOptionalContentConfiguration? DefaultConfiguration => Dictionary?.Get<PdfDictionary>(PdfNames.D) is { } configuration ? new PdfOptionalContentConfiguration(_document, configuration) : null;

    /// <summary>Gets the alternate configurations (<c>/Configs</c>).</summary>
    public IReadOnlyList<PdfOptionalContentConfiguration> Configurations
    {
        get
        {
            var result = new List<PdfOptionalContentConfiguration>();
            if (Dictionary?.Get<PdfArray>(ConfigsName) is { } configurations)
            {
                for (var i = 0; i < configurations.Count && result.Count < MaxGroups; i++)
                {
                    if (configurations.GetResolved(i) is PdfDictionary configuration)
                    {
                        result.Add(new PdfOptionalContentConfiguration(_document, configuration));
                    }
                }
            }

            return result;
        }
    }

    /// <summary>Adds a group to the document, at the end of the list of layers viewers show (<c>/Order</c> of the default configuration).</summary>
    /// <param name="name">The name of the group.</param>
    /// <param name="visible">Whether the group is visible when the document is opened.</param>
    /// <returns>The group.</returns>
    public PdfOptionalContentGroup AddGroup(string name, bool visible = true)
    {
        ArgumentNullException.ThrowIfNull(name);
        var properties = GetOrCreateDictionary();
        var dictionary = new PdfDictionary
        {
            [PdfNames.Type] = OCGName,
            [PdfNames.Name] = PdfString.FromText(name),
        };
        var reference = _document.Objects.Add(dictionary);
        if (properties.Get<PdfArray>(OCGsName) is not { } groups)
        {
            groups = [];
            properties[OCGsName] = groups;
        }

        groups.Add(reference);
        var configuration = GetOrCreateDefaultConfiguration();
        if (configuration.Dictionary.Get<PdfArray>(OrderName) is not { } order)
        {
            order = [];
            configuration.Dictionary[OrderName] = order;
        }

        order.Add(reference);
        var group = new PdfOptionalContentGroup(_document, dictionary, reference);
        configuration.SetVisible(group, visible);
        return group;
    }

    internal PdfOptionalContentConfiguration GetOrCreateDefaultConfiguration()
    {
        var properties = GetOrCreateDictionary();
        if (properties.Get<PdfDictionary>(PdfNames.D) is not { } configuration)
        {
            configuration = new PdfDictionary();
            properties[PdfNames.D] = configuration;
        }

        return new PdfOptionalContentConfiguration(_document, configuration);
    }

    private PdfDictionary GetOrCreateDictionary()
    {
        if (Dictionary is { } dictionary)
            return dictionary;

        dictionary = new PdfDictionary { [OCGsName] = new PdfArray(), [PdfNames.D] = new PdfDictionary() };
        _document.Catalog[OCPropertiesName] = dictionary;
        return dictionary;
    }
}
