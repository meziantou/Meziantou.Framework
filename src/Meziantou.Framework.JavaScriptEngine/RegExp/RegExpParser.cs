using System.Globalization;
using System.Runtime.CompilerServices;
using Meziantou.Framework.Language.Regex;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// Parses a pattern with <c>Meziantou.Framework.Language.Regex</c> (the JavaScript dialect, which reports the early
/// errors) and lowers the syntax tree to <see cref="RegExpNode"/>, applying the compile-time semantics of the
/// specification: CompileToCharSet, CharacterComplement, MaybeSimpleCaseFolding, the property escapes...
/// </summary>
internal sealed class RegExpParser
{
    private const int MaxNestingDepth = 256;

    private static CodePointSet? s_unicodeSetsIgnoreCaseAllCharacters;
    private static CodePointSet? s_whiteSpace;

    private readonly bool _unicode;
    private readonly bool _unicodeSets;
    private readonly Dictionary<string, List<int>> _namedGroups = new(StringComparer.Ordinal);
    private int _groupCount;
    private string? _error;

    private RegExpParser(RegExpFlags flags)
    {
        _unicode = flags.HasEitherUnicodeFlag();
        _unicodeSets = (flags & RegExpFlags.UnicodeSets) != 0;
    }

    /// <summary>Parses a pattern.</summary>
    /// <param name="pattern">The pattern.</param>
    /// <param name="flags">The flags.</param>
    /// <param name="result">The pattern, its groups and its group names.</param>
    /// <param name="error">The message of the SyntaxError when the pattern is not valid.</param>
    public static bool TryParse(string pattern, RegExpFlags flags, out RegExpParseResult? result, out string? error)
    {
        var options = RegexPatternOptions.None;
        if ((flags & RegExpFlags.IgnoreCase) != 0)
        {
            options |= RegexPatternOptions.IgnoreCase;
        }

        if ((flags & RegExpFlags.Multiline) != 0)
        {
            options |= RegexPatternOptions.Multiline;
        }

        if ((flags & RegExpFlags.DotAll) != 0)
        {
            options |= RegexPatternOptions.DotAll | RegexPatternOptions.Singleline;
        }

        if ((flags & RegExpFlags.Unicode) != 0)
        {
            options |= RegexPatternOptions.Unicode;
        }

        if ((flags & RegExpFlags.UnicodeSets) != 0)
        {
            options |= RegexPatternOptions.Unicode | RegexPatternOptions.UnicodeSets;
        }

        var tree = RegexSyntaxTree.ParseText(pattern, new RegexParseOptions(RegexDialect.JavaScript) { PatternOptions = options, MaxRecursionDepth = MaxNestingDepth });
        var diagnostics = tree.GetDiagnostics();
        if (diagnostics.Count > 0)
        {
            result = null;
            error = diagnostics[0].Message;
            return false;
        }

        var parser = new RegExpParser(flags);
        var groupNames = new string?[tree.Captures.Count + 1];
        foreach (var node in tree.GetRoot().DescendantNodes())
        {
            if (node is RegexNamedGroupSyntax named)
            {
                if (named.Number >= groupNames.Length)
                {
                    result = null;
                    error = "Invalid capture group";
                    return false;
                }

                groupNames[named.Number] = named.Name;
                if (!parser._namedGroups.TryGetValue(named.Name, out var numbers))
                {
                    numbers = [];
                    parser._namedGroups.Add(named.Name, numbers);
                }

                numbers.Add(named.Number);
            }
        }

        var root = parser.CompileAlternation(tree.GetRoot().Alternation, depth: 0);
        if (parser._error is null && parser._groupCount != tree.Captures.Count)
        {
            parser._error = "Invalid capture group";
        }

        if (parser._error is not null)
        {
            result = null;
            error = parser._error;
            return false;
        }

        result = new RegExpParseResult(root, parser._groupCount, groupNames);
        error = null;
        return true;
    }

    private void SetError(string message) => _error ??= message;

