using System.Runtime.InteropServices;
using Meziantou.Framework.VectorGraphics;

namespace Meziantou.Framework.Barcodes.Internal;

/// <summary>What the scene of a symbol paints.</summary>
/// <param name="Modules">The modules, by row then column; <see langword="true"/> for dark.</param>
/// <param name="ModuleWidth">The width of a module, already validated and snapped.</param>
/// <param name="ModuleHeight">The height of a module, already validated and snapped.</param>
/// <param name="HorizontalQuietZoneModules">The number of light modules on the left and right sides.</param>
/// <param name="VerticalQuietZoneModules">The number of light modules on the top and bottom sides.</param>
/// <param name="DarkPaint">The paint of the dark modules.</param>
/// <param name="LightPaint">The paint of the background, or <see langword="null"/> for none.</param>
/// <param name="SnapToPixels">Whether the positions are rounded to whole units.</param>
/// <param name="Text">The style of the text under the symbol, or <see langword="null"/> for no text.</param>
/// <param name="TextValue">The text under the symbol.</param>
/// <param name="Logo">The logo over the center of the symbol, already validated and placed, or <see langword="null"/>.</param>
/// <param name="SizeParameterName">The option to blame when the drawing is too large.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct SymbolSceneContent(
    bool[,] Modules,
    double ModuleWidth,
    double ModuleHeight,
    int HorizontalQuietZoneModules,
    int VerticalQuietZoneModules,
    ScenePaint DarkPaint,
    ScenePaint? LightPaint,
    bool SnapToPixels,
    BarcodeTextOptions? Text,
    string? TextValue,
    SymbolLogo? Logo,
    string SizeParameterName);
