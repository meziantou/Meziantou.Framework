using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Layout;

/// <summary>A heading the layout placed: where it is, for the outline, the table of contents and the links to it.</summary>
internal sealed record PlacedHeading(int Level, string Text, int PageIndex, double Top);
