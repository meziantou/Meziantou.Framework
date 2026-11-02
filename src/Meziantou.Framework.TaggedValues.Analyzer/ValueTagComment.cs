namespace Meziantou.Framework.TaggedValues.Analyzer;

/// <summary>
/// Parses the comments that tag local variables, which cannot have attributes.
/// </summary>
/// <remarks>
/// Supported forms: <c>/* ValueTag=OrderId */</c>, <c>/* ValueTag=OrderId, ProjectId */</c> for a union, and
/// <c>/* ValueTag Key=OrderId Value=ProjectId */</c> for a dictionary. Each form can also be written as a line comment,
/// e.g. <c>// ValueTag=OrderId</c>.
/// </remarks>
internal static class ValueTagComment
{
    private const string Keyword = "ValueTag";

    public static ValueTagCommentKind Parse(string commentText, out TagInfo tagInfo)
    {
        tagInfo = TagInfo.None;

        // Most comments are not annotations: find the content and check the keyword by index, so they allocate nothing
        int start;
        int end;
        if (commentText.Length >= 4 && commentText.StartsWith("/*", StringComparison.Ordinal) && commentText.EndsWith("*/", StringComparison.Ordinal))
        {
            start = 2;
            end = commentText.Length - 2;
        }
        else if (commentText.StartsWith("//", StringComparison.Ordinal))
        {
            start = 2;
            end = commentText.Length;
        }
        else
        {
            return ValueTagCommentKind.NotValueTag;
        }

        while (start < end && char.IsWhiteSpace(commentText[start]))
        {
            start++;
        }

        while (end > start && char.IsWhiteSpace(commentText[end - 1]))
        {
            end--;
        }

        if (end - start < Keyword.Length || string.CompareOrdinal(commentText, start, Keyword, 0, Keyword.Length) is not 0)
            return ValueTagCommentKind.NotValueTag;

        start += Keyword.Length;
        if (start < end && (char.IsLetterOrDigit(commentText[start]) || commentText[start] is '_'))
            return ValueTagCommentKind.NotValueTag;

        var rest = commentText.Substring(start, end - start).Trim();

        // Prose such as "// ValueTag is not used here" is not an annotation, but "/* ValueTag OrderId */" is a malformed one
        if (rest.IndexOf("=", StringComparison.Ordinal) < 0 && rest.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length > 1)
            return ValueTagCommentKind.NotValueTag;

        if (rest.StartsWith("=", StringComparison.Ordinal))
        {
            var tags = new List<string>();
            foreach (var part in rest.Substring(1).Split(','))
            {
                var tag = part.Trim();
                if (!IsValidTag(tag))
                    return ValueTagCommentKind.Invalid;

                tags.Add(tag);
            }

            tagInfo = TagInfo.Create(tags, isExplicit: true);
            return ValueTagCommentKind.Valid;
        }

        string? key = null;
        string? value = null;
        var index = 0;
        while (true)
        {
            while (index < rest.Length && (char.IsWhiteSpace(rest[index]) || rest[index] is ','))
            {
                index++;
            }

            if (index >= rest.Length)
                break;

            var nameStart = index;
            while (index < rest.Length && char.IsLetter(rest[index]))
            {
                index++;
            }

            var name = rest.Substring(nameStart, index - nameStart);
            while (index < rest.Length && char.IsWhiteSpace(rest[index]))
            {
                index++;
            }

            if (index >= rest.Length || rest[index] is not '=')
                return ValueTagCommentKind.Invalid;

            index++;
            while (index < rest.Length && char.IsWhiteSpace(rest[index]))
            {
                index++;
            }

            var tagStart = index;
            while (index < rest.Length && !char.IsWhiteSpace(rest[index]) && rest[index] is not ',')
            {
                index++;
            }

            var tag = rest.Substring(tagStart, index - tagStart);
            if (!IsValidTag(tag))
                return ValueTagCommentKind.Invalid;

            switch (name)
            {
                case "Key" when key is null:
                    key = tag;
                    break;

                case "Value" when value is null:
                    value = tag;
                    break;

                default:
                    return ValueTagCommentKind.Invalid;
            }
        }

        if (key is null && value is null)
            return ValueTagCommentKind.Invalid;

        tagInfo = TagInfo.Create([], key is null ? [] : [key], value is null ? [] : [value], isExplicit: true);
        return ValueTagCommentKind.Valid;
    }

    /// <summary>
    /// Returns whether <see cref="Format"/> can write the tags as a comment that parses back to the same tags:
    /// every tag is valid in a comment, and a dictionary has at most one key tag and one value tag.
    /// </summary>
    public static bool CanFormat(TagInfo tagInfo, bool isLineComment = false)
    {
        if (tagInfo.HasKeyOrValue)
            return tagInfo.Tags.IsEmpty && tagInfo.Key.Length <= 1 && tagInfo.Value.Length <= 1 && tagInfo.Key.All(CanWrite) && tagInfo.Value.All(CanWrite);

        return !tagInfo.Tags.IsEmpty && tagInfo.Tags.All(CanWrite);

        // A tag that contains */ would end a block comment
        bool CanWrite(string tag) => IsValidTag(tag) && (isLineComment || tag.IndexOf("*/", StringComparison.Ordinal) < 0);
    }

    /// <summary>
    /// Formats tags as the comment that declares them on a local variable.
    /// </summary>
    public static string Format(TagInfo tagInfo, bool isLineComment = false)
    {
        var content = FormatContent(tagInfo);
        return isLineComment ? "// " + content : "/* " + content + " */";
    }

    private static string FormatContent(TagInfo tagInfo)
    {
        if (!tagInfo.HasKeyOrValue)
            return "ValueTag=" + string.Join(", ", tagInfo.Tags);

        var parts = new List<string>();
        if (!tagInfo.Key.IsEmpty)
        {
            parts.Add("Key=" + tagInfo.Key[0]);
        }

        if (!tagInfo.Value.IsEmpty)
        {
            parts.Add("Value=" + tagInfo.Value[0]);
        }

        return "ValueTag " + string.Join(" ", parts);
    }

    private static bool IsValidTag(string tag)
    {
        if (tag.Length is 0)
            return false;

        foreach (var c in tag)
        {
            if (char.IsWhiteSpace(c) || c is '=' or ',')
                return false;
        }

        return true;
    }
}
