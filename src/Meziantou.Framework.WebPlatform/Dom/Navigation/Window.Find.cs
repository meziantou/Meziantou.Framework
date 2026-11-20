using System.Diagnostics.CodeAnalysis;

namespace Meziantou.Framework.WebPlatform.Dom;

// window.find() (find-in-page), which the browsers implement but no specification defines: it searches a text in the
// rendered text of the document, from the selection, and selects the match.
public partial class Window
{
    // The last match of find() that the selection cannot hold (a match in a shadow tree): the next search starts after it
    // while the selection is empty
    private (Text StartNode, uint StartOffset, Text EndNode, uint EndOffset)? _findMatch;

    /// <summary>
    /// Searches a text in the document, as the find-in-page feature of the browsers (non-standard, implemented by the
    /// browsers): the text of the nodes that are rendered and not inert is searched from the end of the selection (from its
    /// start when <paramref name="backwards"/>), or from the start of the document (from its end) when nothing is selected.
    /// The white space is collapsed, and a match does not span blocks. When a match is found, it is selected and true is
    /// returned.
    /// </summary>
    /// <param name="text">The text to search. The empty string is never found.</param>
    /// <param name="caseSensitive">Whether the case of the letters must match.</param>
    /// <param name="backwards">Whether to search the previous match.</param>
    /// <param name="wrapAround">Whether to continue from the other end of the document when there is no match before it.</param>
    /// <param name="wholeWord">Whether the match must start and end at word boundaries.</param>
    /// <param name="searchInFrames">Ignored: the documents of the child navigables are not searched.</param>
    /// <param name="showDialog">Ignored: there is no find dialog.</param>
    /// <returns>Whether the text was found.</returns>
    /// <seealso href="https://developer.mozilla.org/docs/Web/API/Window/find#syntax"/>
    [Experimental(ExperimentalApis.WindowFind, UrlFormat = ExperimentalApis.UrlFormat)]
    [IdlMember("find", "https://developer.mozilla.org/docs/Web/API/Window/find#syntax")]
    public bool Find(string text = "", bool caseSensitive = false, bool backwards = false, bool wrapAround = false, bool wholeWord = false, bool searchInFrames = false, bool showDialog = false)
    {
        ArgumentNullException.ThrowIfNull(text);
        var document = Document;
        if (text.Length == 0 || !document.IsFullyActive || document.DocumentElement is null || document.GetSelection() is not { } selection)
            return false;

        var searchable = TextDirectives.SearchableText.Create(document, skipInert: true);

        // The current match: the selection, else the last match that the selection could not hold
        int start;
        int end;
        if (selection.RangeOrNull is { } range)
        {
            _findMatch = null;
            start = searchable.GetIndexBefore(range.StartContainer, range.StartOffset);
            end = searchable.GetIndexAfter(range.EndContainer, range.EndOffset);
        }
        else if (_findMatch is { } match && match.StartNode.IsConnected && match.EndNode.IsConnected && ReferenceEquals(match.StartNode.NodeDocument, document))
        {
            start = searchable.GetIndexBefore(match.StartNode, match.StartOffset);
            end = searchable.GetIndexAfter(match.EndNode, match.EndOffset);
        }
        else
        {
            start = backwards ? searchable.Length : 0;
            end = backwards ? searchable.Length : 0;
        }

        var found = backwards
            ? searchable.FindInRange(text, 0, start, caseSensitive, wholeWord, backwards: true)
            : searchable.FindInRange(text, end, searchable.Length, caseSensitive, wholeWord, backwards: false);
        if (found is null && wrapAround)
        {
            found = backwards
                ? searchable.FindInRange(text, start, searchable.Length, caseSensitive, wholeWord, backwards: true)
                : searchable.FindInRange(text, 0, end, caseSensitive, wholeWord, backwards: false);
        }

        if (found is not var (matchStart, matchEnd))
            return false;

        var (startNode, startOffset) = searchable.GetPosition(matchStart, isEnd: false);
        var (endNode, endOffset) = searchable.GetPosition(matchEnd, isEnd: true);

        // The selection of the document only holds the ranges of the document tree
        if (ReferenceEquals(startNode.GetRoot(), document) && ReferenceEquals(endNode.GetRoot(), document))
        {
            _findMatch = null;
            selection.SelectDirectionless(startNode, (uint)startOffset, endNode, (uint)endOffset);
        }
        else
        {
            selection.RemoveAllRanges();
            _findMatch = (startNode, (uint)startOffset, endNode, (uint)endOffset);
        }

        return true;
    }
}
