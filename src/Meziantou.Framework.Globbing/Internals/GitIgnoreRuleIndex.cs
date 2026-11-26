namespace Meziantou.Framework.Globbing.Internals;

/// <summary>
///     Finds the last gitignore rule that matches a path without evaluating every rule. Most entries of a
///     <c>.gitignore</c> file are a name (<c>node_modules</c>, <c>[Bb]in/</c>), an extension (<c>*.log</c>,
///     <c>*.[Cc]ache</c>) or a prefix (<c>_ReSharper*/</c>), and such a rule can only match a path whose last segment
///     has one of those names, one of those extensions, or one of those first characters. Those rules are grouped by
///     name, by extension and by first character, so only the rules that can match are evaluated, still from the last
///     one to the first.
/// </summary>
internal sealed class GitIgnoreRuleIndex
{
    // A rule whose last segment matches a few names, such as '[Dd]ebug', is indexed by each of them. Beyond this many
    // names, it is indexed by its first character instead.
    private const int MaxExpandedNameCount = 16;

    private readonly Glob[] _rules;
    private readonly bool[] _directoryOnly;
    private readonly int[] _otherRules;
    private readonly Dictionary<string, int[]>.AlternateLookup<ReadOnlySpan<char>> _rulesByName;
    private readonly Dictionary<string, int[]>.AlternateLookup<ReadOnlySpan<char>> _rulesByExtension;
    private readonly Dictionary<char, int[]> _rulesByFirstCharacter;

    public GitIgnoreRuleIndex(Glob[] rules)
    {
        _rules = rules;
        _directoryOnly = new bool[rules.Length];

        var otherRules = new List<int>();
        var rulesByName = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        var rulesByExtension = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        var rulesByFirstCharacter = new Dictionary<char, List<int>>();
        for (var i = 0; i < rules.Length; i++)
        {
            var (kind, keys, directoryOnly) = Classify(rules[i]);
            _directoryOnly[i] = directoryOnly;
            switch (kind)
            {
                case RuleKind.Name:
                    foreach (var key in keys!)
                    {
                        Add(rulesByName, key, i);
                    }

                    break;

                case RuleKind.Extension:
                    foreach (var key in keys!)
                    {
                        Add(rulesByExtension, key, i);
                    }

                    break;

                case RuleKind.FirstCharacter:
                    foreach (var key in keys!)
                    {
                        Add(rulesByFirstCharacter, key[0], i);
                    }

                    break;

                default:
                    otherRules.Add(i);
                    break;
            }
        }

        _otherRules = [.. otherRules];
        _rulesByName = ToArrays(rulesByName, StringComparer.Ordinal).GetAlternateLookup<ReadOnlySpan<char>>();
        _rulesByExtension = ToArrays(rulesByExtension, StringComparer.Ordinal).GetAlternateLookup<ReadOnlySpan<char>>();
        _rulesByFirstCharacter = ToArrays(rulesByFirstCharacter, comparer: null);

        static void Add<TKey>(Dictionary<TKey, List<int>> dictionary, TKey key, int index)
            where TKey : notnull
        {
            if (!dictionary.TryGetValue(key, out var list))
            {
                list = [];
                dictionary.Add(key, list);
            }

            // A rule may give the same key twice, such as '[aa]', and must be evaluated once
            if (list.Count == 0 || list[^1] != index)
            {
                list.Add(index);
            }
        }

        static Dictionary<TKey, int[]> ToArrays<TKey>(Dictionary<TKey, List<int>> dictionary, IEqualityComparer<TKey>? comparer)
            where TKey : notnull
        {
            var result = new Dictionary<TKey, int[]>(dictionary.Count, comparer);
            foreach (var (key, value) in dictionary)
            {
                result.Add(key, [.. value]);
            }

            return result;
        }
    }

