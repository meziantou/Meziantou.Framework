using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Redaction;

/// <summary>A content stream without what it painted in the areas of a redaction, and the changes of its resources.</summary>
internal sealed class RedactedContent(byte[] data, List<(PdfName Category, PdfName Name, PdfObject Value)> added, HashSet<(PdfName Category, PdfName Name)> replaced, HashSet<(PdfName Category, PdfName Name)> used)
{
    public byte[] Data { get; } = data;

    /// <summary>
    /// Changes a resource dictionary of the content alone: adds the redacted copies, and removes the resources they
    /// replace when the content does not use them anymore, so that a full save does not write them.
    /// </summary>
    /// <param name="resources">A resource dictionary that belongs to the content alone; its categories may be shared, and are copied before they change.</param>
    public void ApplyTo(PdfDictionary resources)
    {
        // The categories that change are copied once: a category may be shared with other pages or forms
        var categories = new Dictionary<PdfName, PdfDictionary>();
        PdfDictionary GetCategory(PdfName category)
        {
            if (!categories.TryGetValue(category, out var copy))
            {
                copy = resources.GetResolved(category) is PdfDictionary existing ? existing.CloneShallow() : new PdfDictionary();
                resources[category] = copy;
                categories[category] = copy;
            }

            return copy;
        }

        foreach (var (category, name, value) in added)
        {
            GetCategory(category)[name] = value;
        }

        foreach (var (category, name) in replaced)
        {
            if (!used.Contains((category, name)))
            {
                GetCategory(category).Remove(name);
            }
        }
    }
}
