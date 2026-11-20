using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Pdf;

/// <summary>A line of a paragraph: its fragments in logical order, its width and its vertical metrics.</summary>
internal sealed record FlowLine(List<FlowFragment> Fragments, double Width, double Height, double Ascent, bool EndsParagraph);
