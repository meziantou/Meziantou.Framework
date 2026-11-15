namespace Meziantou.Framework.Barcodes.Internal.Linear;

/// <summary>
/// The human-readable layout of an EAN or UPC symbol (GS1 General Specifications): the bars that extend below the data bars
/// (the guards, and the first and last characters of UPC-A), and the groups of digits printed between, beside and above them.
/// </summary>
/// <param name="SymbolEnd">The module after the end guard of the main symbol.</param>
/// <param name="ExtendedBars">The ranges of the bars that extend below the data bars.</param>
/// <param name="Groups">The groups of digits.</param>
/// <param name="AddOnStart">The first module of the add-on, or -1 when there is none.</param>
internal sealed record EanUpcLayout(int SymbolEnd, ModuleRange[] ExtendedBars, EanUpcTextGroup[] Groups, int AddOnStart)
{
    public bool HasAddOn => AddOnStart >= 0;
}