    private RegExpNode CompileAlternation(RegexAlternationSyntax alternation, int depth)
    {
        if (depth > MaxNestingDepth || !RuntimeHelpers.TryEnsureSufficientExecutionStack())
        {
            SetError("Regular expression too large");
            return RegExpEmptyNode.Instance;
        }

        var branches = alternation.Branches;
        if (branches.Count == 1)
            return CompileSequence(branches[0], depth);

        var alternatives = new RegExpNode[branches.Count];
        for (var i = 0; i < alternatives.Length; i++)
        {
            alternatives[i] = CompileSequence(branches[i], depth);
        }

        return new RegExpAlternationNode(alternatives);
    }

    private RegExpNode CompileSequence(RegexSequenceSyntax sequence, int depth)
    {
        var terms = sequence.Terms;
        if (terms.Count == 0)
            return RegExpEmptyNode.Instance;

        var items = new List<RegExpNode>(terms.Count);
        foreach (var term in terms)
        {
            var node = CompileTerm(term, depth);
            if (node is RegExpSequenceNode nested)
            {
                items.AddRange(nested.Items);
            }
            else if (node is not RegExpEmptyNode)
            {
                items.Add(node);
            }
        }

        return items.Count switch
        {
            0 => RegExpEmptyNode.Instance,
            1 => items[0],
            _ => new RegExpSequenceNode([.. items]),
        };
    }

    private RegExpNode CompileTerm(RegexTermSyntax term, int depth)
    {
        var ignoreCase = (term.Options & RegexPatternOptions.IgnoreCase) != 0;
        switch (term)
        {
            case RegexQuantifiedSyntax quantified:
                {
                    var before = _groupCount;
                    var atom = CompileTerm(quantified.Term, depth);
                    var quantifier = quantified.Quantifier;
                    if (quantifier.Mode == RegexQuantifierMode.Possessive)
                    {
                        SetError("Nothing to repeat");
                    }

                    var max = quantifier.MaxCount ?? RegExpQuantifierNode.Infinite;
                    var greedy = quantifier.Mode == RegexQuantifierMode.Greedy;

                    // Annex B: "\c" not followed by a control letter is the atom "\" followed by the atom "c", and
                    // the quantifier applies to the "c" only
                    if (atom is RegExpSequenceNode { Items: [.. var prefix, var last] } && quantified.Term is RegexCharacterEscapeSyntax)
                        return new RegExpSequenceNode([.. prefix, new RegExpQuantifierNode(last, quantifier.MinCount, max, greedy, before + 1, 0)]);

                    return new RegExpQuantifierNode(atom, quantifier.MinCount, max, greedy, before + 1, _groupCount - before);
                }

            case RegexLiteralSyntax literal:
                return new RegExpCharNode(literal.CodePoint, ignoreCase);

            case RegexCharacterEscapeSyntax escape:
                {
                    var value = escape.Value;
                    var items = new List<RegExpNode>();
                    foreach (var codePoint in EnumerateCharacters(value))
                    {
                        items.Add(new RegExpCharNode(codePoint, ignoreCase));
                    }

                    return items.Count switch
                    {
                        0 => Error(term),
                        1 => items[0],
                        _ => new RegExpSequenceNode([.. items]),
                    };
                }

            case RegexAnyCharacterSyntax:
                return new RegExpDotNode((term.Options & (RegexPatternOptions.DotAll | RegexPatternOptions.Singleline)) != 0);

            case RegexCharacterClassEscapeSyntax classEscape:
                {
                    var set = CompileClassEscape(classEscape, ignoreCase);
                    return new RegExpSetNode(set, invert: false, ignoreCase);
                }

            case RegexUnicodeCategorySyntax property:
                {
                    var (set, strings) = CompileProperty(property, ignoreCase);
                    return CreateClassMatcher(set, strings, invert: false, ignoreCase);
                }

            case RegexCharacterClassSyntax characterClass:
                return CompileCharacterClass(characterClass, ignoreCase, depth);

            case RegexAnchorSyntax anchor:
                {
                    var multiline = (term.Options & RegexPatternOptions.Multiline) != 0;
                    return anchor.AnchorKind switch
                    {
                        RegexAnchorKind.Caret => new RegExpAssertionNode(RegExpAssertionKind.Start, multiline, ignoreCase),
                        RegexAnchorKind.Dollar => new RegExpAssertionNode(RegExpAssertionKind.End, multiline, ignoreCase),
                        RegexAnchorKind.WordBoundary => new RegExpAssertionNode(RegExpAssertionKind.WordBoundary, multiline, ignoreCase),
                        RegexAnchorKind.NonWordBoundary => new RegExpAssertionNode(RegExpAssertionKind.NotWordBoundary, multiline, ignoreCase),
                        _ => Error(term),
                    };
                }

            case RegexCapturingGroupSyntax group:
                {
                    _groupCount++;
                    var index = _groupCount;
                    if (group.Number != index)
                    {
                        SetError("Invalid capture group");
                    }

                    return new RegExpGroupNode(index, CompileAlternation(group.Alternation, depth + 1));
                }

            case RegexNamedGroupSyntax group:
                {
                    _groupCount++;
                    var index = _groupCount;
                    if (group.Number != index)
                    {
                        SetError("Invalid capture group");
                    }

                    return new RegExpGroupNode(index, CompileAlternation(group.Alternation, depth + 1));
                }

            case RegexNonCapturingGroupSyntax group:
                return CompileAlternation(group.Alternation, depth + 1);

            case RegexOptionsGroupSyntax group:
                return CompileAlternation(group.Alternation, depth + 1);

            case RegexLookaroundSyntax lookaround:
                {
                    var before = _groupCount;
                    var body = CompileAlternation(lookaround.Alternation, depth + 1);
                    return new RegExpLookaroundNode(lookaround.IsLookbehind, lookaround.IsNegative, body, before + 1, _groupCount - before);
                }

            case RegexBackreferenceSyntax backreference:
                {
                    var number = backreference.Number;
                    if (number <= 0)
                        return Error(term);

                    return new RegExpBackreferenceNode([number], ignoreCase);
                }

            case RegexNamedBackreferenceSyntax backreference:
                {
                    if (!_namedGroups.TryGetValue(backreference.Name, out var groups))
                        return Error(term);

                    return new RegExpBackreferenceNode([.. groups], ignoreCase);
                }

            default:
                return Error(term);
        }
    }

