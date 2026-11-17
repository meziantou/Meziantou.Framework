namespace Meziantou.Framework.Barcodes.Internal.Linear;

/// <summary>Where a group of digits of an EAN or UPC symbol is printed.</summary>
internal enum EanUpcTextPosition
{
    /// <summary>Under the bars, centered between two guards.</summary>
    Below,

    /// <summary>Left of the start guard, in the left quiet zone.</summary>
    Left,

    /// <summary>Right of the end guard, in the right quiet zone or in the gap before the add-on.</summary>
    Right,

    /// <summary>Above the bars of the add-on, which start lower.</summary>
    AboveAddOn,
}
