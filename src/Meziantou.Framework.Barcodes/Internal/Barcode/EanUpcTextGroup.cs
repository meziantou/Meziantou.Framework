using System.Runtime.InteropServices;

namespace Meziantou.Framework.Barcodes.Internal.Linear;

/// <summary>A group of digits printed together.</summary>
/// <param name="Text">The digits.</param>
/// <param name="Range">The modules the digits are centered under (or above, for the add-on). Empty for the digits outside the bars.</param>
/// <param name="Position">Where the digits are printed.</param>
/// <param name="IsSmall">Whether the digits are printed smaller than the others, as the number system and check digits of UPC-A and UPC-E are.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct EanUpcTextGroup(string Text, ModuleRange Range, EanUpcTextPosition Position, bool IsSmall = false);
