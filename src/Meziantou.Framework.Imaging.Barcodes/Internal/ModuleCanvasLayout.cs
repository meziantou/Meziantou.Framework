using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Barcodes.Internal;

/// <summary>Where the parts of a drawing go, relative to its top-left corner.</summary>
/// <param name="Width">The width of the drawing.</param>
/// <param name="Height">The height of the drawing.</param>
/// <param name="SymbolLeft">The left edge of the quiet zone.</param>
/// <param name="SymbolWidth">The width of the symbol, quiet zone included.</param>
/// <param name="SymbolHeight">The height of the symbol, quiet zone included.</param>
/// <param name="TextLeft">The left edge of the text layout.</param>
/// <param name="TextTop">The top edge of the text layout.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct ModuleCanvasLayout(double Width, double Height, double SymbolLeft, double SymbolWidth, double SymbolHeight, double TextLeft, double TextTop);
