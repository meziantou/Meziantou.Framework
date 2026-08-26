// Portions of this file are derived from dotnet/runtime, licensed to the .NET Foundation under the MIT license.
// See THIRD-PARTY-NOTICES.TXT in the project root.
//
// Source: src/libraries/System.Text.RegularExpressions/src/System/Text/RegularExpressions/RegexParser.cs
// Commit: 5ec6efc171b19c0e2d591fbd451920e8f43a1552
// Permalink: https://github.com/dotnet/runtime/blob/5ec6efc171b19c0e2d591fbd451920e8f43a1552/src/libraries/System.Text.RegularExpressions/src/System/Text/RegularExpressions/RegexParser.cs
//
// Changes: NoteCaptureSlot, NoteCaptureName, and AssignNameSlots keep their algorithm; the sparse-to-dense remapping
// that only the matching engine needed is not here, and Hashtable is replaced by typed dictionaries.

using System.Globalization;

namespace Meziantou.Framework.Language.Regex.Internals;

/// <summary>The capture groups a pattern declares, and the numbers the engine assigns them.</summary>
/// <remarks>
/// Built by a pass over the whole pattern before any of it is parsed, because whether <c>\1</c> is a backreference or
/// an octal escape, and whether <c>(?(1)…)</c> tests a group or matches an expression, depend on groups that may be
/// declared later.
/// </remarks>
internal sealed class RegexCaptureTable
{
    public static RegexCaptureTable Empty { get; } = new([], [], new Dictionary<string, int>(StringComparer.Ordinal));

    private readonly Dictionary<int, int> _positions;
    private readonly Dictionary<string, int> _numbersByName;

    private RegexCaptureTable(IReadOnlyList<int> numbers, Dictionary<int, int> positions, Dictionary<string, int> numbersByName)
    {
        Numbers = numbers;
        _positions = positions;
        _numbersByName = numbersByName;
    }

    /// <summary>Every capture number the pattern uses, in ascending order. Group 0 is not included.</summary>
    public IReadOnlyList<int> Numbers { get; }

    public bool ContainsNumber(int number) => _positions.ContainsKey(number);

    public bool TryGetNumber(string name, out int number) => _numbersByName.TryGetValue(name, out number);

    /// <summary>The name of a group, which is the number written out when the group has none.</summary>
    public string GetName(int number)
    {
        foreach (var pair in _numbersByName)
        {
            if (pair.Value == number)
                return pair.Key;
        }

        return number.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>The position of the <c>(</c> that declares a group.</summary>
    public int GetPosition(int number) => _positions.TryGetValue(number, out var position) ? position : 0;

    /// <summary>Collects capture slots and names, then assigns numbers to the names.</summary>
    internal sealed class Builder
    {
        private readonly Dictionary<int, int> _caps = [];
        private readonly Dictionary<string, int> _capnamePositions = new(StringComparer.Ordinal);
        private readonly List<string> _capnamelist = [];

        private int _autocap = 1;

        /// <summary>Notes a used capture slot.</summary>
        public void NoteSlot(int number, int position)
        {
            if (number < 0)
                return;

            _caps.TryAdd(number, position);
        }

        /// <summary>Notes an unnamed group, taking the next free number.</summary>
        public int NoteAutoSlot(int position)
        {
            var number = _autocap++;
            NoteSlot(number, position);

            return number;
        }

        /// <summary>Notes a named group.</summary>
        public void NoteName(string name, int position)
        {
            if (_capnamePositions.TryAdd(name, position))
            {
                _capnamelist.Add(name);
            }
        }

        /// <summary>
        /// Assigns the first free numbers to the names, then builds the table.
        /// </summary>
        /// <remarks>
        /// Named groups are numbered after every explicitly numbered one, so <c>x</c> in <c>(a)(?&lt;x&gt;b)(c)</c> is
        /// group 3, not group 2. Callers see the result through <see cref="TryGetNumber"/>, so nothing else has to know.
        /// </remarks>
        public RegexCaptureTable Build()
        {
            var numbersByName = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var name in _capnamelist)
            {
                while (_caps.ContainsKey(_autocap))
                {
                    _autocap++;
                }

                var position = _capnamePositions[name];
                numbersByName[name] = _autocap;
                NoteSlot(_autocap, position);
                _autocap++;
            }

            var numbers = new List<int>(_caps.Count);
            foreach (var number in _caps.Keys)
            {
                if (number != 0)
                {
                    numbers.Add(number);
                }
            }

            numbers.Sort();

            return new RegexCaptureTable(numbers, _caps, numbersByName);
        }
    }
}
