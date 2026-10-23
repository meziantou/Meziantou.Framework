namespace Meziantou.Framework.Pdf;

/// <summary>The properties of an outline item that were changed since it was read: only they are written back.</summary>
[Flags]
internal enum OutlineItemChanges
{
    None = 0,
    Title = 1,
    Destination = 2,
    Count = 4,
    Style = 8,
    All = Title | Destination | Count | Style,
}
