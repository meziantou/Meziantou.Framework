using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Pdf;

/// <summary>A part of a line of a paragraph in one style: the text of a run, or of a word of it when the line is justified.</summary>
internal readonly record struct FlowFragment(string Text, PdfFont Font, double Size, PdfColor Color, Uri? Link, int RunIndex, double Width);
