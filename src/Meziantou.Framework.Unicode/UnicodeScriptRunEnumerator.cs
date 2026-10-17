using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meziantou.Framework;

/// <summary>Enumerates the runs of a text that are written in one script. See <see cref="Unicode.EnumerateScriptRuns(ReadOnlySpan{char})"/>.</summary>
public ref struct UnicodeScriptRunEnumerator
{
    private const int PendingScript = -1;

    private readonly ReadOnlySpan<char> _text;
    private int _position;
    private UnicodeScriptRun _current;

    // The opening brackets whose closing bracket has not been seen: the closing bracket takes the script of the run
    // the opening one belongs to.
    private BracketStack _brackets;
    private int _bracketCount;

    internal UnicodeScriptRunEnumerator(ReadOnlySpan<char> text)
    {
        _text = text;
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
                    case UnicodeBidiPairedBracketType.Open when _bracketCount < BracketStack.Capacity && UnicodeProperties.TryGetBidiPairedBracket(codePoint, out var closing):
                        _brackets[_bracketCount++] = new Bracket(closing, PendingScript);
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
                _bracketCount = closedBracket;
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
            _brackets[i] = new Bracket(_brackets[i].Closing, (int)resolved);
        }

        _current = new UnicodeScriptRun(start, _position - start, resolved);
        return true;
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
        public Bracket(int closing, int script)
        {
            Closing = closing;
            Script = script;
        }

        public int Closing { get; }

        public int Script { get; }
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
