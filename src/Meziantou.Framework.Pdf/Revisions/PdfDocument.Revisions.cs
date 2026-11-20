namespace Meziantou.Framework.Pdf;

public sealed partial class PdfDocument
{
    /// <summary>
    /// Gets the revisions of the file the document was read from (ISO 32000-2, 7.5.6): the original file, then one per
    /// incremental update; an empty list for a document that was not read from a file. A repaired file is one revision.
    /// </summary>
    /// <remarks>
    /// The revisions end at the end-of-file markers (<c>%%EOF</c>) that follow the cross-reference sections of the
    /// <c>/Prev</c> chain. The first-page section of a linearized file is part of the revision of the main section.
    /// </remarks>
    public IReadOnlyList<PdfRevision> Revisions
    {
        get
        {
            if (Reader is not { } reader)
                return [];

            var length = reader.Input.Length;
            if (reader.IsRepaired || reader.CrossReferenceOffsets.Count == 0)
                return [new PdfRevision(this, 0, length, isLatest: true)];

            // A revision ends at the marker after its section, and after the sections of the older revisions: the
            // first-page section of a linearized file is before its main section (F.3)
            var ends = reader.CrossReferenceOffsets.Select(reader.FindEndOfFile).ToArray();
            for (var i = ends.Length - 2; i >= 0; i--)
            {
                ends[i] = Math.Max(ends[i], ends[i + 1]);
            }

            var distinct = ends.Distinct().Order().ToList();
            return [.. distinct.Select((end, index) => new PdfRevision(this, index, end, isLatest: index == distinct.Count - 1))];
        }
    }
}
