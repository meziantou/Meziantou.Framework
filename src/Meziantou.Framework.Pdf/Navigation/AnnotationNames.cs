using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>The names of the entries of the annotation dictionaries (ISO 32000-2, 12.5).</summary>
internal static class AnnotationNames
{
    public static readonly PdfName AP = PdfName.Create("AP");
    public static readonly PdfName AS = PdfName.Create("AS");
    public static readonly PdfName BE = PdfName.Create("BE");
    public static readonly PdfName BG = PdfName.Create("BG");
    public static readonly PdfName BC = PdfName.Create("BC");
    public static readonly PdfName BS = PdfName.Create("BS");
    public static readonly PdfName CA = PdfName.Create("CA");
    public static readonly PdfName CALower = PdfName.Create("ca");
    public static readonly PdfName Cap = PdfName.Create("Cap");
    public static readonly PdfName CL = PdfName.Create("CL");
    public static readonly PdfName DA = PdfName.Create("DA");
    public static readonly PdfName H = PdfName.Create("H");
    public static readonly PdfName I = PdfName.Create("I");
    public static readonly PdfName IC = PdfName.Create("IC");
    public static readonly PdfName InkList = PdfName.Create("InkList");
    public static readonly PdfName IRT = PdfName.Create("IRT");
    public static readonly PdfName IT = PdfName.Create("IT");
    public static readonly PdfName LE = PdfName.Create("LE");
    public static readonly PdfName LL = PdfName.Create("LL");
    public static readonly PdfName LLE = PdfName.Create("LLE");
    public static readonly PdfName M = PdfName.Create("M");
    public static readonly PdfName MK = PdfName.Create("MK");
    public static readonly PdfName NM = PdfName.Create("NM");
    public static readonly PdfName OC = PdfName.Create("OC");
    public static readonly PdfName Open = PdfName.Create("Open");
    public static readonly PdfName OverlayText = PdfName.Create("OverlayText");
    public static readonly PdfName Popup = PdfName.Create("Popup");
    public static readonly PdfName Q = PdfName.Create("Q");
    public static readonly PdfName QuadPoints = PdfName.Create("QuadPoints");
    public static readonly PdfName R = PdfName.Create("R");
    public static readonly PdfName RC = PdfName.Create("RC");
    public static readonly PdfName RD = PdfName.Create("RD");
    public static readonly PdfName Repeat = PdfName.Create("Repeat");
    public static readonly PdfName RO = PdfName.Create("RO");
    public static readonly PdfName RT = PdfName.Create("RT");
    public static readonly PdfName State = PdfName.Create("State");
    public static readonly PdfName StateModel = PdfName.Create("StateModel");
    public static readonly PdfName Sy = PdfName.Create("Sy");
    public static readonly PdfName Vertices = PdfName.Create("Vertices");
    public static readonly PdfName W = PdfName.Create("W");

    // The subtypes (12.5.6, table 171)
    public static readonly PdfName Text = PdfName.Create("Text");
    public static readonly PdfName FreeText = PdfName.Create("FreeText");
    public static readonly PdfName Line = PdfName.Create("Line");
    public static readonly PdfName Square = PdfName.Create("Square");
    public static readonly PdfName Circle = PdfName.Create("Circle");
    public static readonly PdfName Polygon = PdfName.Create("Polygon");
    public static readonly PdfName PolyLine = PdfName.Create("PolyLine");
    public static readonly PdfName Highlight = PdfName.Create("Highlight");
    public static readonly PdfName Underline = PdfName.Create("Underline");
    public static readonly PdfName Squiggly = PdfName.Create("Squiggly");
    public static readonly PdfName StrikeOut = PdfName.Create("StrikeOut");
    public static readonly PdfName Stamp = PdfName.Create("Stamp");
    public static readonly PdfName Caret = PdfName.Create("Caret");
    public static readonly PdfName Ink = PdfName.Create("Ink");
    public static readonly PdfName PopupSubtype = Popup;
    public static readonly PdfName Redact = PdfName.Create("Redact");
    public static readonly PdfName Widget = PdfName.Create("Widget");
}