    /// <summary>Returns the mode of the last rule that matches the path, or <see langword="null"/> when no rule matches it.</summary>
    public GlobMode? GetLastMatch(ReadOnlySpan<char> directory, ReadOnlySpan<char> filename, PathItemType? itemType)
    {
        // Read the path as the rules read it, so the last segment and the kind of item are the ones they see
        var pathReader = new PathReader(directory, filename, itemType);
        var isDirectory = pathReader.IsDirectory;
        pathReader.ConsumeToLastSegment();
        var name = pathReader.CurrentText;

        ReadOnlySpan<int> nameRules = _rulesByName.TryGetValue(name, out var byName) ? byName : [];
        ReadOnlySpan<int> extensionRules = [];
        var dotIndex = name.LastIndexOf('.');
        if (dotIndex >= 0 && _rulesByExtension.TryGetValue(name[(dotIndex + 1)..], out var byExtension))
        {
            extensionRules = byExtension;
        }

        ReadOnlySpan<int> firstCharacterRules = [];
        if (!name.IsEmpty && _rulesByFirstCharacter.TryGetValue(name[0], out var byFirstCharacter))
        {
            firstCharacterRules = byFirstCharacter;
        }

        ReadOnlySpan<int> otherRules = _otherRules;

        // The lists are sorted, so merge them from the end to evaluate the rules from the last one
        var otherIndex = otherRules.Length - 1;
        var nameIndex = nameRules.Length - 1;
        var extensionIndex = extensionRules.Length - 1;
        var firstCharacterIndex = firstCharacterRules.Length - 1;
        while (true)
        {
            var ruleIndex = -1;
            var source = 0;
            if (otherIndex >= 0)
            {
                ruleIndex = otherRules[otherIndex];
                source = 1;
            }

            if (nameIndex >= 0 && nameRules[nameIndex] > ruleIndex)
            {
                ruleIndex = nameRules[nameIndex];
                source = 2;
            }

            if (extensionIndex >= 0 && extensionRules[extensionIndex] > ruleIndex)
            {
                ruleIndex = extensionRules[extensionIndex];
                source = 3;
            }

            if (firstCharacterIndex >= 0 && firstCharacterRules[firstCharacterIndex] > ruleIndex)
            {
                ruleIndex = firstCharacterRules[firstCharacterIndex];
                source = 4;
            }

            switch (source)
            {
                case 0:
                    return null;
                case 1:
                    otherIndex--;
                    break;
                case 2:
                    nameIndex--;
                    break;
                case 3:
                    extensionIndex--;
                    break;
                default:
                    firstCharacterIndex--;
                    break;
            }

            // A rule ending with a '/' only matches a directory
            if (_directoryOnly[ruleIndex] && !isDirectory)
                continue;

            var rule = _rules[ruleIndex];
            if (rule.IsMatchCore(directory, filename, itemType))
                return rule.Mode;
        }
    }

    private static (RuleKind Kind, IEnumerable<string>? Keys, bool DirectoryOnly) Classify(Glob rule)
    {
        ReadOnlySpan<Segment> segments = rule._segments;

        // A gitignore entry ending with a '/' ends with a segment that only matches the directory at the end of the path
        var directoryOnly = false;
        if (segments is [.., DirectoryContentSegment directoryContent])
        {
            if (!ReferenceEquals(directoryContent, DirectoryContentSegment.DirectoryOnly))
                return (RuleKind.Other, null, false);

            directoryOnly = true;
            segments = segments[..^1];
        }

        if (segments.IsEmpty)
            return (RuleKind.Other, null, directoryOnly);

        // The last segment matches the last segment of the path as a whole
        var (kind, keys) = ClassifyLastSegment(segments[^1]);
        return (kind, keys, directoryOnly);
    }

