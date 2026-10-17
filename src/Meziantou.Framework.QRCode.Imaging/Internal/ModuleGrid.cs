namespace Meziantou.Framework.Internal;

/// <summary>
/// A grid of modules and its layout in pixels.
/// </summary>
/// <param name="Columns">The number of module columns.</param>
/// <param name="Rows">The number of module rows.</param>
/// <param name="IsDark">Returns whether the module at a row and a column is dark.</param>
/// <param name="ModuleWidth">The width of a module, in pixels.</param>
/// <param name="ModuleHeight">The height of a module, in pixels.</param>
/// <param name="HorizontalQuietZoneModules">The number of quiet zone modules on the left and on the right.</param>
/// <param name="VerticalQuietZoneModules">The number of quiet zone modules above and below.</param>
internal readonly record struct ModuleGrid(
    int Columns,
    int Rows,
    Func<int, int, bool> IsDark,
    int ModuleWidth,
    int ModuleHeight,
    int HorizontalQuietZoneModules,
    int VerticalQuietZoneModules);
