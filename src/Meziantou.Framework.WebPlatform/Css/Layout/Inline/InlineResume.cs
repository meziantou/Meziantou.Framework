using System.Runtime.InteropServices;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// A position in the inline content of a block container where its line boxes start (the start of a line box): the index of an
/// inline item of the content and the offset in its text. A block container laid out from such a position has the line boxes
/// of its content after it only (the pages of another inline size that start between two line boxes, see
/// <see cref="LayoutContext.InlineResumes"/>).
/// </summary>
/// <param name="Item">The index of the inline item.</param>
/// <param name="Position">The offset in the text of the item.</param>
/// <param name="ItemCount">The number of inline items of the content (the position is valid for the same items only).</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct InlineResume(int Item, int Position, int ItemCount);
