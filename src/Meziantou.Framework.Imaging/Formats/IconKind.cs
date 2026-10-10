namespace Meziantou.Framework.Imaging.Formats;

/// <summary>Which Windows icon container <see cref="IcoEncoder"/> writes.</summary>
public enum IconKind
{
    /// <summary>Not specified; <see cref="IcoEncoder"/> never uses this value (its default is <see cref="Icon"/>).</summary>
    None = 0,

    /// <summary>An icon (<c>.ico</c>): <see cref="ImageFormat.Ico"/>. Hotspots are not stored.</summary>
    Icon = 1,

    /// <summary>A cursor (<c>.cur</c>): <see cref="ImageFormat.Cur"/>. Every representation stores a hotspot.</summary>
    Cursor = 2,
}
