using Meziantou.Framework.Diagrams.Layout;

namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>The decorations of the ends of an edge.</summary>
internal enum EdgeMarker
{
    None,
    Arrow,
    OpenTriangle,
    Circle,
    OpenCircle,
    Cross,
    FilledDiamond,
    OpenDiamond,
    OpenArrow,
    ErOnlyOne,
    ErZeroOrOne,
    ErOneOrMore,
    ErZeroOrMore,
    ContainsCircle,
}