    private static (RuleKind Kind, IEnumerable<string>? Keys) ClassifyLastSegment(Segment segment)
    {
        switch (segment)
        {
            case LastSegment last:
                return ClassifyLastSegment(last.InnerSegment);

            case LiteralSegment { IgnoreCase: false } literal:
                return (RuleKind.Name, [literal.Value]);

            case PathSuffixSegment { IgnoreCase: false, Segments: [.., var name] }:
                return (RuleKind.Name, [name]);

            case EndsWithSegment { IgnoreCase: false } endsWith when TryGetExtension(endsWith.Value, out var extension):
                return (RuleKind.Extension, [extension]);

            case MatchAllEndsWithSegment { IgnoreCase: false } endsWith when TryGetExtension(endsWith.Suffix, out var extension):
                return (RuleKind.Extension, [extension]);

            case StartsWithSegment { IgnoreCase: false, Value.Length: > 0 } startsWith:
                return (RuleKind.FirstCharacter, [startsWith.Value[..1]]);

            case RaggedSegment ragged when !ragged.HasAlternation:
            {
                var subSegments = ragged._segments.AsSpan();

                // A segment made of literals and brackets only matches a few names, such as '[Dd]ebug'
                var names = Expand(subSegments, endsWithFirst: false);
                if (names is not null)
                    return (RuleKind.Name, names);

                // A segment that ends with a dot followed by literals and brackets, such as '*.[Cc]ache', only
                // matches the names that have one of a few extensions
                var tailStart = subSegments.Length;
                while (tailStart > 0 && IsFixedLength(subSegments[tailStart - 1]))
                {
                    tailStart--;
                }

                var tail = subSegments[tailStart..];
                if (tailStart > 0 && subSegments[tailStart - 1] is EndsWithSegment { IgnoreCase: false } endsWith)
                {
                    tail = subSegments[(tailStart - 1)..];
                }

                var tails = Expand(tail, endsWithFirst: true);
                if (tails is not null && TryGetExtensions(tails, out var extensions))
                    return (RuleKind.Extension, extensions);

                // The segment rejects a name that does not start with one of these characters, such as
                // '[Tt]est[Rr]esult*'
                if (ragged.FirstRequiredCharacters is { Length: > 0 } firstCharacters)
                    return (RuleKind.FirstCharacter, firstCharacters.Select(c => c.ToString()));

                break;
            }
        }

        return (RuleKind.Other, null);

        static bool IsFixedLength(Segment segment)
        {
            return segment is LiteralSegment { IgnoreCase: false } or CharacterSetSegment { IgnoreCase: false } or CharacterRangeSegment { Range.Length: <= MaxExpandedNameCount };
        }

        // Every text that the subsegments match, or null when they hold a wildcard or match too many texts. With
        // endsWithFirst, the first subsegment may be a '*' followed by a literal: the texts are then the ends of the
        // texts that the subsegments match, which is all an extension needs.
        static List<string>? Expand(ReadOnlySpan<Segment> subSegments, bool endsWithFirst)
        {
            if (subSegments.IsEmpty)
                return null;

            var result = new List<string> { "" };
            foreach (var subSegment in subSegments)
            {
                ReadOnlySpan<char> characters;
                switch (subSegment)
                {
                    case LiteralSegment { IgnoreCase: false } literal:
                        for (var i = 0; i < result.Count; i++)
                        {
                            result[i] += literal.Value;
                        }

                        continue;

                    case EndsWithSegment { IgnoreCase: false } endsWith when endsWithFirst && ReferenceEquals(subSegment, subSegments[0]):
                        for (var i = 0; i < result.Count; i++)
                        {
                            result[i] += endsWith.Value;
                        }

                        continue;

                    case CharacterSetSegment { IgnoreCase: false } set:
                        characters = set.Set;
                        break;

                    case CharacterRangeSegment range when range.Range.Length <= MaxExpandedNameCount:
                        characters = range.Range.EnumerateCharacters();
                        break;

                    default:
                        return null;
                }

                var distinct = new HashSet<char>(characters.ToArray());
                if (distinct.Count == 0 || result.Count * distinct.Count > MaxExpandedNameCount)
                    return null;

                var expanded = new List<string>(result.Count * distinct.Count);
                foreach (var prefix in result)
                {
                    foreach (var c in distinct)
                    {
                        expanded.Add(prefix + c);
                    }
                }

                result = expanded;
            }

            return result;
        }

        // The extension of a name that ends with each text, when each of them holds a dot
        static bool TryGetExtensions(List<string> suffixes, [NotNullWhen(true)] out List<string>? extensions)
        {
            extensions = new List<string>(suffixes.Count);
            foreach (var suffix in suffixes)
            {
                if (!TryGetExtension(suffix, out var extension))
                {
                    extensions = null;
                    return false;
                }

                extensions.Add(extension);
            }

            return true;
        }
    }

    // A name that ends with a text holding a dot has the extension that follows the last dot of this text. A text
    // holding a separator can end a path that spans several segments, so it does not tell the extension of the name.
    private static bool TryGetExtension(string suffix, [NotNullWhen(true)] out string? extension)
    {
        var dotIndex = suffix.AsSpan().LastIndexOf('.');
        if (dotIndex >= 0 && suffix.AsSpan().IndexOfAny(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) < 0)
        {
            extension = suffix[(dotIndex + 1)..];
            return true;
        }

        extension = null;
        return false;
    }

    private enum RuleKind
    {
        Other,
        Name,
        Extension,
        FirstCharacter,
    }
}
