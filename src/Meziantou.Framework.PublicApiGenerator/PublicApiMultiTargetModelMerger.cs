using System.Collections.Immutable;
using System.Text;

namespace Meziantou.Framework.PublicApiGenerator;

internal static class PublicApiMultiTargetModelMerger
{
    private const string Indentation = "    ";

    public static PublicApiModel Merge(IReadOnlyDictionary<string, PublicApiModel> modelsBySymbol)
    {
        ArgumentNullException.ThrowIfNull(modelsBySymbol);

        if (modelsBySymbol.Count == 0)
        {
            throw new ArgumentException("At least one model must be provided.", nameof(modelsBySymbol));
        }

        if (modelsBySymbol.Count == 1)
        {
            return modelsBySymbol.Values.Single();
        }

        var orderedSymbols = modelsBySymbol.Keys.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var typesByQualifiedName = new Dictionary<string, Dictionary<string, PublicApiTypeModel>>(StringComparer.Ordinal);
        foreach (var symbol in orderedSymbols)
        {
            foreach (var type in modelsBySymbol[symbol].Types)
            {
                if (!typesByQualifiedName.TryGetValue(type.QualifiedName, out var typeBySymbol))
                {
                    typeBySymbol = new Dictionary<string, PublicApiTypeModel>(StringComparer.Ordinal);
                    typesByQualifiedName[type.QualifiedName] = typeBySymbol;
                }

                typeBySymbol[symbol] = type;
            }
        }

        var mergedTypes = new List<PublicApiTypeModel>(typesByQualifiedName.Count);
        foreach (var typeGroup in typesByQualifiedName.OrderBy(value => value.Key, StringComparer.Ordinal))
        {
            mergedTypes.Add(MergeType(typeGroup.Value, orderedSymbols));
        }

        return new PublicApiModel([.. mergedTypes]);
    }

    private static PublicApiTypeModel MergeType(IReadOnlyDictionary<string, PublicApiTypeModel> typesBySymbol, string[] orderedSymbols)
    {
        var sampleType = typesBySymbol.Values.First();
        if (AllTypesAreIdentical(typesBySymbol, orderedSymbols))
        {
            return sampleType;
        }

        if (typesBySymbol.Count == orderedSymbols.Length && TryMergeMembers(typesBySymbol, orderedSymbols, out var mergedSource))
        {
            return sampleType with
            {
                Source = mergedSource,
            };
        }

        var conditionalTypeSource = BuildConditionalTypeSource(typesBySymbol, orderedSymbols);
        return sampleType with
        {
            Source = conditionalTypeSource,
        };
    }

