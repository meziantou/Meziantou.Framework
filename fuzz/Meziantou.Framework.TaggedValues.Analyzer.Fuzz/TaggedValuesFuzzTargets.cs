using System.Globalization;
using System.Text.RegularExpressions;
using Meziantou.Framework.Fuzzing;
using Microsoft.CodeAnalysis.Text;

namespace Meziantou.Framework.TaggedValues.Analyzer.Fuzz;

/// <summary>
/// <see cref="ValueTagComment.FindKeyword"/> returns the positions of the <c>ValueTag</c> keyword that a search of the whole string finds,
/// whatever the length of the text, so the overlapping chunks it reads neither lose nor duplicate a keyword. <see cref="ValueTagComment.Parse"/>
/// only recognizes a comment that contains the keyword, which lets the analyzer skip the files and the declarations without it; a valid comment
/// declares tags, an invalid one does not, and the comment <see cref="ValueTagComment.Format"/> writes for the tags parses back to the same tags.
/// </summary>
public static partial class TaggedValuesFuzzTargets
{
    private const string Keyword = "ValueTag";

    // FindKeyword reads the text in chunks of 4096 characters: the text is repeated to span several of them
    private const int MaxRepeatedLength = 64 * 1024;

    private static readonly string[] Tokens =
    [
        "ValueTag", "ValueTag=", "ValueTag ", "/*", "*/", "//", "///", "=", ",", ", ", " ", "\t", "\n", "\r\n", "Key=", "Value=", "Key", "Value",
        "OrderId", "ProjectId", "ValueTa", "alueTag", "ValueTagged", "*", "/",
    ];

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("taggedvalues-find-keyword", FindKeyword) { Seeds = GetTextSeeds, Dictionary = Tokens },
        new("taggedvalues-comment", ParseComment) { Seeds = GetCommentSeeds, Dictionary = Tokens },
    ];

    /// <summary>The input is a repeat count byte, then a text.</summary>
    private static void FindKeyword(ReadOnlySpan<byte> data)
    {
        var repeat = 1 + FuzzInput.ReadByte(ref data);
        var fragment = FuzzText.Decode(data);
        if (fragment.Length > 0)
        {
            repeat = Math.Min(repeat, Math.Max(1, MaxRepeatedLength / fragment.Length));
        }

        var text = string.Concat(Enumerable.Repeat(fragment, repeat));
        var expected = new List<int>();
        for (var index = text.IndexOf(Keyword, StringComparison.Ordinal); index >= 0; index = text.IndexOf(Keyword, index + 1, StringComparison.Ordinal))
        {
            expected.Add(index);
        }

        var actual = ValueTagComment.FindKeyword(SourceText.From(text));
        FuzzAssert.Equal(string.Join(",", expected), string.Join(",", actual), "The positions of the keyword");
    }

    /// <summary>The input is the text of a comment.</summary>
    private static void ParseComment(ReadOnlySpan<byte> data)
    {
        var comment = FuzzText.Decode(data);
        var kind = ValueTagComment.Parse(comment, out var tags);
        if (kind is ValueTagCommentKind.NotValueTag)
        {
            FuzzAssert.True(tags.IsEmpty, "A comment that is not a value tag comment declares no tag");
            return;
        }

        // The analyzer only reads the comments at the positions of the keyword
        FuzzAssert.True(comment.Contains(Keyword, StringComparison.Ordinal), "A value tag comment contains the keyword");
        FuzzAssert.True(ValueTagComment.FindKeyword(SourceText.From(comment)).Length > 0, "FindKeyword finds the keyword of a value tag comment");

        if (kind is ValueTagCommentKind.Invalid)
        {
            FuzzAssert.True(tags.IsEmpty, "An invalid comment declares no tag");
            return;
        }

        FuzzAssert.Equal(ValueTagCommentKind.Valid, kind, "The kind of the comment");
        FuzzAssert.False(tags.IsEmpty, "A valid comment declares tags");
        foreach (var isLineComment in (bool[])[false, true])
        {
            if (!ValueTagComment.CanFormat(tags, isLineComment))
                continue;

            var formatted = ValueTagComment.Format(tags, isLineComment);
            FuzzAssert.Equal(ValueTagCommentKind.Valid, ValueTagComment.Parse(formatted, out var parsed), "The kind of the formatted comment " + FuzzAssert.Format(formatted));
            FuzzAssert.True(parsed.TagsEqual(tags), "The formatted comment " + FuzzAssert.Format(formatted) + " declares the tags of " + FuzzAssert.Format(comment));
        }
    }

    private static IEnumerable<FuzzSeed> GetTextSeeds()
    {
        var index = 0;
        foreach (var source in GetSources())
        {
            yield return FuzzSeed.FromText(string.Create(CultureInfo.InvariantCulture, $"source-{index}"), [0], source);
            index++;
        }

        foreach (var comment in GetComments())
        {
            // Repeated enough to span several chunks of the search
            yield return FuzzSeed.FromText(string.Create(CultureInfo.InvariantCulture, $"repeated-{index}"), [255], comment);
            index++;
        }
    }

    private static IEnumerable<FuzzSeed> GetCommentSeeds()
    {
        var index = 0;
        foreach (var comment in GetComments())
        {
            yield return FuzzSeed.FromText(string.Create(CultureInfo.InvariantCulture, $"comment-{index}"), comment);
            index++;
        }
    }

    private static IEnumerable<string> GetSources()
    {
        yield return FuzzResources.ReadText(typeof(TaggedValuesFuzzTargets).Assembly, "LocalTagTests.cs");
        yield return FuzzResources.ReadText(typeof(TaggedValuesFuzzTargets).Assembly, "InvalidAnnotationRuleTests.cs");
    }

    /// <summary>Gets the comments of the unit tests: the comments the parser tests read, and the comments of the code the analyzer tests analyze.</summary>
    private static IEnumerable<string> GetComments()
    {
        var comments = new List<string>();
        foreach (var source in GetSources())
        {
            comments.AddRange(InlineDataRegex().Matches(source).Select(match => Regex.Unescape(match.Groups["value"].Value)));
            comments.AddRange(BlockCommentRegex().Matches(source).Select(match => match.Value));
            comments.AddRange(LineCommentRegex().Matches(source).Select(match => match.Value.TrimEnd()));
        }

        return comments.Where(comment => comment.Contains(Keyword, StringComparison.Ordinal)).Distinct(StringComparer.Ordinal);
    }

    [GeneratedRegex(@"InlineData\(""(?<value>(?:[^""\\]|\\.)*)""", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 10_000)]
    private static partial Regex InlineDataRegex();

    [GeneratedRegex(@"/\*(?:[^*\r\n]|\*(?!/))*\*/", RegexOptions.None, matchTimeoutMilliseconds: 10_000)]
    private static partial Regex BlockCommentRegex();

    [GeneratedRegex(@"//[^\r\n|""]*", RegexOptions.None, matchTimeoutMilliseconds: 10_000)]
    private static partial Regex LineCommentRegex();
}
