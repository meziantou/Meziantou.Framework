using System.Buffers;
using System.Text;

namespace Meziantou.Framework.Markdown.Parsing;

/// <summary>The Unicode full case folding that CommonMark uses to match the labels of the links, as cmark does.</summary>
internal static partial class CaseFolding
{
    /// <summary>Appends the full case folding of a text, such as <c>ss</c> for <c>ẞ</c> or <c>ff</c> for <c>ﬀ</c>.</summary>
    /// <remarks>A lone surrogate is appended as is.</remarks>
    public static void AppendFolded(StringBuilder builder, ReadOnlySpan<char> value)
    {
        while (!value.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(value, out var rune, out var charsConsumed) is OperationStatus.Done)
            {
                if (CodePointToFolded.TryGetValue(rune.Value, out var folded))
                {
                    builder.Append(folded);
                }
                else
                {
                    builder.Append(value[..charsConsumed]);
                }
            }
            else
            {
                builder.Append(value[0]);
                charsConsumed = 1;
            }

            value = value[charsConsumed..];
        }
    }
}
