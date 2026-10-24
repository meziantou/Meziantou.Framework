using System.Text.Json;

namespace Meziantou.Framework.Pdf.TestHarness.ExternalTools;

/// <summary>The objects of a file as <c>qpdf --json=2</c> reads them.</summary>
/// <param name="Objects">The objects by their key (<c>obj:12 0 R</c>; the trailer is <c>trailer</c>).</param>
/// <param name="HasWarnings">Whether qpdf reported warnings: it repaired the file.</param>
public sealed record QpdfObjects(IReadOnlyDictionary<string, JsonElement> Objects, bool HasWarnings);
