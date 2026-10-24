namespace Meziantou.Framework.Pdf.Filters.Jbig2;

/// <summary>The contexts a symbol dictionary may retain for the dictionaries that refer to it (7.4.2.2).</summary>
internal sealed class Jbig2RetainedContexts(int template, int refinementTemplate)
{
    public int Template { get; } = template;

    public int RefinementTemplate { get; } = refinementTemplate;

    public byte[] Generic { get; private init; } = new byte[Jbig2GenericRegion.GetContextCount(template)];

    public byte[] Refinement { get; private init; } = new byte[Jbig2RefinementRegion.GetContextCount(refinementTemplate)];

    /// <summary>Copies the contexts: a dictionary that uses them starts from their values at the end of the dictionary that retained them.</summary>
    public Jbig2RetainedContexts Clone() => new(Template, RefinementTemplate) { Generic = (byte[])Generic.Clone(), Refinement = (byte[])Refinement.Clone() };
}