    private RegExpEmptyNode Error(RegexSyntaxNode node)
    {
        SetError("Invalid regular expression: '" + node + "'");
        return RegExpEmptyNode.Instance;
    }

    /// <summary>Enumerates the characters of a value: the code points under "u" or "v", else the code units.</summary>
    private IEnumerable<int> EnumerateCharacters(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (_unicode && char.IsHighSurrogate(value[i]) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                yield return char.ConvertToUtf32(value[i], value[i + 1]);
                i++;
            }
            else
            {
                yield return value[i];
            }
        }
    }

    /// <summary>Gets the single character a class atom stands for (a range endpoint).</summary>
    private int GetSingleCharacter(RegexSyntaxNode? node)
    {
        switch (node)
        {
            case RegexLiteralSyntax literal:
                return literal.CodePoint;

            case RegexCharacterEscapeSyntax escape:
                {
                    var value = escape.Value;
                    if (value.Length == 1)
                        return value[0];

                    if (value.Length == 2 && char.IsSurrogatePair(value[0], value[1]))
                        return _unicode ? char.ConvertToUtf32(value[0], value[1]) : value[0];

                    break;
                }
        }

        SetError("Invalid character class");
        return 0;
    }

    /// <summary>AllCharacters(rer)</summary>
    private CodePointSet AllCharacters(bool ignoreCase)
    {
        if (_unicodeSets && ignoreCase)
            return s_unicodeSetsIgnoreCaseAllCharacters ?? CreateUnicodeSetsIgnoreCaseAllCharacters();

        return CodePointSet.Range(0, _unicode ? CodePointSet.MaxCodePoint : 0xFFFF);

        static CodePointSet CreateUnicodeSetsIgnoreCaseAllCharacters()
        {
            var folded = RegExpCaseFolding.GetSimpleCaseFoldings().CodePoints;
            var set = new CodePointSet();
            foreach (var codePoint in folded)
            {
                set.Add(codePoint);
            }

            var result = set.Complement(CodePointSet.MaxCodePoint);
            _ = result.RangeCount;
            Volatile.Write(ref s_unicodeSetsIgnoreCaseAllCharacters, result);
            return result;
        }
    }

    /// <summary>CharacterComplement(rer, set)</summary>
    private CodePointSet CharacterComplement(CodePointSet set, bool ignoreCase) => CodePointSet.Subtract(AllCharacters(ignoreCase), set);

    /// <summary>MaybeSimpleCaseFolding(rer, set) for the single characters.</summary>
    private CodePointSet MaybeSimpleCaseFolding(CodePointSet set, bool ignoreCase)
    {
        if (!_unicodeSets || !ignoreCase)
            return set;

        // scf only changes the code points that have a mapping; the others stay
        var (codePoints, folded) = RegExpCaseFolding.GetSimpleCaseFoldings();
        var result = set.Clone();
        var changed = new CodePointSet();
        var removed = new CodePointSet();
        for (var i = 0; i < codePoints.Length; i++)
        {
            if (set.Contains(codePoints[i]))
            {
                removed.Add(codePoints[i]);
                changed.Add(folded[i]);
            }
        }

        if (removed.IsEmpty)
            return result;

        result = CodePointSet.Subtract(result, removed);
        result.AddSet(changed);
        return result;
    }

    private string MaybeSimpleCaseFolding(string value, bool ignoreCase)
    {
        if (!_unicodeSets || !ignoreCase)
            return value;

        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var codePoint in EnumerateCharacters(value))
        {
            AppendCodePoint(builder, RegExpCaseFolding.CanonicalizeUnicode(codePoint));
        }

        return builder.ToString();
    }

    private static void AppendCodePoint(System.Text.StringBuilder builder, int codePoint)
    {
        if (codePoint > 0xFFFF)
        {
            builder.Append(char.ConvertFromUtf32(codePoint));
        }
        else
        {
            builder.Append((char)codePoint);
        }
    }

    /// <summary>WordCharacters(rer): the ASCII word characters, and the characters whose canonical value is one of them.</summary>
    private CodePointSet WordCharacters(bool ignoreCase)
    {
        var set = CreateBasicWordCharacters();
        if (_unicode && ignoreCase)
        {
            var (codePoints, folded) = RegExpCaseFolding.GetSimpleCaseFoldings();
            for (var i = 0; i < codePoints.Length; i++)
            {
                if (IsBasicWordCharacter(folded[i]) && !IsBasicWordCharacter(codePoints[i]))
                {
                    set.Add(codePoints[i]);
                }
            }
        }

        return set;
    }

    private static CodePointSet CreateBasicWordCharacters()
    {
        var set = new CodePointSet();
        set.AddRange('0', '9');
        set.AddRange('A', 'Z');
        set.Add('_');
        set.AddRange('a', 'z');
        return set;
    }

    public static bool IsBasicWordCharacter(int c) => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_';

    /// <summary>The characters of WhiteSpace and LineTerminator (a shared set: it must not be modified).</summary>
    public static CodePointSet CreateWhiteSpaceSet() => Volatile.Read(ref s_whiteSpace) ?? CreateWhiteSpaceSetCore();

    private static CodePointSet CreateWhiteSpaceSetCore()
    {
        var set = new CodePointSet();
        set.AddRange(0x09, 0x0D); // TAB, LF, VT, FF, CR
        set.Add(0x20);
        set.Add(0xA0);
        set.Add(0x1680);
        set.AddRange(0x2000, 0x200A);
        set.AddRange(0x2028, 0x2029);
        set.Add(0x202F);
        set.Add(0x205F);
        set.Add(0x3000);
        set.Add(0xFEFF);

        // The other characters of the Zs category, if a later version of Unicode adds some
        for (var c = 0; c <= 0xFFFF; c++)
        {
            if (UnicodeProperties.GetGeneralCategory(c) == UnicodeCategory.SpaceSeparator)
            {
                set.Add(c);
            }
        }

        _ = set.RangeCount;
        Volatile.Write(ref s_whiteSpace, set);
        return set;
    }

    private CodePointSet CompileClassEscape(RegexCharacterClassEscapeSyntax escape, bool ignoreCase)
    {
        var text = escape.EscapeToken.Text;
        var letter = text.Length == 2 ? text[1] : '\0';
        switch (letter)
        {
            case 'd':
                return CodePointSet.Range('0', '9');
            case 'D':
                return CharacterComplement(CodePointSet.Range('0', '9'), ignoreCase);
            case 's':
                return CreateWhiteSpaceSet();
            case 'S':
                return CharacterComplement(CreateWhiteSpaceSet(), ignoreCase);
            case 'w':
                return MaybeSimpleCaseFolding(WordCharacters(ignoreCase), ignoreCase);
            case 'W':
                return CharacterComplement(MaybeSimpleCaseFolding(WordCharacters(ignoreCase), ignoreCase), ignoreCase);
            default:
                SetError("Invalid escape");
                return new CodePointSet();
        }
    }

    private (CodePointSet Set, IReadOnlyCollection<string> Strings) CompileProperty(RegexUnicodeCategorySyntax property, bool ignoreCase)
    {
        var kind = RegExpUnicodeProperties.Resolve(property.Name, _unicodeSets, out var set, out var strings);
        if (kind == RegExpUnicodeProperties.PropertyKind.Unknown)
        {
            SetError("Invalid property name");
            return (new CodePointSet(), []);
        }

        if (property.IsNegated)
        {
            if (strings.Length > 0)
            {
                SetError("Invalid property name");
            }

            // CharacterComplement(rer, CompileToCharSet of UnicodePropertyValueExpression)
            var expression = kind == RegExpUnicodeProperties.PropertyKind.GeneralCategory ? set : MaybeSimpleCaseFolding(set, ignoreCase);
            return (CharacterComplement(expression, ignoreCase), []);
        }

        if (kind == RegExpUnicodeProperties.PropertyKind.GeneralCategory)
            return (set, []);

        if (strings.Length == 0)
            return (MaybeSimpleCaseFolding(set, ignoreCase), []);

        var foldedStrings = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in strings)
        {
            foldedStrings.Add(MaybeSimpleCaseFolding(value, ignoreCase));
        }

        return (MaybeSimpleCaseFolding(set, ignoreCase), foldedStrings);
    }

    private RegExpNode CompileCharacterClass(RegexCharacterClassSyntax characterClass, bool ignoreCase, int depth)
    {
        if (_unicodeSets)
        {
            var value = CompileClassSet(characterClass, ignoreCase, depth);
            return CreateClassMatcher(value.Singles, value.Strings, invert: false, ignoreCase);
        }

        var set = new CodePointSet();
        foreach (var member in characterClass.Members)
        {
            switch (member)
            {
                case RegexLiteralSyntax literal:
                    set.Add(literal.CodePoint);
                    break;

                case RegexCharacterEscapeSyntax escape:
                    foreach (var c in EnumerateCharacters(escape.Value))
                    {
                        set.Add(c);
                    }

                    break;

                case RegexCharacterRangeSyntax range:
                    {
                        var start = GetSingleCharacter(range.Start);
                        var end = GetSingleCharacter(range.End);
                        if (start > end)
                        {
                            SetError("Range out of order in character class");
                        }

                        set.AddRange(start, end);
                        break;
                    }

                case RegexCharacterClassEscapeSyntax classEscape:
                    set.AddSet(CompileClassEscape(classEscape, ignoreCase));
                    break;

                case RegexUnicodeCategorySyntax property:
                    set.AddSet(CompileProperty(property, ignoreCase).Set);
                    break;

                default:
                    Error(member);
                    break;
            }
        }

        return new RegExpSetNode(set, characterClass.IsNegated, ignoreCase);
    }

    /// <summary>Atom :: CharacterClass under the "v" flag: the strings, longest first, then the single characters, then the empty string.</summary>
    private static RegExpNode CreateClassMatcher(CodePointSet singles, IReadOnlyCollection<string> strings, bool invert, bool ignoreCase)
    {
        if (strings.Count == 0)
            return new RegExpSetNode(singles, invert, ignoreCase);

        var sequences = new List<int[]>();
        var hasEmpty = false;
        foreach (var value in strings)
        {
            if (value.Length == 0)
            {
                hasEmpty = true;
                continue;
            }

            var codePoints = new List<int>();
            for (var i = 0; i < value.Length; i++)
            {
                if (char.IsHighSurrogate(value[i]) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                {
                    codePoints.Add(char.ConvertToUtf32(value[i], value[i + 1]));
                    i++;
                }
                else
                {
                    codePoints.Add(value[i]);
                }
            }

            sequences.Add([.. codePoints]);
        }

        // Longest first; the order of the strings of the same length does not change the result
        sequences.Sort((a, b) => b.Length.CompareTo(a.Length));
        var alternatives = new List<RegExpNode>(sequences.Count + 2);
        foreach (var sequence in sequences)
        {
            var items = new RegExpNode[sequence.Length];
            for (var i = 0; i < sequence.Length; i++)
            {
                items[i] = new RegExpCharNode(sequence[i], ignoreCase);
            }

            alternatives.Add(items.Length == 1 ? items[0] : new RegExpSequenceNode(items));
        }

        alternatives.Add(new RegExpSetNode(singles, invert: false, ignoreCase));
        if (hasEmpty)
        {
            alternatives.Add(RegExpEmptyNode.Instance);
        }

        return new RegExpAlternationNode([.. alternatives]);
    }

    /// <summary>CompileToCharSet of the contents of a class under the "v" flag.</summary>
    private ClassSetValue CompileClassSet(RegexCharacterClassSyntax characterClass, bool ignoreCase, int depth)
    {
        if (depth > MaxNestingDepth || !RuntimeHelpers.TryEnsureSufficientExecutionStack())
        {
            SetError("Regular expression too large");
            return new ClassSetValue();
        }

        var result = new ClassSetValue();
        foreach (var member in characterClass.Members)
        {
            result.UnionWith(CompileClassSetOperand(member, ignoreCase, depth + 1));
        }

        if (characterClass.IsNegated)
        {
            if (result.Strings.Count > 0)
            {
                SetError("Negated character class may contain strings");
            }

            return new ClassSetValue(CharacterComplement(result.Singles, ignoreCase));
        }

        return result;
    }

    private ClassSetValue CompileClassSetOperand(RegexSyntaxNode node, bool ignoreCase, int depth)
    {
        switch (node)
        {
            case RegexClassSetOperationSyntax operation:
                {
                    var operands = operation.Operands;
                    var result = CompileClassSetOperand(operands[0], ignoreCase, depth);
                    for (var i = 1; i < operands.Count; i++)
                    {
                        var other = CompileClassSetOperand(operands[i], ignoreCase, depth);
                        result = operation.IsIntersection ? ClassSetValue.Intersect(result, other) : ClassSetValue.Subtract(result, other);
                    }

                    return result;
                }

            case RegexCharacterRangeSyntax range:
                {
                    var start = GetSingleCharacter(range.Start);
                    var end = GetSingleCharacter(range.End);
                    if (start > end)
                    {
                        SetError("Range out of order in character class");
                    }

                    return new ClassSetValue(MaybeSimpleCaseFolding(CodePointSet.Range(start, end), ignoreCase));
                }

            case RegexLiteralSyntax literal:
                return new ClassSetValue(MaybeSimpleCaseFolding(CodePointSet.Single(literal.CodePoint), ignoreCase));

            case RegexCharacterEscapeSyntax escape:
                return new ClassSetValue(MaybeSimpleCaseFolding(CodePointSet.Single(GetSingleCharacter(escape)), ignoreCase));

            case RegexCharacterClassSyntax nested:
                return CompileClassSet(nested, ignoreCase, depth);

            case RegexClassStringLiteralSyntax strings:
                {
                    var result = new ClassSetValue();
                    foreach (var value in DecodeClassStrings(strings.TextToken.Text))
                    {
                        result.Add(MaybeSimpleCaseFolding(value, ignoreCase));
                    }

                    return result;
                }

            case RegexCharacterClassEscapeSyntax classEscape:
                return new ClassSetValue(CompileClassEscape(classEscape, ignoreCase));

            case RegexUnicodeCategorySyntax property:
                {
                    var (set, strings) = CompileProperty(property, ignoreCase);
                    var result = new ClassSetValue(set);
                    foreach (var value in strings)
                    {
                        result.Add(value);
                    }

                    return result;
                }

            default:
                Error(node);
                return new ClassSetValue();
        }
    }

    /// <summary>Decodes the alternatives of <c>\q{...}</c>: ClassSetCharacter sequences separated by <c>|</c>.</summary>
    private List<string> DecodeClassStrings(string text)
    {
        var result = new List<string>();
        var builder = new System.Text.StringBuilder();
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '|')
            {
                result.Add(builder.ToString());
                builder.Clear();
                i++;
                continue;
            }

            if (c != '\\')
            {
                builder.Append(c);
                i++;
                continue;
            }

            i++;
            if (i >= text.Length)
            {
                SetError("Invalid escape");
                break;
            }

            c = text[i];
            i++;
            switch (c)
            {
                case 'b':
                    builder.Append('\b');
                    break;
                case 'f':
                    builder.Append('\f');
                    break;
                case 'n':
                    builder.Append('\n');
                    break;
                case 'r':
                    builder.Append('\r');
                    break;
                case 't':
                    builder.Append('\t');
                    break;
                case 'v':
                    builder.Append('\v');
                    break;
                case '0':
                    builder.Append('\0');
                    break;
                case 'c' when i < text.Length && char.IsAsciiLetter(text[i]):
                    builder.Append((char)(text[i] % 32));
                    i++;
                    break;
                case 'x' when i + 2 <= text.Length && int.TryParse(text.AsSpan(i, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var hex):
                    builder.Append((char)hex);
                    i += 2;
                    break;
                case 'u' when i < text.Length && text[i] == '{':
                    {
                        var end = text.AsSpan(i).IndexOf('}');
                        if (end >= 0)
                        {
                            end += i;
                        }

                        if (end < 0 || !int.TryParse(text.AsSpan(i + 1, end - i - 1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var codePoint) || codePoint > CodePointSet.MaxCodePoint)
                        {
                            SetError("Invalid Unicode escape");
                            return result;
                        }

                        AppendCodePoint(builder, codePoint);
                        i = end + 1;
                        break;
                    }

                case 'u' when i + 4 <= text.Length && int.TryParse(text.AsSpan(i, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var unit):
                    builder.Append((char)unit);
                    i += 4;
                    break;
                default:
                    if (char.IsHighSurrogate(c) && i < text.Length && char.IsLowSurrogate(text[i]))
                    {
                        builder.Append(c).Append(text[i]);
                        i++;
                    }
                    else
                    {
                        builder.Append(c);
                    }

                    break;
            }
        }

        result.Add(builder.ToString());
        return result;
    }

    /// <summary>A CharSet under the "v" flag: single characters, and the strings that are not one character long.</summary>
    private sealed class ClassSetValue
    {
        public ClassSetValue()
            : this(new CodePointSet())
        {
        }

        public ClassSetValue(CodePointSet singles)
        {
            Singles = singles;
        }

        public CodePointSet Singles { get; private set; }

        public HashSet<string> Strings { get; } = new(StringComparer.Ordinal);

        public void Add(string value)
        {
            if (value.Length == 1)
            {
                AddSingle(value[0]);
            }
            else if (value.Length == 2 && char.IsSurrogatePair(value[0], value[1]))
            {
                AddSingle(char.ConvertToUtf32(value[0], value[1]));
            }
            else
            {
                Strings.Add(value);
            }
        }

        private void AddSingle(int value)
        {
            Singles = Singles.Clone();
            Singles.Add(value);
        }

        public void UnionWith(ClassSetValue other)
        {
            Singles = CodePointSet.Union(Singles, other.Singles);
            Strings.UnionWith(other.Strings);
        }

        public static ClassSetValue Intersect(ClassSetValue left, ClassSetValue right)
        {
            var result = new ClassSetValue(CodePointSet.Intersect(left.Singles, right.Singles));
            foreach (var value in left.Strings)
            {
                if (right.Strings.Contains(value))
                {
                    result.Strings.Add(value);
                }
            }

            return result;
        }

        public static ClassSetValue Subtract(ClassSetValue left, ClassSetValue right)
        {
            var result = new ClassSetValue(CodePointSet.Subtract(left.Singles, right.Singles));
            foreach (var value in left.Strings)
            {
                if (!right.Strings.Contains(value))
                {
                    result.Strings.Add(value);
                }
            }

            return result;
        }
    }
}

/// <summary>A parsed pattern: the root node, the number of capturing groups and the name of each group (index 0 unused).</summary>
internal sealed record RegExpParseResult(RegExpNode Root, int GroupCount, string?[] GroupNames);
