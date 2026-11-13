namespace Meziantou.Framework.Barcodes.Internal.Linear;

/// <summary>A symbol found on a scan line: it covers the runs from <see cref="Start"/> (a dark run) to <see cref="End"/>, exclusive.</summary>
internal sealed record LinearMatch(BarcodeType Type, string Text, int Start, int End, double ModuleWidth);
