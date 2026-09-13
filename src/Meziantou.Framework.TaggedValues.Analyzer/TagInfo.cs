using System.Collections.Immutable;
using System.Text;

namespace Meziantou.Framework.TaggedValues.Analyzer;

/// <summary>
/// The tags carried by a value.
/// </summary>
/// <remarks>
/// Tags describe the innermost value: for a <c>List&lt;Guid&gt;</c> they describe the elements, for a <c>Task&lt;Guid&gt;</c> the result.
/// <see cref="Key"/> and <see cref="Value"/> describe the keys and the values of a dictionary, or of a <c>KeyValuePair</c>.
/// An empty set means the value is not tagged, which is never reported.
/// </remarks>
internal sealed class TagInfo
{
    public static readonly TagInfo None = new([], [], [], isExplicit: false);

    private TagInfo(ImmutableArray<string> tags, ImmutableArray<string> key, ImmutableArray<string> value, bool isExplicit)
    {
        Tags = tags;
        Key = key;
        Value = value;
        IsExplicit = isExplicit;
    }

    public ImmutableArray<string> Tags { get; }

    public ImmutableArray<string> Key { get; }

    public ImmutableArray<string> Value { get; }

    /// <summary>
    /// Gets a value indicating whether the tags were written by the user, as opposed to inferred from a naming convention.
    /// Code fixes only propagate explicit tags.
    /// </summary>
    public bool IsExplicit { get; }

    public bool IsEmpty => Tags.IsEmpty && Key.IsEmpty && Value.IsEmpty;

    public bool HasKeyOrValue => !Key.IsEmpty || !Value.IsEmpty;

    public static TagInfo Create(IEnumerable<string> tags, bool isExplicit)
    {
        return Create(tags, [], [], isExplicit);
    }

    public static TagInfo Create(IEnumerable<string> tags, IEnumerable<string> key, IEnumerable<string> value, bool isExplicit)
    {
        var result = new TagInfo(Normalize(tags), Normalize(key), Normalize(value), isExplicit);
        return result.IsEmpty ? None : result;
    }

    /// <summary>
    /// Returns the tags of the keys of a dictionary as the tags of a value.
    /// </summary>
    public TagInfo GetKey()
    {
        return Create(Key, IsExplicit);
    }

    /// <summary>
    /// Returns the tags of the values of a dictionary as the tags of a value.
    /// </summary>
    public TagInfo GetValue()
    {
        return Create(Value, IsExplicit);
    }

    public static TagInfo KeyValue(TagInfo key, TagInfo value)
    {
        return Create([], key.Tags, value.Tags, key.IsExplicit || value.IsExplicit);
    }

    /// <summary>
    /// Returns whether two values can be mixed: every part tagged on both sides shares at least one tag.
    /// </summary>
    public static bool AreCompatible(TagInfo left, TagInfo right)
    {
        return AreCompatible(left.Tags, right.Tags) && AreCompatible(left.Key, right.Key) && AreCompatible(left.Value, right.Value);
    }

    private static bool AreCompatible(ImmutableArray<string> left, ImmutableArray<string> right)
    {
        if (left.IsEmpty || right.IsEmpty)
            return true;

        foreach (var tag in left)
        {
            if (right.Contains(tag, StringComparer.Ordinal))
                return true;
        }

        return false;
    }

    public static TagInfo Union(TagInfo left, TagInfo right)
    {
        if (left.IsEmpty)
            return right;

        if (right.IsEmpty)
            return left;

        return Create(left.Tags.Concat(right.Tags), left.Key.Concat(right.Key), left.Value.Concat(right.Value), left.IsExplicit && right.IsExplicit);
    }

    public bool TagsEqual(TagInfo other)
    {
        return Tags.SequenceEqual(other.Tags, StringComparer.Ordinal) && Key.SequenceEqual(other.Key, StringComparer.Ordinal) && Value.SequenceEqual(other.Value, StringComparer.Ordinal);
    }

    /// <summary>
    /// Formats the tags as the attribute to write, e.g. <c>[ValueTag("OrderId")]</c>.
    /// </summary>
    public string ToAttributeString()
    {
        var sb = new StringBuilder("[ValueTag(");
        var first = true;
        foreach (var tag in Tags)
        {
            if (!first)
                sb.Append(", ");

            AppendString(sb, tag);
            first = false;
        }

        AppendNamed(sb, "Key", Key, ref first);
        AppendNamed(sb, "Value", Value, ref first);
        sb.Append(")]");
        return sb.ToString();

        static void AppendNamed(StringBuilder sb, string name, ImmutableArray<string> tags, ref bool first)
        {
            foreach (var tag in tags)
            {
                if (!first)
                    sb.Append(", ");

                sb.Append(name).Append(" = ");
                AppendString(sb, tag);
                first = false;
            }
        }

        static void AppendString(StringBuilder sb, string value)
        {
            sb.Append('"');
            foreach (var c in value)
            {
                if (c is '"' or '\\')
                {
                    sb.Append('\\');
                }

                sb.Append(c);
            }

            sb.Append('"');
        }
    }

    /// <summary>
    /// Serializes the tags so a code fix can rebuild them from the diagnostic properties.
    /// </summary>
    public string Serialize()
    {
        return string.Join("|", Tags) + "\n" + string.Join("|", Key) + "\n" + string.Join("|", Value);
    }

    public static TagInfo Deserialize(string value)
    {
        var parts = value.Split('\n');
        if (parts.Length is not 3)
            return None;

        return Create(parts[0].Split('|'), parts[1].Split('|'), parts[2].Split('|'), isExplicit: true);
    }

    private static ImmutableArray<string> Normalize(IEnumerable<string> tags)
    {
        var builder = ImmutableArray.CreateBuilder<string>();
        foreach (var tag in tags)
        {
            if (string.IsNullOrWhiteSpace(tag))
                continue;

            if (!builder.Contains(tag, StringComparer.Ordinal))
            {
                builder.Add(tag);
            }
        }

        builder.Sort(StringComparer.Ordinal);
        return builder.ToImmutable();
    }
}
