using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Imaging.Typography;

namespace Meziantou.Framework.Imaging.Barcodes.Internal;

/// <summary>What a drawing paints.</summary>
internal sealed record ModuleCanvasContent(
    int Columns,
    int Rows,
    Func<int, int, bool> IsDark,
    double ModuleWidth,
    double ModuleHeight,
    int HorizontalQuietZoneModules,
    int VerticalQuietZoneModules,
    Brush DarkBrush,
    Brush? LightBrush,
    TextLayout? Text,
    Brush? TextBrush,
    bool SnapToPixels);
