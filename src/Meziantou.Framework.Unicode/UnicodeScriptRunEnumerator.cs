using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meziantou.Framework;

/// <summary>Enumerates the runs of a text that are written in one script. See <see cref="Unicode.EnumerateScriptRuns(ReadOnlySpan{char})"/>.</summary>
public ref struct UnicodeScriptRunEnumerator
{
    private const int PendingScript = -1;

    private readonly ReadOnlySpan<char> _text;

    // Whether the characters below U+0300 are read without looking up their cluster: false in the tests that compare
    // it with the general case.
    private readonly bool _skipSimpleCharacters;

    private int _position;
    private UnicodeScriptRun _current;

    // The opening brackets whose closing bracket has not been seen: the closing bracket takes the script of the run
    // the opening one belongs to. Consecutive openings of the same bracket in the same run share an entry. When the
    // stack is full, the outermost entry is dropped: the closing brackets match the innermost openings first, and the
    // closing brackets of the dropped openings are script-neutral.
    private BracketStack _brackets;
    private int _bracketCount;

    internal UnicodeScriptRunEnumerator(ReadOnlySpan<char> text, bool skipSimpleCharacters = true)
    {
        _text = text;
        _skipSimpleCharacters = skipSimpleCharacters;
    }

    /// <summary>Gets the current run.</summary>
    public readonly UnicodeScriptRun Current => _current;

    /// <summary>Returns this instance, so that the enumerator can be used by a <see langword="foreach"/> statement.</summary>
    /// <returns>This instance.</returns>
    public readonly UnicodeScriptRunEnumerator GetEnumerator() => this;

    /// <summary>Moves to the next run.</summary>
    /// <returns><see langword="true"/> when there is a run; <see langword="false"/> at the end of the text.</returns>
    public bool MoveNext()
    {
        if (_position >= _text.Length)
            return false;

        var start = _position;

        // The scripts every cluster of the run can be written in; empty while only script-neutral clusters were seen.
        var candidates = default(ScriptSet);
        var hasCandidates = false;
        var firstScript = UnicodeScript.Common;
        var hasFirstScript = false;

        while (_position < _text.Length)
        {
            if (_skipSimpleCharacters && TrySkipSimpleCharacters(ref candidates, ref hasCandidates, ref firstScript, ref hasFirstScript))
                continue;

            var length = Unicode.GetGraphemeClusterLength(_text[_position..]);
            var cluster = _text.Slice(_position, length);
            var hasSet = TryGetClusterScripts(cluster, out var set, out var clusterScript, out var hasClusterScript);
            var closedBracket = -1;
            if (!hasSet)
            {
                // A script-neutral cluster: a closing bracket takes the script of its opening bracket.
                var codePoint = Unicode.DecodeAt(cluster, 0, out _);
                switch (UnicodeProperties.GetBidiPairedBracketType(codePoint))
                {
                    case UnicodeBidiPairedBracketType.Open when UnicodeProperties.TryGetBidiPairedBracket(codePoint, out var closing):
                        if (_bracketCount > 0 && _brackets[_bracketCount - 1].Closing == closing && _brackets[_bracketCount - 1].Script == PendingScript)
                        {
                            _brackets[_bracketCount - 1] = _brackets[_bracketCount - 1].WithCount(_brackets[_bracketCount - 1].Count + 1);
                        }
                        else
                        {
                            if (_bracketCount == BracketStack.Capacity)
                            {
                                Span<Bracket> brackets = _brackets;
                                brackets[1..].CopyTo(brackets);
                                _bracketCount--;
                            }

                            _brackets[_bracketCount++] = new Bracket(closing, PendingScript, count: 1);
                        }

                        break;

                    case UnicodeBidiPairedBracketType.Close:
                        for (var i = _bracketCount - 1; i >= 0; i--)
                        {
                            if (_brackets[i].Closing != codePoint)
                                continue;

                            var script = _brackets[i].Script;
                            closedBracket = i;
                            if (script != PendingScript)
                            {
                                set = default;
                                set.Add(script);
                                hasSet = true;
                                clusterScript = (UnicodeScript)script;
                                hasClusterScript = true;
                            }

                            break;
                        }

                        break;
                }
            }

            if (hasSet)
            {
                if (!hasCandidates)
                {
                    candidates = set;
                    hasCandidates = true;
                }
                else
                {
                    var common = candidates;
                    common.IntersectWith(set);
                    if (common.IsEmpty)
                    {
                        // The cluster starts the next run, where it is read again: a bracket it closes stays open until then.
                        break;
                    }

                    candidates = common;
                }

                if (hasClusterScript && !hasFirstScript)
                {
                    firstScript = clusterScript;
                    hasFirstScript = true;
                }
            }

            if (closedBracket >= 0)
            {
                // The closing bracket closes one opening of the entry, and the openings above it that were left open.
                var count = _brackets[closedBracket].Count;
                if (count > 1)
                {
                    _brackets[closedBracket] = _brackets[closedBracket].WithCount(count - 1);
                    _bracketCount = closedBracket + 1;
                }
                else
                {
                    _bracketCount = closedBracket;
                }
            }

            _position += length;
        }

        // The script of the run: the script of its first character that has one when every cluster can be written
        // in it, and the candidate with the lowest value otherwise.
        var resolved = !hasCandidates ? UnicodeScript.Common
            : hasFirstScript && candidates.Contains((int)firstScript) ? firstScript
            : (UnicodeScript)candidates.GetFirst();

        // The brackets opened in this run now have a script.
        for (var i = _bracketCount - 1; i >= 0 && _brackets[i].Script == PendingScript; i--)
        {
            _brackets[i] = new Bracket(_brackets[i].Closing, (int)resolved, _brackets[i].Count);
        }

        _current = new UnicodeScriptRun(start, _position - start, resolved);
        return true;
    }

    /// <summary>Skips the characters below U+0300 at the current position that are Latin letters or script-neutral, which is most of a text written with the Latin alphabet.</summary>
    /// <returns><see langword="false"/> when the current character is not one of them, or ends the run.</returns>
    /// <remarks>
    /// No character below U+0300 extends a grapheme cluster, so each of these characters is a cluster on its own. They
    /// have no Script_Extensions and are not brackets: the Latin letters set the run to Latin, and the Common characters
    /// do not change the run. The last of them is left when a character that can extend its cluster follows.
    /// </remarks>
    private bool TrySkipSimpleCharacters(ref ScriptSet candidates, ref bool hasCandidates, ref UnicodeScript firstScript, ref bool hasFirstScript)
    {
        if (_text[_position] >= SimpleCharacters.Limit)
            return false;

        var kinds = SimpleCharacters.Kinds;
        var remaining = _text[_position..];
        var length = 0;
        var firstLetter = -1;
        while ((uint)length < (uint)remaining.Length)
        {
            var c = remaining[length];
            if (c >= SimpleCharacters.Limit)
                break;

            var kind = kinds[c];
            if (kind is SimpleCharacters.Other)
                break;

            if (kind is SimpleCharacters.Latin && firstLetter < 0)
            {
                firstLetter = length;
            }

            length++;
        }

        if (length > 0 && length < remaining.Length && remaining[length] >= SimpleCharacters.Limit)
        {
            length--;
            if (firstLetter == length)
            {
                firstLetter = -1;
            }
        }

        var segment = remaining[..length];
        if (firstLetter >= 0)
        {
            if (hasCandidates && !candidates.Contains((int)UnicodeScript.Latin))
            {
                // The first letter starts the next run: skip the characters before it only.
                segment = segment[..firstLetter];
            }
            else
            {
                candidates = default;
                candidates.Add((int)UnicodeScript.Latin);
                hasCandidates = true;
                if (!hasFirstScript)
                {
                    firstScript = UnicodeScript.Latin;
                    hasFirstScript = true;
                }
            }
        }

        _position += segment.Length;
        return !segment.IsEmpty;
    }

    /// <summary>The kind of each character below U+0300, from the tables of the scripts and of the brackets.</summary>
    private static class SimpleCharacters
    {
        public const int Limit = 0x300;

        public const byte Other = 0;
        public const byte Neutral = 1;
        public const byte Latin = 2;

        public static readonly byte[] Kinds = Create();

        private static byte[] Create()
        {
            var kinds = new byte[Limit];
            for (var codePoint = 0; codePoint < Limit; codePoint++)
            {
                if (!UnicodeScripts.GetScriptExtensionsRaw(codePoint).IsEmpty || UnicodeProperties.GetBidiPairedBracketType(codePoint) is not UnicodeBidiPairedBracketType.None)
                    continue;

                kinds[codePoint] = UnicodeScripts.GetScript(codePoint) switch
                {
                    UnicodeScript.Latin => Latin,
                    UnicodeScript.Common => Neutral,
                    _ => Other,
                };
            }

            return kinds;
        }
    }

    /// <summary>Gets the scripts a cluster can be written in.</summary>
    /// <returns><see langword="false"/> when the cluster is script-neutral: it can be part of a run of any script.</returns>
    private static bool TryGetClusterScripts(ReadOnlySpan<char> cluster, out ScriptSet set, out UnicodeScript script, out bool hasScript)
    {
        set = default;
        script = UnicodeScript.Common;
        hasScript = false;
        var hasSet = false;
        for (var index = 0; index < cluster.Length;)
        {
            var codePoint = Unicode.DecodeAt(cluster, index, out var length);
            index += length;

            var value = UnicodeScripts.GetScript(codePoint);
            var isNeutral = value is UnicodeScript.Common or UnicodeScript.Inherited;
            var extensions = UnicodeScripts.GetScriptExtensionsRaw(codePoint);
            if (isNeutral && extensions.IsEmpty)
                continue;

            var current = default(ScriptSet);
            if (extensions.IsEmpty)
            {
                current.Add((int)value);
            }
            else
            {
                for (var i = 0; i < extensions.Length; i += 2)
                {
                    current.Add(extensions[i] | (extensions[i + 1] << 8));
                }
            }

            if (!isNeutral)
            {
                // The cluster is written in the script of its first character that has one.
                set = current;
                script = value;
                hasScript = true;
                return true;
            }

            // Script-neutral characters that are only used with some scripts narrow the choice.
            if (!hasSet)
            {
                set = current;
                hasSet = true;
            }
            else
            {
                var common = set;
                common.IntersectWith(current);
                if (!common.IsEmpty)
                {
                    set = common;
                }
            }
        }

        return hasSet;
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly struct Bracket
    {
        public Bracket(int closing, int script, int count)
        {
            Closing = closing;
            Script = script;
            Count = count;
        }

        public int Closing { get; }

        public int Script { get; }

        /// <summary>Gets the number of openings that are not closed yet.</summary>
        public int Count { get; }

        public Bracket WithCount(int count) => new(Closing, Script, count);
    }

    [InlineArray(Capacity)]
    private struct BracketStack
    {
        public const int Capacity = 32;

        private Bracket _first;
    }

    /// <summary>A set of scripts, by numeric value; the tables store a script in one byte.</summary>
    [StructLayout(LayoutKind.Auto)]
    private struct ScriptSet
    {
        private ulong _bits0;
        private ulong _bits1;
        private ulong _bits2;
        private ulong _bits3;

        public readonly bool IsEmpty => (_bits0 | _bits1 | _bits2 | _bits3) == 0;

        public void Add(int script)
        {
            var bit = 1UL << (script & 63);
            switch ((script >> 6) & 3)
            {
                case 0:
                    _bits0 |= bit;
                    break;

                case 1:
                    _bits1 |= bit;
                    break;

                case 2:
                    _bits2 |= bit;
                    break;

                default:
                    _bits3 |= bit;
                    break;
            }
        }

        public readonly bool Contains(int script)
        {
            var bit = 1UL << (script & 63);
            return (((script >> 6) & 3) switch
            {
                0 => _bits0,
                1 => _bits1,
                2 => _bits2,
                _ => _bits3,
            } & bit) != 0;
        }

        public void IntersectWith(ScriptSet other)
        {
            _bits0 &= other._bits0;
            _bits1 &= other._bits1;
            _bits2 &= other._bits2;
            _bits3 &= other._bits3;
        }

        public readonly int GetFirst()
        {
            if (_bits0 != 0)
                return BitOperations.TrailingZeroCount(_bits0);

            if (_bits1 != 0)
                return 64 + BitOperations.TrailingZeroCount(_bits1);

            if (_bits2 != 0)
                return 128 + BitOperations.TrailingZeroCount(_bits2);

            return 192 + BitOperations.TrailingZeroCount(_bits3);
        }
    }
}
