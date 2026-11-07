using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Markdown.Helpers;

/// <summary>
/// Remembers where the searches for the end of inline raw HTML constructs (processing instructions, CDATA sections,
/// comments and declarations) stopped in an inline text, so that an unclosed construct does not make every later
/// construct of the same kind scan to the end of the text again. Without it, inputs such as <c>a&lt;?</c> repeated
/// n times take O(n²) time.
/// </summary>
internal sealed class InlineHtmlScanCache
{
    private readonly Search[] _searches = new Search[(int)HtmlScanTarget.NullCharacter + 1];
    private string? _text;
    private int _textEnd;

    /// <summary>
    /// Forgets everything remembered unless <paramref name="text"/> is the text the previous searches ran on.
    /// </summary>
    public void SetText(StringSlice text)
    {
        // The result of a search only depends on the characters between its start and the end of the text
        if (!ReferenceEquals(_text, text.Text) || _textEnd != text.End)
        {
            Clear();
            _text = text.Text;
            _textEnd = text.End;
        }
    }

    /// <summary>
    /// Forgets everything remembered.
    /// </summary>
    public void Clear()
    {
        _text = null;
        _textEnd = 0;
        Array.Clear(_searches);
    }

    /// <summary>
    /// Finds the first occurrence of <paramref name="target"/> at or after <paramref name="start"/> in <paramref name="text"/>.
    /// </summary>
    /// <param name="text">The text to search, up to its end.</param>
    /// <param name="start">The absolute position in <see cref="StringSlice.Text"/> where the search starts.</param>
    /// <param name="target">The string to search.</param>
    /// <param name="cache">The cache of the previous searches in <paramref name="text"/>, or <see langword="null"/>.</param>
    /// <returns>The absolute position of the occurrence in <see cref="StringSlice.Text"/>, or -1 if there is none before the end of the slice.</returns>
    public static int IndexOf(StringSlice text, int start, HtmlScanTarget target, InlineHtmlScanCache? cache)
    {
        if (cache is null)
        {
            return IndexOf(text, start, target);
        }

        Debug.Assert(ReferenceEquals(cache._text, text.Text) && cache._textEnd == text.End);
        ref var search = ref cache._searches[(int)target];

        // The previous search found no occurrence between its start and its result (or the end of the text), so it
        // answers any search starting in that range.
        if (search.IsValid && start >= search.Start && (search.Result < 0 || start <= search.Result))
        {
            return search.Result;
        }

        var result = IndexOf(text, start, target);
        search = new Search(start, result);
        return result;
    }

    private static int IndexOf(StringSlice text, int start, HtmlScanTarget target)
    {
        if (start > text.End)
        {
            return -1;
        }

        var span = text.Text.AsSpan(start, text.End - start + 1);
        var index = target switch
        {
            HtmlScanTarget.ProcessingInstructionEnd => span.IndexOf("?>", StringComparison.Ordinal),
            HtmlScanTarget.CDataEnd => span.IndexOf("]]>", StringComparison.Ordinal),
            HtmlScanTarget.CommentEnd => span.IndexOf("-->", StringComparison.Ordinal),
            HtmlScanTarget.DeclarationEnd => span.IndexOf('>'),
            _ => span.IndexOf('\0'),
        };

        return index < 0 ? -1 : start + index;
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly struct Search(int start, int result)
    {
        // The start is stored plus one so that the default value is not a valid search
        private readonly int _startPlusOne = start + 1;

        public bool IsValid => _startPlusOne > 0;

        public int Start => _startPlusOne - 1;

        public int Result { get; } = result;
    }
}