    private static bool AllTypesAreIdentical(IReadOnlyDictionary<string, PublicApiTypeModel> typesBySymbol, string[] orderedSymbols)
    {
        if (typesBySymbol.Count != orderedSymbols.Length)
        {
            return false;
        }

        var source = typesBySymbol[orderedSymbols[0]].Source;
        foreach (var symbol in orderedSymbols)
        {
            if (!string.Equals(source, typesBySymbol[symbol].Source, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryMergeMembers(IReadOnlyDictionary<string, PublicApiTypeModel> typesBySymbol, string[] orderedSymbols, out string mergedSource)
    {
        mergedSource = string.Empty;

        var parsedTypes = new Dictionary<string, ParsedTypeSource>(StringComparer.Ordinal);
        foreach (var symbol in orderedSymbols)
        {
            if (!TryParseTypeSource(typesBySymbol[symbol].Source, out var parsedType))
            {
                return false;
            }

            parsedTypes[symbol] = parsedType;
        }

        var firstParsedType = parsedTypes[orderedSymbols[0]];
        foreach (var symbol in orderedSymbols)
        {
            var parsedType = parsedTypes[symbol];
            if (!string.Equals(firstParsedType.Prefix, parsedType.Prefix, StringComparison.Ordinal) ||
                !string.Equals(firstParsedType.Suffix, parsedType.Suffix, StringComparison.Ordinal))
            {
                return false;
            }
        }

        var membersBySymbol = orderedSymbols.ToDictionary(
            symbol => symbol,
            symbol => parsedTypes[symbol].Members,
            StringComparer.Ordinal);

        var sb = new StringBuilder();
        sb.Append(firstParsedType.Prefix);
        var commonAnchors = FindCommonAnchors(membersBySymbol, orderedSymbols);
        var previousIndexesBySymbol = orderedSymbols.ToDictionary(symbol => symbol, _ => -1, StringComparer.Ordinal);
        foreach (var commonAnchor in commonAnchors)
        {
            var segmentBySymbol = new Dictionary<string, ImmutableArray<string>>(StringComparer.Ordinal);
            foreach (var symbol in orderedSymbols)
            {
                var members = membersBySymbol[symbol];
                var startIndex = previousIndexesBySymbol[symbol] + 1;
                var endIndex = commonAnchor.IndexesBySymbol[symbol];
                segmentBySymbol[symbol] = Slice(members, startIndex, endIndex);
            }

            AppendConditionalSegment(sb, segmentBySymbol, orderedSymbols);
            AppendIndentedBlock(sb, commonAnchor.Member, 1);
            foreach (var symbol in orderedSymbols)
            {
                previousIndexesBySymbol[symbol] = commonAnchor.IndexesBySymbol[symbol];
            }
        }

        var trailingSegmentBySymbol = new Dictionary<string, ImmutableArray<string>>(StringComparer.Ordinal);
        foreach (var symbol in orderedSymbols)
        {
            var members = membersBySymbol[symbol];
            var startIndex = previousIndexesBySymbol[symbol] + 1;
            trailingSegmentBySymbol[symbol] = Slice(members, startIndex, members.Length);
        }

        AppendConditionalSegment(sb, trailingSegmentBySymbol, orderedSymbols);
        sb.Append(firstParsedType.Suffix);
        mergedSource = sb.ToString();
        return true;
    }

    private static void AppendConditionalSegment(StringBuilder sb, IReadOnlyDictionary<string, ImmutableArray<string>> segmentBySymbol, string[] orderedSymbols)
    {
        var groups = GroupSegmentBySymbols(segmentBySymbol, orderedSymbols);
        var nonEmptyGroups = groups.Where(group => group.Members.Length > 0).ToArray();
        if (nonEmptyGroups.Length == 0)
        {
            return;
        }

        if (nonEmptyGroups.Length == 1 && nonEmptyGroups[0].Symbols.Count == orderedSymbols.Length)
        {
            AppendMembers(sb, nonEmptyGroups[0].Members);
            return;
        }

        if (nonEmptyGroups.Length == 1)
        {
            AppendConditionalDirective(sb, "#if", nonEmptyGroups[0].Symbols);
            AppendMembers(sb, nonEmptyGroups[0].Members);
            AppendIndentedDirective(sb, "#endif");
            return;
        }

        for (var index = 0; index < nonEmptyGroups.Length; index++)
        {
            var directive = index == 0 ? "#if" : "#elif";
            AppendConditionalDirective(sb, directive, nonEmptyGroups[index].Symbols);
            AppendMembers(sb, nonEmptyGroups[index].Members);
        }

        AppendIndentedDirective(sb, "#endif");
    }

    private static List<SegmentGroup> GroupSegmentBySymbols(IReadOnlyDictionary<string, ImmutableArray<string>> segmentBySymbol, string[] orderedSymbols)
    {
        var groupsByKey = new Dictionary<string, SegmentGroup>(StringComparer.Ordinal);
        var groups = new List<SegmentGroup>();
        foreach (var symbol in orderedSymbols)
        {
            var members = segmentBySymbol[symbol];
            var key = string.Join("\u001f", members);
            if (!groupsByKey.TryGetValue(key, out var group))
            {
                group = new SegmentGroup
                {
                    Members = members,
                };
                groupsByKey[key] = group;
                groups.Add(group);
            }

            group.Symbols.Add(symbol);
        }

        return groups;
    }

    private static void AppendMembers(StringBuilder sb, ImmutableArray<string> members)
    {
        foreach (var member in members)
        {
            AppendIndentedBlock(sb, member, 1);
        }
    }

    private static void AppendConditionalDirective(StringBuilder sb, string directive, IReadOnlyList<string> symbols)
    {
        var condition = string.Join(" || ", symbols);
        AppendIndentedDirective(sb, directive + " " + condition);
    }

    private static void AppendIndentedDirective(StringBuilder sb, string directive)
    {
        sb.Append(Indentation);
        sb.AppendLine(directive);
    }

    private static string BuildConditionalTypeSource(IReadOnlyDictionary<string, PublicApiTypeModel> typesBySymbol, string[] orderedSymbols)
    {
        var sourceGroups = GroupSources(typesBySymbol, orderedSymbols);
        if (sourceGroups.Count == 1 && sourceGroups[0].Symbols.Count == orderedSymbols.Length)
        {
            return sourceGroups[0].Source;
        }

        var sb = new StringBuilder();
        for (var index = 0; index < sourceGroups.Count; index++)
        {
            var directive = index == 0 ? "#if" : "#elif";
            var condition = string.Join(" || ", sourceGroups[index].Symbols);
            sb.Append(directive);
            sb.Append(' ');
            sb.AppendLine(condition);
            sb.Append(sourceGroups[index].Source);
        }

        sb.AppendLine("#endif");
        return sb.ToString();
    }

    private static List<SourceGroup> GroupSources(IReadOnlyDictionary<string, PublicApiTypeModel> typesBySymbol, string[] orderedSymbols)
    {
        var sourceGroupsBySource = new Dictionary<string, SourceGroup>(StringComparer.Ordinal);
        var sourceGroups = new List<SourceGroup>();
        foreach (var symbol in orderedSymbols)
        {
            if (!typesBySymbol.TryGetValue(symbol, out var type))
            {
                continue;
            }

            if (!sourceGroupsBySource.TryGetValue(type.Source, out var group))
            {
                group = new SourceGroup(type.Source);
                sourceGroupsBySource[type.Source] = group;
                sourceGroups.Add(group);
            }

            group.Symbols.Add(symbol);
        }

        return sourceGroups;
    }

    private static List<MemberAnchor> FindCommonAnchors(Dictionary<string, ImmutableArray<string>> membersBySymbol, string[] orderedSymbols)
    {
        var anchors = new List<MemberAnchor>();
        var firstSymbol = orderedSymbols[0];
        var referenceMembers = membersBySymbol[firstSymbol];
        var currentIndexesBySymbol = orderedSymbols.ToDictionary(symbol => symbol, _ => -1, StringComparer.Ordinal);
        foreach (var referenceMember in referenceMembers)
        {
            var indexesBySymbol = new Dictionary<string, int>(StringComparer.Ordinal);
            var foundInAllSymbols = true;
            foreach (var symbol in orderedSymbols)
            {
                var memberIndex = IndexOf(membersBySymbol[symbol], referenceMember, currentIndexesBySymbol[symbol] + 1);
                if (memberIndex < 0)
                {
                    foundInAllSymbols = false;
                    break;
                }

                indexesBySymbol[symbol] = memberIndex;
            }

            if (!foundInAllSymbols)
            {
                continue;
            }

            anchors.Add(new MemberAnchor(referenceMember, indexesBySymbol));
            foreach (var symbol in orderedSymbols)
            {
                currentIndexesBySymbol[symbol] = indexesBySymbol[symbol];
            }
        }

        return anchors;
    }

    private static int IndexOf(ImmutableArray<string> items, string value, int startIndex)
    {
        for (var index = startIndex; index < items.Length; index++)
        {
            if (string.Equals(items[index], value, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private static ImmutableArray<string> Slice(ImmutableArray<string> items, int startIndex, int endIndex)
    {
        if (startIndex >= endIndex)
        {
            return [];
        }

        var builder = ImmutableArray.CreateBuilder<string>(endIndex - startIndex);
        for (var index = startIndex; index < endIndex; index++)
        {
            builder.Add(items[index]);
        }

        return builder.MoveToImmutable();
    }

    private static bool TryParseTypeSource(string source, out ParsedTypeSource parsedType)
    {
        parsedType = null!;
        if (!TryGetOuterBraces(source, out var openingBraceIndex, out var closingBraceIndex))
        {
            return false;
        }

        var prefix = source[..(openingBraceIndex + 1)];
        if (!prefix.EndsWith("\n", StringComparison.Ordinal))
        {
            prefix += Environment.NewLine;
        }

        var body = source[(openingBraceIndex + 1)..closingBraceIndex];
        var suffix = source[closingBraceIndex..];
        if (!TryParseMembers(body, out var members))
        {
            return false;
        }

        parsedType = new ParsedTypeSource(prefix, [.. members], suffix);
        return true;
    }

    private static bool TryGetOuterBraces(string source, out int openingBraceIndex, out int closingBraceIndex)
    {
        openingBraceIndex = -1;
        closingBraceIndex = -1;
        var braceDepth = 0;
        var inString = false;
        var inChar = false;
        var escapeNext = false;
        for (var index = 0; index < source.Length; index++)
        {
            var character = source[index];
            if (inString)
            {
                if (escapeNext)
                {
                    escapeNext = false;
                }
                else if (character == '\\')
                {
                    escapeNext = true;
                }
                else if (character == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (inChar)
            {
                if (escapeNext)
                {
                    escapeNext = false;
                }
                else if (character == '\\')
                {
                    escapeNext = true;
                }
                else if (character == '\'')
                {
                    inChar = false;
                }

                continue;
            }

            if (character == '"')
            {
                inString = true;
                continue;
            }

            if (character == '\'')
            {
                inChar = true;
                continue;
            }

            if (character == '{')
            {
                if (braceDepth == 0)
                {
                    openingBraceIndex = index;
                }

                braceDepth++;
                continue;
            }

            if (character != '}')
            {
                continue;
            }

            braceDepth--;
            if (braceDepth == 0)
            {
                closingBraceIndex = index;
                return true;
            }

            if (braceDepth < 0)
            {
                return false;
            }
        }

        return false;
    }

    private static bool TryParseMembers(string body, out List<string> members)
    {
        members = new List<string>();
        var index = 0;
        while (index < body.Length)
        {
            while (index < body.Length && char.IsWhiteSpace(body[index]))
            {
                index++;
            }

            if (index >= body.Length)
            {
                return true;
            }

            var startIndex = index;
            var localBraceDepth = 0;
            var inString = false;
            var inChar = false;
            var escapeNext = false;
            while (index < body.Length)
            {
                var character = body[index];
                if (inString)
                {
                    if (escapeNext)
                    {
                        escapeNext = false;
                    }
                    else if (character == '\\')
                    {
                        escapeNext = true;
                    }
                    else if (character == '"')
                    {
                        inString = false;
                    }

                    index++;
                    continue;
                }

                if (inChar)
                {
                    if (escapeNext)
                    {
                        escapeNext = false;
                    }
                    else if (character == '\\')
                    {
                        escapeNext = true;
                    }
                    else if (character == '\'')
                    {
                        inChar = false;
                    }

                    index++;
                    continue;
                }

                if (character == '"')
                {
                    inString = true;
                    index++;
                    continue;
                }

                if (character == '\'')
                {
                    inChar = true;
                    index++;
                    continue;
                }

                if (character == '{')
                {
                    localBraceDepth++;
                    index++;
                    continue;
                }

                if (character == '}')
                {
                    if (localBraceDepth <= 0)
                    {
                        return false;
                    }

                    localBraceDepth--;
                    index++;
                    if (localBraceDepth == 0)
                    {
                        break;
                    }

                    continue;
                }

                index++;
                if (character == ';' && localBraceDepth == 0)
                {
                    break;
                }
            }

            if (index > body.Length)
            {
                return false;
            }

            var member = body[startIndex..index].Trim();
            if (member.Length == 0)
            {
                continue;
            }

            members.Add(RemoveSingleIndentationLevel(member));
        }

        return true;
    }

    private static string RemoveSingleIndentationLevel(string value)
    {
        var sb = new StringBuilder(value.Length);
        using var reader = new StringReader(value);
        string? line;
        var firstLine = true;
        while ((line = reader.ReadLine()) is not null)
        {
            if (!firstLine)
            {
                sb.AppendLine();
            }

            firstLine = false;
            if (line.StartsWith(Indentation, StringComparison.Ordinal))
            {
                sb.Append(line.AsSpan(Indentation.Length));
            }
            else if (line.StartsWith('\t'))
            {
                sb.Append(line.AsSpan(1));
            }
            else
            {
                sb.Append(line);
            }
        }

        return sb.ToString();
    }

    private static void AppendIndentedBlock(StringBuilder sb, string block, int indentationLevel)
    {
        using var reader = new StringReader(block);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length > 0)
            {
                sb.Append(' ', indentationLevel * Indentation.Length);
            }

            sb.AppendLine(line);
        }
    }

    private sealed record ParsedTypeSource(string Prefix, ImmutableArray<string> Members, string Suffix);

    private sealed record MemberAnchor(string Member, IReadOnlyDictionary<string, int> IndexesBySymbol);

    private sealed record SourceGroup(string Source)
    {
        public List<string> Symbols { get; } = [];
    }

    private sealed class SegmentGroup
    {
        public required ImmutableArray<string> Members { get; init; }

        public List<string> Symbols { get; } = [];
    }
}
